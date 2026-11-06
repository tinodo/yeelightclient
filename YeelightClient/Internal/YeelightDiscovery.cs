namespace Yeelight
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Numerics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// What a <see cref="YeelightDiscovery"/> does and where.
    /// </summary>
    internal sealed record YeelightDiscoveryOptions
    {
        /// <summary>Hear the announcements that devices send by themselves (this joins the multicast group on every network).</summary>
        public bool Listen { get; init; }

        /// <summary>Ask the devices to say who they are, now and every <see cref="SearchInterval"/>, and hear their answers.</summary>
        public bool Search { get; init; }

        public TimeSpan SearchInterval { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Also send every search as a broadcast on every network. A device that does not hear the multicast search (an access
        /// point or a router that does not pass the group on to every WiFi client) does hear the broadcast: on the network where
        /// this was measured, one bulb of three never answered the multicast search and answered every broadcast.
        /// </summary>
        public bool UseBroadcast { get; init; } = true;

        /// <summary>Where the broadcast search is sent instead of the broadcast addresses of the networks (for tests: the loopback has none).</summary>
        public IPEndPoint? BroadcastEndpoint { get; init; }

        /// <summary>How many times a search is sent when it is due: UDP over WiFi loses packets, and a device that saves power can miss one.</summary>
        public int SearchBurst { get; init; } = 3;

        /// <summary>The wait between the searches of a burst: a lost packet is usually lost with the ones around it, so they are spread out.</summary>
        public TimeSpan SearchBurstSpacing { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>How often the network addresses of this computer are looked at. When they changed the sockets are made again.</summary>
        public TimeSpan AddressRefreshInterval { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>The UDP port that devices are searched on and announce themselves to.</summary>
        public int Port { get; init; } = YeelightDiscovery.DefaultPort;

        /// <summary>Where a search is sent, when it is not the multicast group of the devices on <see cref="Port"/>.</summary>
        public IPEndPoint? SearchEndpoint { get; init; }

        /// <summary>The address that announcements are listened on.</summary>
        public IPAddress ListenAddress { get; init; } = IPAddress.Any;

        /// <summary>The port that announcements are listened on, when it is not <see cref="Port"/> (port 0 takes a free one).</summary>
        public int? ListenPort { get; init; }

        /// <summary>The port that a device is asked on, when it is asked directly and it is not <see cref="Port"/>.</summary>
        public int? ProbePort { get; init; }

        /// <summary>Join the multicast group and send the search out of the right network. Off, only unicast is used.</summary>
        public bool UseMulticast { get; init; } = true;

        /// <summary>The addresses of this computer to search from, when they are not those of the networks it is connected to.</summary>
        public Func<IReadOnlyList<IPAddress>>? GetLocalAddresses { get; init; }
    }

    /// <summary>
    /// Finds Yeelight devices on the local network: it sends the search (to the multicast group and as a broadcast, from every
    /// network the computer is on), and listens for the answers and for the announcements that the devices send by themselves.
    /// </summary>
    /// <remarks>
    /// Both are needed. A device that is on the same network can fail to hear the multicast group (an access point or a router
    /// that does not pass it on to every WiFi client) and still answers the broadcast; and the devices announce themselves too
    /// rarely (none was heard in 100 seconds) to be found by listening alone.
    /// <para>
    /// Nothing that arrives, and nothing that the network does, ends this: a message that is not from a device is dropped, a
    /// failing receive is tried again, and when the network addresses of the computer change (Wi-Fi comes and goes, a VPN
    /// connects, the computer wakes up) the sockets are made again.
    /// </para>
    /// </remarks>
    internal sealed class YeelightDiscovery : IDisposable, IAsyncDisposable
    {
        public const int DefaultPort = 1982;

        public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.255.250");

        private const int MaxDatagramLength = 4096;
        private const int MaxReceiveFailures = 5;
        private static readonly TimeSpan MinimumTick = TimeSpan.FromMilliseconds(10);
        private static readonly TimeSpan MaximumTick = TimeSpan.FromSeconds(1);

        private static readonly byte[] SearchMessage = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n");

        private readonly YeelightDiscoveryOptions _options;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SemaphoreSlim _wake = new(0);
        private readonly object _gate = new();

        private Task? _supervisor;
        private bool _started;
        private bool _disposed;
        private int _searchRequested;
        private int _generations;
        private volatile IPEndPoint? _listenEndPoint;
        private volatile IReadOnlyList<IPEndPoint> _searchEndPoints = [];

        public YeelightDiscovery(YeelightDiscoveryOptions? options = null, ILogger? logger = null)
        {
            this._options = options ?? new YeelightDiscoveryOptions();
            this._logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Raised for every announcement of a device, on the thread that received it. The address in it was checked.</summary>
        public event Action<DeviceAnnouncement>? Announced;

        /// <summary>Where announcements are listened for, once the sockets are made.</summary>
        public IPEndPoint? ListenEndPoint => this._listenEndPoint;

        /// <summary>The local end points that searches are sent from, once the sockets are made.</summary>
        public IReadOnlyList<IPEndPoint> SearchEndPoints => this._searchEndPoints;

        /// <summary>How many times the sockets were made: once, and again after every change of the network addresses.</summary>
        public int Generations => Volatile.Read(ref this._generations);

        /// <summary>
        /// Starts looking, and goes on until this is disposed.
        /// </summary>
        public void Start()
        {
            lock (this._gate)
            {
                if (this._started || this._disposed)
                {
                    return;
                }

                this._started = true;
                this._supervisor = Task.Run(() => this.RunAsync(this._lifetime.Token));
            }
        }

        /// <summary>
        /// Sends a search now instead of when the interval ends.
        /// </summary>
        public void SearchNow()
        {
            if (this._options.Search && Interlocked.Exchange(ref this._searchRequested, 1) == 0)
            {
                this._wake.Release();
            }
        }

        /// <summary>
        /// Asks one address whether a Yeelight device is there, for networks that do not pass multicast (a VPN, some access points).
        /// </summary>
        /// <returns>What the device answered, or null when nothing (that came from that address) arrived in time.</returns>
        public async Task<DeviceAnnouncement?> ProbeAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (!DeviceAnnouncement.IsLocalNetworkAddress(address))
            {
                throw new ArgumentException("A Yeelight device has an address on the local network.", nameof(address));
            }

            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            // UDP loses packets (WiFi most of all): the search is sent again every second until an answer comes or the time is up.
            var target = new IPEndPoint(address, this._options.ProbePort ?? this._options.Port);
            var sender = Task.Run(async () =>
            {
                try
                {
                    while (!timeoutSource.IsCancellationRequested)
                    {
                        await client.SendAsync(SearchMessage, SearchMessage.Length, target).ConfigureAwait(false);
                        await Task.Delay(this._options.SearchBurstSpacing, timeoutSource.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // The wait is over, or the search cannot be sent from here: the receive says what there is to say.
                }
            });
            try
            {
                while (true)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await client.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // Nothing listens on that port: nobody will answer.
                        return null;
                    }

                    if (TryRead(result.RemoteEndPoint, result.Buffer, out var announcement) && announcement!.IsFrom(address))
                    {
                        return announcement;
                    }
                }
            }
            finally
            {
                timeoutSource.Cancel();
                await sender.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            lock (this._gate)
            {
                this._disposed = true;
            }

            this._lifetime.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Task? supervisor;
            lock (this._gate)
            {
                this._disposed = true;
                supervisor = this._supervisor;
            }

            this._lifetime.Cancel();
            if (supervisor != null)
            {
                try
                {
                    await supervisor.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    this._logger.LogWarning("The discovery took too long to stop.");
                }
            }
        }

        /// <summary>The addresses of this computer on the networks that it is connected to, for searching from.</summary>
        internal static IReadOnlyList<IPAddress> GetNetworkAddresses()
        {
            var addresses = new List<IPAddress>();
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    !network.SupportsMulticast ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address) &&
                        !address.Equals(IPAddress.Any) &&
                        !IsLinkLocal(address))
                    {
                        addresses.Add(address);
                    }
                }
            }

            return addresses;
        }

        /// <summary>
        /// The broadcast address of the network that an address of this computer is on (192.168.1.157 with the mask 255.255.255.0
        /// gives 192.168.1.255), or null when the network has none: a network of one or two addresses has no broadcast address.
        /// </summary>
        internal static IPAddress? BroadcastAddress(IPAddress address, IPAddress mask)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentNullException.ThrowIfNull(mask);
            if (address.AddressFamily != AddressFamily.InterNetwork || mask.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            var hostBits = ~BinaryPrimitives.ReadUInt32BigEndian(mask.GetAddressBytes());
            if (BitOperations.PopCount(hostBits) < 2)
            {
                return null;
            }

            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes()) | hostBits);
            return new IPAddress(bytes);
        }

        private static IPAddress? FindBroadcastAddress(IPAddress address)
        {
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.Equals(address))
                        {
                            return BroadcastAddress(address, unicast.IPv4Mask);
                        }
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // The networks cannot be read right now: the broadcast to everyone on the network is still sent.
            }

            return null;
        }

        private static bool IsLinkLocal(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }

        private static bool TryRead(IPEndPoint source, byte[] data, out DeviceAnnouncement? announcement)
        {
            announcement = null;
            if (data.Length is 0 or > MaxDatagramLength)
            {
                return false;
            }

            return DeviceAnnouncement.TryParse(Encoding.UTF8.GetString(data), out announcement) &&
                announcement!.IsFrom(source.Address);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var failureDelay = TimeSpan.FromSeconds(1);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var addresses = this.GetAddresses();
                    using var generation = this.OpenSockets(addresses, cancellationToken);
                    failureDelay = TimeSpan.FromSeconds(1);
                    await this.WatchAsync(generation, addresses, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning("The discovery failed ({Reason}); trying again in {Seconds:0} seconds.", ex.Message, failureDelay.TotalSeconds);
                    try
                    {
                        await Task.Delay(failureDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    failureDelay = TimeSpan.FromSeconds(Math.Min(failureDelay.TotalSeconds * 2, 60));
                }
            }
        }

        /// <summary>
        /// Sends the searches, and looks at the network addresses; ends when they changed (the caller makes the sockets again), when
        /// a socket failed for good (this throws) or when this is stopped (this throws OperationCanceledException).
        /// </summary>
        private async Task WatchAsync(Generation generation, IReadOnlyList<IPAddress> addresses, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            TimeSpan? lastSearch = null;
            var lastRefresh = TimeSpan.Zero;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                generation.ThrowIfFaulted();

                var now = clock.Elapsed;
                var requested = Interlocked.Exchange(ref this._searchRequested, 0) == 1;
                if (this._options.Search && (requested || lastSearch is null || now - lastSearch.Value >= this._options.SearchInterval))
                {
                    lastSearch = now;
                    await this.SendSearchAsync(generation, cancellationToken).ConfigureAwait(false);
                }

                if (now - lastRefresh >= this._options.AddressRefreshInterval)
                {
                    lastRefresh = now;
                    if (!SameAddresses(addresses, this.GetAddresses()))
                    {
                        this._logger.LogInformation("The network addresses changed; the sockets of the discovery are made again.");
                        return;
                    }
                }

                var elapsed = clock.Elapsed;
                var wait = this._options.AddressRefreshInterval - (elapsed - lastRefresh);
                if (this._options.Search && lastSearch is { } searched)
                {
                    var untilSearch = this._options.SearchInterval - (elapsed - searched);
                    if (untilSearch < wait)
                    {
                        wait = untilSearch;
                    }
                }

                wait = wait < MinimumTick ? MinimumTick : (wait > MaximumTick ? MaximumTick : wait);
                await this._wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SendSearchAsync(Generation generation, CancellationToken cancellationToken)
        {
            var count = Math.Max(1, this._options.SearchBurst);
            var searchEndpoint = this._options.SearchEndpoint ?? new IPEndPoint(MulticastGroup, this._options.Port);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(this._options.SearchBurstSpacing, cancellationToken).ConfigureAwait(false);
                }

                foreach (var searcher in generation.Searchers)
                {
                    await SendAsync(searcher, searchEndpoint).ConfigureAwait(false);
                    foreach (var target in searcher.BroadcastTargets)
                    {
                        await SendAsync(searcher, target).ConfigureAwait(false);
                    }
                }
            }

            async Task SendAsync(Searcher searcher, IPEndPoint target)
            {
                try
                {
                    await searcher.Client.SendAsync(SearchMessage, SearchMessage.Length, target).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    this._logger.LogDebug("The search from {From} to {To} failed: {Reason}", searcher.Address, target.Address, ex.Message);
                }
            }
        }

        private Generation OpenSockets(IReadOnlyList<IPAddress> addresses, CancellationToken cancellationToken)
        {
            var generation = new Generation(cancellationToken);
            try
            {
                if (this._options.Search)
                {
                    foreach (var address in addresses)
                    {
                        var client = this.TryCreateSearcher(address);
                        if (client != null)
                        {
                            generation.Searchers.Add(new Searcher(client, address, this.GetBroadcastTargets(address)));
                            generation.Loops.Add(Task.Run(() => this.ReceiveLoopAsync(client, generation)));
                        }
                    }
                }

                if (this._options.Listen)
                {
                    var listener = this.CreateListener(addresses);
                    generation.Listener = listener;
                    generation.Loops.Add(Task.Run(() => this.ReceiveLoopAsync(listener, generation)));
                }
            }
            catch
            {
                generation.Dispose();
                throw;
            }

            this._searchEndPoints = [.. generation.Searchers.Select(s => (IPEndPoint)s.Client.Client.LocalEndPoint!)];
            this._listenEndPoint = generation.Listener?.Client.LocalEndPoint as IPEndPoint;
            Interlocked.Increment(ref this._generations);
            return generation;
        }

        private UdpClient? TryCreateSearcher(IPAddress address)
        {
            UdpClient? client = null;
            try
            {
                client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.Bind(new IPEndPoint(address, 0));
                if (this._options.UseBroadcast)
                {
                    client.EnableBroadcast = true;
                }

                if (this._options.UseMulticast)
                {
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                }

                return client;
            }
            catch (Exception ex)
            {
                this._logger.LogDebug("No search from {Address}: {Reason}", address, ex.Message);
                client?.Dispose();
                return null;
            }
        }

        /// <summary>Where the search of one network is sent as a broadcast: to its own broadcast address, and to everyone.</summary>
        private IReadOnlyList<IPEndPoint> GetBroadcastTargets(IPAddress address)
        {
            if (!this._options.UseBroadcast)
            {
                return [];
            }

            if (this._options.BroadcastEndpoint is { } target)
            {
                return [target];
            }

            var everyone = new IPEndPoint(IPAddress.Broadcast, this._options.Port);
            var network = FindBroadcastAddress(address);
            return network is null || network.Equals(IPAddress.Broadcast) ? [everyone] : [new IPEndPoint(network, this._options.Port), everyone];
        }

        private UdpClient CreateListener(IReadOnlyList<IPAddress> addresses)
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(this._options.ListenAddress, this._options.ListenPort ?? this._options.Port));
                if (this._options.UseMulticast)
                {
                    foreach (var address in addresses)
                    {
                        try
                        {
                            client.JoinMulticastGroup(MulticastGroup, address);
                        }
                        catch (Exception ex) when (ex is SocketException or ArgumentException)
                        {
                            this._logger.LogDebug("No announcements from the network of {Address}: {Reason}", address, ex.Message);
                        }
                    }
                }

                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private async Task ReceiveLoopAsync(UdpClient client, Generation generation)
        {
            var failures = 0;
            var token = generation.Token;
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(token).ConfigureAwait(false);
                    failures = 0;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows reports an earlier send to a host that does not listen (ICMP port unreachable) on the next receive.
                    continue;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failures++;
                    this._logger.LogDebug("Receiving failed ({Reason}).", ex.Message);
                    if (failures >= MaxReceiveFailures)
                    {
                        generation.Fault(ex);
                        return;
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * failures), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    continue;
                }

                this.Handle(result);
            }
        }

        private void Handle(UdpReceiveResult result)
        {
            try
            {
                if (!TryRead(result.RemoteEndPoint, result.Buffer, out var announcement))
                {
                    return;
                }

                this.Announced?.Invoke(announcement!);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning("A message from {From} could not be handled: {Reason}", result.RemoteEndPoint, ex.Message);
            }
        }

        private IReadOnlyList<IPAddress> GetAddresses()
        {
            try
            {
                var addresses = (this._options.GetLocalAddresses ?? GetNetworkAddresses)();
                return [.. addresses.Distinct().OrderBy(a => a.ToString(), StringComparer.Ordinal)];
            }
            catch (Exception ex)
            {
                this._logger.LogWarning("The network addresses are not known: {Reason}", ex.Message);
                return [];
            }
        }

        private static bool SameAddresses(IReadOnlyList<IPAddress> first, IReadOnlyList<IPAddress> second)
        {
            return first.Count == second.Count && first.Zip(second).All(pair => pair.First.Equals(pair.Second));
        }

        /// <summary>A socket that searches from one address of this computer, and where it sends the broadcast search.</summary>
        private sealed record Searcher(UdpClient Client, IPAddress Address, IReadOnlyList<IPEndPoint> BroadcastTargets);

        /// <summary>The sockets for one set of network addresses, and the loops that read them.</summary>
        private sealed class Generation : IDisposable
        {
            private readonly CancellationTokenSource _source;
            private Exception? _fault;

            public Generation(CancellationToken parent)
            {
                this._source = CancellationTokenSource.CreateLinkedTokenSource(parent);
            }

            public CancellationToken Token => this._source.Token;

            public List<Searcher> Searchers { get; } = [];

            public UdpClient? Listener { get; set; }

            public List<Task> Loops { get; } = [];

            public void Fault(Exception exception)
            {
                Interlocked.CompareExchange(ref this._fault, exception, null);
            }

            public void ThrowIfFaulted()
            {
                if (Volatile.Read(ref this._fault) is { } fault)
                {
                    throw new IOException($"A socket of the discovery failed: {fault.Message}", fault);
                }
            }

            public void Dispose()
            {
                this._source.Cancel();
                foreach (var searcher in this.Searchers)
                {
                    searcher.Client.Dispose();
                }

                this.Listener?.Dispose();
                this._source.Dispose();
            }
        }
    }
}
