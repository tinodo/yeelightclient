namespace Yeelight.TestApp.Session
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.UI.Dispatching;
    using Yeelight.TestApp.Services;

    /// <summary>
    /// What the pages share: the one <see cref="YeelightClient"/> of the app, the lights that it found, the light that is selected, the
    /// music mode sessions, the simulated lights, and the log and the events that the client produced. The client raises its events on a
    /// thread of its own; everything that this class raises (and everything on the lists) is on the thread of the user interface.
    /// </summary>
    /// <remarks>
    /// Nothing is sent to the network when the app starts: a client that is made does nothing. Searching, listening and probing start when
    /// a person asks for them, and the app never scans a network.
    /// </remarks>
    internal sealed class AppSession
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly ConcurrentDictionary<YeelightDevice, LightItem> _byDevice = new();
        private readonly Dictionary<string, YeelightMusicSession> _music = [];
        private readonly DispatcherQueueTimer _timer;
        private YeelightClient _client;
        private LightItem? _selected;
        private bool _selectedByUser;
        private YeelightDevice? _watched;
        private PropertyChangedEventHandler? _watchMain;
        private PropertyChangedEventHandler? _watchBackground;
        private Task? _shutdown;
        private volatile bool _closing;

        /// <summary>Initializes a new instance of the <see cref="AppSession"/> class; call it on the thread of the user interface.</summary>
        /// <param name="dispatcher">The dispatcher of the user interface.</param>
        /// <param name="settings">The settings of the app.</param>
        public AppSession(DispatcherQueue dispatcher, AppSettings settings)
        {
            this._dispatcher = dispatcher;
            this.Settings = settings;
            this.Log = LogEntry.NewStore();
            this.Events = EventEntry.NewStore();
            this.Logger = new AppLogger(this.Log);
            this.Simulated = new SimulatedLights();
            this._client = this.CreateClient(settings);

            // The time since a light was last heard from is not an event of the library: it is read again every second.
            this._timer = dispatcher.CreateTimer();
            this._timer.Interval = TimeSpan.FromSeconds(1);
            this._timer.IsRepeating = true;
            this._timer.Tick += (_, _) =>
            {
                foreach (var item in this.Lights)
                {
                    item.Tick();
                }

                this.Ticked?.Invoke(this, EventArgs.Empty);
            };
            this._timer.Start();
        }

        /// <summary>Occurs every second, on the thread of the user interface: for what has to be read again although no event told so.</summary>
        public event EventHandler? Ticked;

        /// <summary>Occurs when a light was added to <see cref="Lights"/> or removed from it.</summary>
        public event EventHandler? LightsChanged;

        /// <summary>Occurs when another light was selected.</summary>
        public event EventHandler? SelectedChanged;

        /// <summary>Occurs when a music mode session started or ended; the argument is the id of the light.</summary>
        public event EventHandler<string>? MusicChanged;

        /// <summary>Occurs when the client was replaced, by applying the settings: nothing is searching or listening then, and the lights are gone.</summary>
        public event EventHandler? ClientReplaced;

        /// <summary>Occurs when the settings were replaced.</summary>
        public event EventHandler? SettingsChanged;

        /// <summary>Gets the settings of the app.</summary>
        public AppSettings Settings { get; private set; }

        /// <summary>Gets the log: everything that the client logged, and what the app noted. It is bounded.</summary>
        public BoundedBuffer<LogEntry> Log { get; }

        /// <summary>Gets the events that the client raised. It is bounded.</summary>
        public BoundedBuffer<EventEntry> Events { get; }

        /// <summary>Gets the logger that every client of the app logs to.</summary>
        public AppLogger Logger { get; }

        /// <summary>Gets the lights that the app simulates, on the loopback.</summary>
        public SimulatedLights Simulated { get; }

        /// <summary>Gets the lights that the client found. Use it on the thread of the user interface.</summary>
        public ObservableCollection<LightItem> Lights { get; } = [];

        /// <summary>Gets the client of the app.</summary>
        public YeelightClient Client => this._client;

        /// <summary>Gets the light that is selected, or <c>null</c> when none is.</summary>
        public LightItem? Selected => this._selected;

        /// <summary>Gets the dispatcher of the user interface.</summary>
        public DispatcherQueue Dispatcher => this._dispatcher;

        /// <summary>Gets a value indicating whether the app is closing: nothing is raised to the screen any more.</summary>
        public bool IsClosing => this._closing;

        /// <summary>Gets a value indicating whether the client searches by itself (<see cref="YeelightClient.StartDiscovery"/>).</summary>
        public bool IsSearching => this._client.IsSearching;

        /// <summary>Gets a value indicating whether the client listens to the announcements of the lights.</summary>
        public bool IsListening => this._client.IsListening;

        /// <summary>Finds the item of a device.</summary>
        /// <param name="device">The device.</param>
        /// <returns>The item, or <c>null</c> when the device is not in the list.</returns>
        public LightItem? FindItem(YeelightDevice device) => this._byDevice.TryGetValue(device, out var item) ? item : null;

        /// <summary>Finds the item of a light by its id.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The item, or <c>null</c>.</returns>
        public LightItem? FindItem(string id) => this.Lights.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Selects a light, which the Control and the Raw page then act on.</summary>
        /// <param name="item">The light, or <c>null</c> for none.</param>
        /// <param name="byUser">Whether a person chose it (and the app remembers it), or the app did because it was the first light found.</param>
        public void Select(LightItem? item, bool byUser = false)
        {
            if (ReferenceEquals(this._selected, item))
            {
                this._selectedByUser |= byUser && item is not null;
                return;
            }

            this._selected = item;
            this._selectedByUser = byUser && item is not null;
            this.Watch(item?.Device);
            if (byUser && item is not null)
            {
                this.Settings.LastLightId = item.Id;
            }

            this.SelectedChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Searches for lights for a while and waits (<see cref="YeelightClient.DiscoverAsync"/>). The lights that are found are added to the list.</summary>
        /// <param name="timeout">How long to search.</param>
        /// <param name="cancellationToken">To stop.</param>
        /// <returns>The number of lights that the client knows when the time has passed.</returns>
        public async Task<int> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var client = this._client;
            var devices = await client.DiscoverAsync(timeout, cancellationToken);
            return this.Adopt(client, devices);
        }

        /// <summary>Asks one address whether a light is there (<see cref="YeelightClient.ProbeAsync(IPAddress, int, CancellationToken)"/>).</summary>
        /// <param name="address">The address as a person typed it: <c>192.168.1.50</c>, or with the port that it answers searches on (<c>127.0.0.1:55000</c>).</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light, or <c>null</c> when nothing answered.</returns>
        /// <exception cref="ArgumentException">The text is not an address, or the address is not one of the local network.</exception>
        public async Task<LightItem?> ProbeAsync(string address, CancellationToken cancellationToken)
        {
            var client = this._client;
            if (!LocalNetwork.TryParseEndpoint(address, this.Settings.DiscoveryPort, out var ip, out var port, out var problem))
            {
                throw new ArgumentException(problem);
            }

            var device = await client.ProbeAsync(ip!, port, cancellationToken);
            return device is null ? null : this.Adopt(client, device);
        }

        /// <summary>Asks the loopback, on a port, whether a (simulated) light is there.</summary>
        /// <param name="port">The UDP port that the light answers a search on.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The light, or <c>null</c> when nothing answered.</returns>
        public async Task<LightItem?> ProbeLoopbackAsync(int port, CancellationToken cancellationToken)
        {
            var client = this._client;
            var device = await client.ProbeAsync(IPAddress.Loopback, port, cancellationToken);
            return device is null ? null : this.Adopt(client, device);
        }

        /// <summary>Starts the periodic search of the client, which also sends one now.</summary>
        /// <param name="interval">The time between two searches.</param>
        public void StartSearching(TimeSpan interval)
        {
            this._client.StartDiscovery(interval);
            this.Logger.Note(LogLevel.Information, $"Searching every {interval.TotalSeconds:0.#} seconds.");
        }

        /// <summary>Stops the periodic search.</summary>
        public void StopSearching()
        {
            this._client.StopDiscovery();
            this.Logger.Note(LogLevel.Information, "Stopped searching.");
        }

        /// <summary>Starts listening to the announcements of the lights.</summary>
        public void StartListening()
        {
            this._client.StartListening();
            this.Logger.Note(LogLevel.Information, "Listening to the announcements of the lights.");
        }

        /// <summary>Stops listening.</summary>
        public void StopListening()
        {
            this._client.StopListening();
            this.Logger.Note(LogLevel.Information, "Stopped listening.");
        }

        /// <summary>Forgets a light: the client closes its connection and drops it, and so does the list. It comes back when it answers a search again.</summary>
        /// <param name="item">The light.</param>
        /// <returns><c>true</c> when the client had the light.</returns>
        public async Task<bool> ForgetAsync(LightItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            var client = this._client;
            await this.StopMusicAsync(item.Id);
            var removed = await client.RemoveDeviceAsync(item.Device);
            if (ReferenceEquals(client, this._client))
            {
                this.Remove(item);
            }

            this.Logger.Note(LogLevel.Information, $"Forgot {item.Title}.", item.Id);
            return removed;
        }

        /// <summary>Starts simulated lights (one of every kind that is asked for), and finds them with a probe on the loopback.</summary>
        /// <param name="kinds">The kinds of lights.</param>
        /// <param name="cancellationToken">To stop waiting for the probes.</param>
        /// <returns>The lights that were found, in the order of <paramref name="kinds"/>.</returns>
        public async Task<IReadOnlyList<LightItem>> StartSimulatedAsync(IEnumerable<SimulatedKind> kinds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(kinds);
            var found = new List<LightItem>();
            foreach (var kind in kinds)
            {
                var light = this.Simulated.Start(kind);
                this.Logger.Note(LogLevel.Information, $"Simulated light: {light.Description}.", light.Id);
                if (await this.ProbeLoopbackAsync(light.DiscoveryPort, cancellationToken) is { } item)
                {
                    found.Add(item);
                }
            }

            return found;
        }

        /// <summary>Forgets the simulated lights and stops them.</summary>
        /// <returns>The number of simulated lights that were stopped.</returns>
        public async Task<int> StopSimulatedAsync()
        {
            foreach (var light in this.Simulated.Running)
            {
                if (this.FindItem(light.Id) is { } item)
                {
                    await this.ForgetAsync(item);
                }
            }

            var stopped = await this.Simulated.StopAllAsync();
            if (stopped.Count > 0)
            {
                this.Logger.Note(LogLevel.Information, $"Stopped {stopped.Count} simulated lights.");
            }

            return stopped.Count;
        }

        /// <summary>Replaces the client by a new one that is made of new settings: the old one is disposed, and the list of lights is cleared.</summary>
        /// <param name="settings">The settings. They are checked first: a client is only replaced by one that can be made.</param>
        /// <returns>A task that ends when the old client is disposed.</returns>
        public async Task ApplySettingsAsync(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var problems = settings.Validate();
            if (problems.Count > 0)
            {
                throw new ArgumentException(string.Join(' ', problems), nameof(settings));
            }

            var old = this._client;
            var next = this.CreateClient(settings);
            await this.StopAllMusicAsync();
            this.Select(null);
            this.ClearLights();
            this.Settings = settings;
            this._client = next;
            this.Logger.Note(LogLevel.Information, "New client. " + settings.Describe());
            this.ClientReplaced?.Invoke(this, EventArgs.Empty);
            this.SettingsChanged?.Invoke(this, EventArgs.Empty);
            await CloseClientAsync(old);
        }

        /// <summary>Tells the app that its settings (the theme, for one) changed, and writes them.</summary>
        public void NotifySettingsChanged()
        {
            this.Settings.Save();
            this.SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts a light in music mode and keeps the session, so that every page can send over it.</summary>
        /// <param name="item">The light.</param>
        /// <param name="localAddress">The address of this computer that the light connects to, or <c>null</c> for the address that the operating system uses to reach the light.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The session.</returns>
        public async Task<YeelightMusicSession> StartMusicAsync(LightItem item, IPAddress? localAddress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            await this.StopMusicAsync(item.Id);

            // The client picks the address of its control connection when it is given none, which is an IPv4-mapped IPv6 address that a
            // light cannot connect to (and Windows cannot listen on): the address that the system routes to the light is given instead.
            var address = localAddress ?? (item.Device.Address is { } light ? LocalNetwork.LocalAddressFor(light) : null);
            var session = await item.Device.StartMusicModeAsync(address, 0, cancellationToken);
            this._music[item.Id] = session;
            this.Logger.Note(LogLevel.Information, $"Music mode started for {item.Title}.", item.Id);
            this.MusicChanged?.Invoke(this, item.Id);
            _ = this.WatchMusicAsync(item.Id, session);
            return session;
        }

        /// <summary>Gets the music mode session of a light.</summary>
        /// <param name="id">The id of the light.</param>
        /// <returns>The session, or <c>null</c> when the light is not in music mode through this app.</returns>
        public YeelightMusicSession? GetMusic(string id) => this._music.TryGetValue(id, out var session) && session.IsConnected ? session : null;

        /// <summary>Ends the music mode session of a light, if it has one.</summary>
        /// <param name="id">The id of the light.</param>
        /// <returns>A task that ends when the session is closed.</returns>
        public async Task StopMusicAsync(string id)
        {
            if (!this._music.Remove(id, out var session))
            {
                return;
            }

            try
            {
                await session.DisposeAsync();
            }
            finally
            {
                this.Logger.Note(LogLevel.Information, "Music mode stopped.", id);
                this.MusicChanged?.Invoke(this, id);
            }
        }

        /// <summary>Ends the whole session as the window closes: nothing is raised to the screen after this. It can be called again.</summary>
        /// <returns>A task that ends when the music sessions are closed, the client is disposed and the simulated lights are stopped.</returns>
        public Task ShutdownAsync()
        {
            return this._shutdown ??= this.ShutdownCoreAsync();
        }

        /// <summary>Posts an action to the thread of the user interface.</summary>
        /// <param name="action">The action.</param>
        /// <returns><c>false</c> when the thread is no longer there.</returns>
        public bool Post(Action action) => this._dispatcher.TryEnqueue(() => action());

        private static async Task CloseClientAsync(YeelightClient client)
        {
            // A client waits for its connections to close: a light that is gone must not keep the app from going on.
            try
            {
                await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6));
            }
            catch (TimeoutException)
            {
                // The rest of the closing goes on in the background.
            }
        }

        private YeelightClient CreateClient(AppSettings settings)
        {
            var client = new YeelightClient(settings.ToClientOptions(), this.Logger);
            client.DeviceDiscovered += (_, e) => this.OnDiscovered(client, e.Device);
            client.DeviceStateChanged += (_, e) => this.OnStateChanged(client, e);
            return client;
        }

        // On the dispatcher of the client: nothing here may wait.
        private void OnDiscovered(YeelightClient client, YeelightDevice device)
        {
            if (this._closing || !ReferenceEquals(client, Volatile.Read(ref this._client)))
            {
                return;
            }

            this.Events.Add(EventEntry.Discovered(device, DateTimeOffset.Now));
            this.Post(() => this.Adopt(client, device));
        }

        private void OnStateChanged(YeelightClient client, DeviceStateChangedEventArgs e)
        {
            if (this._closing || !ReferenceEquals(client, Volatile.Read(ref this._client)))
            {
                return;
            }

            this.Events.Add(EventEntry.StateChanged(e.Device, e.Light, e.PropertyName, DateTimeOffset.Now));
            if (this._byDevice.TryGetValue(e.Device, out var item))
            {
                item.RequestRefresh();
            }
        }

        private void RecordPropertyChanged(YeelightDevice device, YeelightLight light, string? property)
        {
            if (!this._closing && property is not null)
            {
                this.Events.Add(EventEntry.PropertyChanged(device, light, property, DateTimeOffset.Now));
            }
        }

        // The Events page also shows the PropertyChanged of the light that is selected: the event of the light itself, next to that of the client.
        private void Watch(YeelightDevice? device)
        {
            if (this._watched is { } previous)
            {
                previous.PropertyChanged -= this._watchMain;
                previous.Background.PropertyChanged -= this._watchBackground;
            }

            this._watched = device;
            if (device is null)
            {
                return;
            }

            this._watchMain = (_, e) => this.RecordPropertyChanged(device, device, e.PropertyName);
            this._watchBackground = (_, e) => this.RecordPropertyChanged(device, device.Background, e.PropertyName);
            device.PropertyChanged += this._watchMain;
            device.Background.PropertyChanged += this._watchBackground;
        }

        private int Adopt(YeelightClient client, IReadOnlyList<YeelightDevice> devices)
        {
            foreach (var device in devices)
            {
                this.Adopt(client, device);
            }

            return devices.Count;
        }

        // On the thread of the user interface: a light that the client found is added to the list, once.
        private LightItem? Adopt(YeelightClient client, YeelightDevice device)
        {
            if (this._closing || !ReferenceEquals(client, this._client))
            {
                return null;
            }

            if (this._byDevice.TryGetValue(device, out var known))
            {
                return known;
            }

            var item = new LightItem(device, this.Simulated.Find(device.Id) is not null, this._dispatcher);
            this._byDevice[device] = item;
            this.Lights.Add(item);
            this.LightsChanged?.Invoke(this, EventArgs.Empty);

            // The first light that is found is selected, unless a person chose one; the light that was used last comes before the others.
            if (this._selected is null || (!this._selectedByUser && string.Equals(this.Settings.LastLightId, device.Id, StringComparison.OrdinalIgnoreCase)))
            {
                this.Select(item);
            }

            return item;
        }

        private void Remove(LightItem item)
        {
            this._byDevice.TryRemove(item.Device, out _);
            this.Lights.Remove(item);
            if (ReferenceEquals(this._selected, item))
            {
                this.Select(null);
            }

            this.LightsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearLights()
        {
            this._byDevice.Clear();
            this.Lights.Clear();
            this.LightsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task WatchMusicAsync(string id, YeelightMusicSession session)
        {
            await session.Completion.ConfigureAwait(false);
            this.Post(() =>
            {
                // The session ended by itself (the light closed it): the light is no longer in music mode through this app.
                if (this._music.TryGetValue(id, out var current) && ReferenceEquals(current, session))
                {
                    this._music.Remove(id);
                    this.Logger.Note(LogLevel.Information, "The music mode session ended.", id);
                    this.MusicChanged?.Invoke(this, id);
                }
            });
        }

        private async Task StopAllMusicAsync()
        {
            foreach (var id in this._music.Keys.ToArray())
            {
                await this.StopMusicAsync(id);
            }
        }

        private async Task ShutdownCoreAsync()
        {
            this._closing = true;
            this._timer.Stop();
            this.Watch(null);
            this.Settings.Save();
            try
            {
                await this.StopAllMusicAsync();
            }
            catch (Exception exception) when (exception is YeelightException or ObjectDisposedException)
            {
                // The sessions are closed with the client anyway.
            }

            await CloseClientAsync(this._client);
            await this.Simulated.DisposeAsync();
        }
    }
}
