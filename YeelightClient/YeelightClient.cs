namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// Finds Yeelight devices on the local network, keeps the list of them, and keeps a connection to each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where you start. Make a client, tell it to look for lights, and control the ones that it finds:
    /// </para>
    /// <code language="csharp">
    /// using var client = new YeelightClient();
    /// client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device}");
    /// client.StartDiscovery();
    ///
    /// var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(3));
    /// foreach (var light in lights)
    /// {
    ///     await light.TurnOnAsync();
    ///     await light.SetBrightnessAsync(60, TimeSpan.FromSeconds(1));
    /// }
    /// </code>
    /// <para>
    /// Lights are looked for in two ways, and the client does both: it <b>searches</b> (a message to the multicast group of the lights and as
    /// a broadcast, from every network that this computer is on, to which every light answers) and it <b>listens</b> to what the lights say by
    /// themselves, which they do now and then (<see cref="StartDiscovery"/> and <see cref="StartListening"/>). A light that is found
    /// is a <see cref="YeelightDevice"/> in <see cref="Devices"/>, and <see cref="DeviceDiscovered"/> is raised once for it. The
    /// client keeps a connection to each (see <see cref="YeelightClientOptions.AutoConnect"/>), through which it learns what the lights do,
    /// whoever changes them. A light that is found again, at another address, keeps its object and its connection follows it.
    /// </para>
    /// <para>
    /// <b>Events</b> (<see cref="DeviceDiscovered"/>, <see cref="DeviceStateChanged"/> and the <c>PropertyChanged</c> of the lights) are raised
    /// by a dispatcher of the client: one at a time, in the order in which they happened, on a thread of the thread pool that is not the
    /// thread that reads from a light. A handler can therefore wait for the answer to a command, but the events behind it wait while it runs.
    /// The event of a change of a property says which property changed and not what it changed to, so a change that is still waiting
    /// when the same property changes again is raised once: read the property in the handler. Once the client is disposed nothing new is
    /// started: what was waiting is dropped. A handler that is running is neither interrupted nor waited for.
    /// </para>
    /// <para>
    /// <b>Trust.</b> The Yeelight protocol has no authentication and no encryption: any computer on the network can control a light, and
    /// any computer can announce one. The client takes only announcements that name the address that they were sent from and that is on
    /// the local network (a private, link-local or loopback address), never moves a light that it is connected to to another address,
    /// and keeps at most <see cref="YeelightClientOptions.MaxDevices"/> lights. That is as far as a client can go: use it on a network that you
    /// trust, and give a light that must not be found by anyone else a network of its own.
    /// </para>
    /// <para>
    /// The client writes to the <see cref="ILogger{TCategoryName}"/> that it is given: every message that it sends to a light and every message
    /// that it gets are logged at the debug level, and the connections and the searches at the information level.
    /// </para>
    /// </remarks>
    public sealed class YeelightClient : IDisposable, IAsyncDisposable
    {
        private static readonly TimeSpan DefaultSearchInterval = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

        private readonly object _gate = new();
        private readonly YeelightClientOptions _options;
        private readonly ILogger _logger;
        private readonly EventDispatcher _dispatcher;
        private readonly DeviceContext _context;
        private readonly TimeProvider _time;
        private readonly Func<YeelightDiscoveryOptions, YeelightDiscoveryOptions>? _discoveryCustomizer;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly TaskCompletionSource _disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<YeelightDiscovery> _temporary = [];

        private YeelightDevice[] _devices = [];
        private ReadOnlyCollection<YeelightDevice> _snapshot = Array.AsReadOnly<YeelightDevice>([]);
        private YeelightDiscovery? _listener;
        private YeelightDiscovery? _searcher;
        private bool _disposed;
        private bool _capWarned;

        /// <summary>
        /// Initializes a new instance of the <see cref="YeelightClient"/> class with the default options and no logging.
        /// </summary>
        public YeelightClient()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YeelightClient"/> class.
        /// </summary>
        /// <param name="options">The settings, or <c>null</c> for the defaults. They are read now: changing the object later changes nothing.</param>
        /// <param name="logger">Where the client writes what it does, or <c>null</c> to write nothing.</param>
        /// <exception cref="ArgumentOutOfRangeException">An option has a value that cannot be used.</exception>
        public YeelightClient(YeelightClientOptions? options, ILogger<YeelightClient>? logger = null)
            : this(options, logger, null, null)
        {
        }

        internal YeelightClient(
            YeelightClientOptions? options,
            ILogger? logger,
            Func<YeelightDiscoveryOptions, YeelightDiscoveryOptions>? discoveryCustomizer,
            TimeProvider? time)
        {
            this._options = options ?? YeelightClientOptions.Default;
            this._options.Validate();
            this._logger = SafeLogger.Wrap(logger ?? NullLogger<YeelightClient>.Instance);
            this._time = time ?? TimeProvider.System;
            this._discoveryCustomizer = discoveryCustomizer;
            this._dispatcher = new EventDispatcher(this._logger);
            this._context = new DeviceContext(this._options, this._logger, this._dispatcher, this._time, this.OnStateChanged);
        }

        /// <summary>
        /// Raised when a light is found for the first time. The light is in <see cref="Devices"/> by then, and is connecting.
        /// </summary>
        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

        /// <summary>
        /// Raised when a property of a light changed (its brightness, whether it is connected, its name, ...), for every light of the client. The
        /// event says which property changed: a change that is still waiting when the same property changes again is raised once, so read the property.
        /// </summary>
        public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;

        /// <summary>
        /// Gets the settings of the client.
        /// </summary>
        public YeelightClientOptions Options => this._options;

        /// <summary>
        /// Gets the lights that were found. This is a copy that never changes: loop over it freely, and ask again (or use
        /// <see cref="DeviceDiscovered"/>) to learn about the lights that are found later.
        /// </summary>
        public IReadOnlyList<YeelightDevice> Devices => Volatile.Read(ref this._snapshot);

        /// <summary>
        /// Gets whether the client listens to the announcements of the lights (see <see cref="StartListening"/>).
        /// </summary>
        public bool IsListening
        {
            get
            {
                lock (this._gate)
                {
                    return this._listener is not null;
                }
            }
        }

        /// <summary>
        /// Gets whether the client searches for lights (see <see cref="StartDiscovery"/>).
        /// </summary>
        public bool IsSearching
        {
            get
            {
                lock (this._gate)
                {
                    return this._searcher is not null;
                }
            }
        }

        /// <summary>
        /// Listens to the announcements that the lights send by themselves, which they do when something changed and now and then. This
        /// finds lights without asking, but it can take a while: <see cref="StartDiscovery"/> is quicker. It joins the multicast group of
        /// the lights on every network of this computer, and holds UDP port 1982 (shared with other programs that listen to it).
        /// Calling it again starts the listening anew.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        public void StartListening()
        {
            lock (this._gate)
            {
                this.ThrowIfDisposed();
                this._listener?.Dispose();
                this._listener = this.Begin(new YeelightDiscoveryOptions { Listen = true, Port = this._options.DiscoveryPort });
            }
        }

        /// <summary>
        /// Stops listening to the announcements of the lights.
        /// </summary>
        public void StopListening()
        {
            lock (this._gate)
            {
                this._listener?.Dispose();
                this._listener = null;
            }
        }

        /// <summary>
        /// Searches for lights now and every <paramref name="interval"/>, on every network that this computer is on, and takes over the answers.
        /// A search goes out as a multicast and as a broadcast, and is sent three times (see <see cref="YeelightClientOptions.SearchBurst"/>), because
        /// UDP over WiFi loses packets. Calling it again starts anew (to change the interval, for example).
        /// </summary>
        /// <param name="interval">The time between two searches, from a second to 24 days; <c>null</c> is a minute.</param>
        /// <exception cref="ArgumentOutOfRangeException">The interval cannot be used.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        public void StartDiscovery(TimeSpan? interval = null)
        {
            var every = interval ?? DefaultSearchInterval;
            ArgumentOutOfRangeException.ThrowIfLessThan(every, TimeSpan.FromSeconds(1), nameof(interval));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(every, TimeSpan.FromDays(24), nameof(interval));
            lock (this._gate)
            {
                this.ThrowIfDisposed();
                this._searcher?.Dispose();
                this._searcher = this.Begin(this.SearchOptions(every));
            }
        }

        /// <summary>
        /// Stops searching for lights.
        /// </summary>
        public void StopDiscovery()
        {
            lock (this._gate)
            {
                this._searcher?.Dispose();
                this._searcher = null;
            }
        }

        /// <summary>
        /// Searches for lights now instead of when the interval ends. Does nothing when <see cref="StartDiscovery"/> was not called.
        /// </summary>
        public void SearchNow()
        {
            lock (this._gate)
            {
                this._searcher?.SearchNow();
            }
        }

        /// <summary>
        /// Searches for lights for a while, and returns the lights that the client knows then.
        /// </summary>
        /// <remarks>
        /// When <see cref="StartDiscovery"/> was called, a search is sent now and the client waits. Otherwise the client searches for the
        /// time that it waits, and stops. The lights that are found stay in <see cref="Devices"/> either way. A light answers a search within a
        /// second, but one that does not hear the multicast group (see <see cref="YeelightClientOptions.UseBroadcast"/>) is only found because
        /// of the second and the third search, which are a second apart: wait at least three seconds to find all.
        /// </remarks>
        /// <param name="timeout">How long to search.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The lights that the client knows: the same list as <see cref="Devices"/>, as it is when the time has passed.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is not more than zero, or more than about 49 days.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled, or the client was disposed while it waited.</exception>
        public async Task<IReadOnlyList<YeelightDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, YeelightLimits.LongestTimer, nameof(timeout));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._lifetime.Token);
            YeelightDiscovery? temporary = null;
            lock (this._gate)
            {
                this.ThrowIfDisposed();
                if (this._searcher is { } running)
                {
                    running.SearchNow();
                }
                else
                {
                    temporary = this.Begin(this.SearchOptions(TimeSpan.FromDays(1)));
                    this._temporary.Add(temporary);
                }
            }

            try
            {
                await Task.Delay(timeout, this._time, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                if (temporary is not null)
                {
                    lock (this._gate)
                    {
                        this._temporary.Remove(temporary);
                    }

                    await temporary.DisposeAsync().ConfigureAwait(false);
                }
            }

            return this.Devices;
        }

        /// <summary>
        /// Asks one address whether a Yeelight light is there, with a search that is sent straight to it. For networks that do not pass the
        /// multicast or the broadcast of a search (a VPN, a network with its lights on another subnet): a light answers a search that is
        /// sent to it, wherever it is, as long as the address can be reached.
        /// </summary>
        /// <param name="address">The IP address of the light, on the local network (a private, link-local or loopback address).</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light (it is in <see cref="Devices"/> from now on), or <c>null</c> when nothing answered within three seconds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The address is not an address of the local network.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled, or the client was disposed while it waited.</exception>
        public Task<YeelightDevice?> ProbeAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            return this.ProbeAsync(address, this._options.DiscoveryPort, cancellationToken);
        }

        /// <summary>
        /// Asks one address whether a Yeelight light is there, on a UDP port other than the usual one (<see cref="YeelightClientOptions.DiscoveryPort"/>).
        /// For a simulator, or a light behind a forwarded port. See <see cref="ProbeAsync(IPAddress, CancellationToken)"/>.
        /// </summary>
        /// <param name="address">The IP address of the light, on the local network (a private, link-local or loopback address).</param>
        /// <param name="port">The UDP port that the light answers searches on, from 1 to 65535.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light (it is in <see cref="Devices"/> from now on), or <c>null</c> when nothing answered within three seconds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The address is not an address of the local network.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not a port.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled, or the client was disposed while it waited.</exception>
        public async Task<YeelightDevice?> ProbeAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            this.ThrowIfDisposed();
            if (!DeviceAnnouncement.IsLocalNetworkAddress(address))
            {
                throw new ArgumentException("A Yeelight light has an address on the local network.", nameof(address));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._lifetime.Token);
            using var probe = new YeelightDiscovery(this.CustomizeDiscovery(new YeelightDiscoveryOptions { Port = this._options.DiscoveryPort, ProbePort = port }), this._logger);
            var announcement = await probe.ProbeAsync(address, ProbeTimeout, linked.Token).ConfigureAwait(false);
            return announcement is null ? null : this.Apply(announcement);
        }

        /// <summary>
        /// Asks one address whether a Yeelight light is there. See <see cref="ProbeAsync(IPAddress, CancellationToken)"/>.
        /// </summary>
        /// <param name="address">The IP address of the light, for example <c>192.168.1.50</c>.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light, or <c>null</c> when nothing answered within three seconds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The text is not an IP address, or not one of the local network.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled, or the client was disposed while it waited.</exception>
        public Task<YeelightDevice?> ProbeAsync(string address, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (!IPAddress.TryParse(address.Trim(), out var ip))
            {
                throw new ArgumentException($"'{address}' is not an IP address.", nameof(address));
            }

            return this.ProbeAsync(ip, cancellationToken);
        }

        /// <summary>
        /// Finds a light by its id.
        /// </summary>
        /// <param name="id">The id of the light, for example <c>0x000000000015243f</c> (the case does not matter).</param>
        /// <returns>The light, or <c>null</c> when the client has not found it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
        public YeelightDevice? FindDevice(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            return Array.Find(Volatile.Read(ref this._devices), device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Waits until a light is found, when it is not yet: look for lights first (<see cref="StartDiscovery"/>, <see cref="StartListening"/>
        /// or <see cref="ProbeAsync(IPAddress, CancellationToken)"/>).
        /// </summary>
        /// <param name="id">The id of the light, for example <c>0x000000000015243f</c> (the case does not matter).</param>
        /// <param name="timeout">How long to wait, or <see cref="Timeout.InfiniteTimeSpan"/> to wait until the light is found (or the token is cancelled, or the client disposed).</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative (and not infinite), or more than about 49 days.</exception>
        /// <exception cref="TimeoutException">The light was not found within the time.</exception>
        /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled, or the client was disposed while it waited.</exception>
        public async Task<YeelightDevice> WaitForDeviceAsync(string id, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero, nameof(timeout));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, YeelightLimits.LongestTimer, nameof(timeout));
            }

            this.ThrowIfDisposed();
            var found = new TaskCompletionSource<YeelightDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnAdded(YeelightDevice device)
            {
                if (string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    found.TrySetResult(device);
                }
            }

            // Subscribe first, then look: a light that is found in between cannot be missed.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._lifetime.Token);
            this.DeviceAdded += OnAdded;
            try
            {
                if (this.FindDevice(id) is { } known)
                {
                    return known;
                }

                try
                {
                    return await found.Task.WaitAsync(timeout, this._time, linked.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"The light {id} was not found within {timeout.TotalSeconds:0.#} seconds.");
                }
            }
            finally
            {
                this.DeviceAdded -= OnAdded;
            }
        }

        /// <summary>
        /// Forgets a light and closes its connection. It is found again (as a new object) when it announces itself or answers a search again.
        /// </summary>
        /// <param name="device">The light.</param>
        /// <returns><c>true</c> when the client had the light; <c>false</c> when it did not (or no longer).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
        public async Task<bool> RemoveDeviceAsync(YeelightDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            var removed = false;
            lock (this._gate)
            {
                var index = Array.IndexOf(this._devices, device);
                if (index >= 0)
                {
                    var rest = new List<YeelightDevice>(this._devices);
                    rest.RemoveAt(index);
                    this.SetDevices([.. rest]);
                    removed = true;
                }
            }

            if (removed)
            {
                await device.CloseAsync().ConfigureAwait(false);
            }

            return removed;
        }

        /// <summary>
        /// Stops looking for lights, forgets them, and closes their connections.
        /// </summary>
        public void Dispose()
        {
            // On the thread pool: a caller that has a synchronization context cannot be deadlocked by the work that waits for it.
            Task.Run(() => this.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Stops looking for lights, forgets them, and closes their connections. What the event dispatcher still had to raise is dropped.
        /// </summary>
        /// <returns>A task that ends when the connections are closed.</returns>
        public async ValueTask DisposeAsync()
        {
            YeelightDevice[] devices = [];
            YeelightDiscovery? listener = null;
            YeelightDiscovery? searcher = null;
            YeelightDiscovery[] temporary = [];
            bool first;
            lock (this._gate)
            {
                first = !this._disposed;
                if (first)
                {
                    this._disposed = true;
                    devices = this._devices;
                    this.SetDevices([]);
                    listener = this._listener;
                    searcher = this._searcher;
                    temporary = [.. this._temporary];
                    this._listener = null;
                    this._searcher = null;
                    this._temporary.Clear();
                }
            }

            if (!first)
            {
                // Somebody else is disposing the client, or did: this ends when that does.
                await this._disposeCompleted.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                // What waits for a light, or for a search to end, is ended with the client.
                this._lifetime.Cancel();
                this._dispatcher.Dispose();
                var closing = new List<Task>();
                if (listener is not null)
                {
                    closing.Add(listener.DisposeAsync().AsTask());
                }

                if (searcher is not null)
                {
                    closing.Add(searcher.DisposeAsync().AsTask());
                }

                foreach (var discovery in temporary)
                {
                    closing.Add(discovery.DisposeAsync().AsTask());
                }

                foreach (var device in devices)
                {
                    closing.Add(device.CloseAsync());
                }

                await Task.WhenAll(closing).ConfigureAwait(false);
            }
            finally
            {
                this._disposeCompleted.TrySetResult();
            }
        }

        /// <summary>Raised when a light was added, synchronously: for the code that waits for one.</summary>
        private event Action<YeelightDevice>? DeviceAdded;

        /// <summary>
        /// Takes over an announcement of a light: a new light is added (and connected), a known one gets what the announcement says
        /// (including a new address when the router gave it one).
        /// </summary>
        /// <returns>The light, or <c>null</c> when the client is disposed.</returns>
        internal YeelightDevice? Apply(DeviceAnnouncement announcement)
        {
            ArgumentNullException.ThrowIfNull(announcement);

            YeelightDevice? added = null;
            YeelightDevice device;
            lock (this._gate)
            {
                if (this._disposed)
                {
                    return null;
                }

                var known = Array.Find(this._devices, candidate => string.Equals(candidate.Id, announcement.Id, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    // Anyone on the network can announce a light: what the client keeps is bounded.
                    if (this._devices.Length >= this._options.MaxDevices)
                    {
                        if (!this._capWarned)
                        {
                            this._capWarned = true;
                            this._logger.LogWarning("The client already has {Count} lights: announcements of other lights are ignored.", this._options.MaxDevices);
                        }

                        return null;
                    }

                    device = new YeelightDevice(this._context, announcement);
                    this.SetDevices([.. this._devices, device]);
                    added = device;

                    // Queued under the lock, so that what another thread does to the light as soon as it can find it (an announcement that
                    // changes something) is queued after it: DeviceDiscovered is the first event of every light.
                    var discovered = device;
                    this._dispatcher.Post(() => this._dispatcher.Raise(this.DeviceDiscovered, this, new DeviceDiscoveredEventArgs(discovered)));
                }
                else
                {
                    device = known;
                }
            }

            if (added is not null)
            {
                this._logger.LogInformation("Found {Light} ({Model}) at {Location}.", added.Id, added.Model, added.Location);

                // Whatever the connection does after it starts is news only for a light that was announced: DeviceDiscovered is queued already.
                this.DeviceAdded?.Invoke(added);
                if (this._options.AutoConnect)
                {
                    added.Connect();
                }

                return added;
            }

            device.Apply(announcement);
            return device;
        }

        private void OnStateChanged(YeelightDevice device, YeelightLight light, string propertyName)
        {
            // This runs on the dispatcher, which is where the handlers of the application run.
            this._dispatcher.Raise(this.DeviceStateChanged, this, new DeviceStateChangedEventArgs(device, light, propertyName));
        }

        private YeelightDiscoveryOptions SearchOptions(TimeSpan interval)
        {
            return new YeelightDiscoveryOptions
            {
                Search = true,
                SearchInterval = interval,
                UseBroadcast = this._options.UseBroadcast,
                SearchBurst = this._options.SearchBurst,
                Port = this._options.DiscoveryPort,
            };
        }

        private YeelightDiscoveryOptions CustomizeDiscovery(YeelightDiscoveryOptions options)
        {
            return this._discoveryCustomizer is null ? options : this._discoveryCustomizer(options);
        }

        private YeelightDiscovery Begin(YeelightDiscoveryOptions options)
        {
            var discovery = new YeelightDiscovery(this.CustomizeDiscovery(options), this._logger);
            discovery.Announced += announcement => this.Apply(announcement);
            discovery.Start();
            return discovery;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
        }

        /// <summary>Replaces the list of lights (a copy that never changes) and the read-only view of it. Called under the lock.</summary>
        private void SetDevices(YeelightDevice[] devices)
        {
            this._devices = devices;
            Volatile.Write(ref this._snapshot, Array.AsReadOnly(devices));
        }
    }
}
