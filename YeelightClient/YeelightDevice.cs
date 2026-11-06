namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A Yeelight device on the network: who it is, what its main light is doing, and the commands that control it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A device is the object of its main light (see <see cref="YeelightLight"/>: the properties, the commands, how failures are reported),
    /// and has what concerns the device as a whole: its identity (<see cref="Id"/>, <see cref="Model"/>, <see cref="FirmwareVersion"/>,
    /// <see cref="Location"/>, <see cref="Name"/>), the methods that it supports, its connection, its timer and its music mode. Its
    /// <see cref="Background"/> is the second light of a device that has one.
    /// </para>
    /// <para>
    /// You do not make a device: the <see cref="YeelightClient"/> finds it, and keeps it (and its connection) until it is told to forget it.
    /// A device is known by its <see cref="Id"/>, never by its name or its address, which can change: when the router gives a light a new
    /// address, the connection follows it.
    /// </para>
    /// </remarks>
    public sealed class YeelightDevice : YeelightLight
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly YeelightBackgroundLight _background;
        private readonly List<YeelightMusicSession> _musicSessions = [];
        private YeelightConnection? _connection;
        private bool _closed;

        private string _location;
        private string _host;
        private int _port;
        private IPAddress? _address;
        private string _model;
        private string _firmwareVersion;
        private string[] _support;
        private ReadOnlyCollection<string> _supportedMethods;
        private string _name = string.Empty;
        private int _delayOff;
        private bool _musicOn;
        private int _nightLightBrightness;
        private ActiveMode? _activeMode;
        private long _lastSeen;
        private string? _ignoredMove;
        internal YeelightDevice(DeviceContext context, DeviceAnnouncement announcement)
            : base(context, string.Empty)
        {
            this._background = new YeelightBackgroundLight(this);
            this.Id = announcement.Id;
            this._location = announcement.Location;
            this._host = announcement.Host;
            this._port = announcement.Port;
            this._address = IPAddress.TryParse(announcement.Host, out var address) ? address : null;
            this._model = announcement.Model;
            this._firmwareVersion = announcement.FirmwareVersion;
            this._support = [.. announcement.Support];
            this._supportedMethods = Array.AsReadOnly(this._support);
            this._lastSeen = context.Time.GetUtcNow().UtcTicks;

            // What the announcement says is the starting point, and not a change: nobody has seen this device yet.
            this.Muted = true;
            this._background.Muted = true;
            this.ApplyProperties(announcement.Properties);
            this.Muted = false;
            this._background.Muted = false;
        }

        /// <summary>
        /// Gets the id of the device, for example <c>0x000000000015243f</c>. This is what tells devices apart: it does not change when the
        /// name or the address of the device does.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets where the control service of the device is: <c>yeelight://192.168.1.239:55443</c>.
        /// </summary>
        public string Location => this._location;

        /// <summary>
        /// Gets the address of the device, or <c>null</c> when its location does not hold an IP address.
        /// </summary>
        public IPAddress? Address => this._address;

        /// <summary>
        /// Gets the TCP port of the control service of the device.
        /// </summary>
        public int Port => this._port;

        /// <summary>
        /// Gets the product model, for example <c>color</c>, <c>mono</c>, <c>stripe</c>, <c>ceiling</c> or <c>bslamp</c>. The lights that
        /// are known by a number or a name of their own (<c>colora</c>, <c>strip6</c>) say so here. It is empty until the device has said.
        /// </summary>
        public string Model => this._model;

        /// <summary>
        /// Gets the version of the firmware, which is empty until the device has said.
        /// </summary>
        public string FirmwareVersion => this._firmwareVersion;

        /// <summary>
        /// Gets the name of the device, as stored on the device with <see cref="SetNameAsync(string, CancellationToken)"/> (the Yeelight app
        /// keeps its own names in the cloud, so the two can differ). It is empty when there is none.
        /// </summary>
        public string Name => this._name;

        /// <summary>
        /// Gets the methods that the device supports, as it lists them (<c>set_power</c>, <c>set_rgb</c>, ...). A command that is not on
        /// the list is refused by the device, and by this client before it is sent. The list is empty until the device has said.
        /// </summary>
        public IReadOnlyList<string> SupportedMethods => Volatile.Read(ref this._supportedMethods);

        /// <summary>
        /// Gets whether the device lists <c>set_rgb</c> or <c>set_hsv</c>: it can show colours.
        /// </summary>
        public bool SupportsColor => this.Supports("set_rgb") || this.Supports("set_hsv");

        /// <summary>
        /// Gets whether the device lists <c>set_ct_abx</c>: it can show a colour temperature.
        /// </summary>
        public bool SupportsColorTemperature => this.Supports("set_ct_abx");

        /// <summary>
        /// Gets whether the device lists <c>bg_set_power</c>: it has a <see cref="Background"/> light.
        /// </summary>
        public bool SupportsBackgroundLight => this.Supports("bg_set_power");

        /// <summary>
        /// Gets whether the device lists <c>set_music</c>: it can be put in music mode (see <see cref="StartMusicModeAsync"/>).
        /// </summary>
        public bool SupportsMusicMode => this.Supports("set_music");

        /// <summary>
        /// Gets the background light of the device. It always exists as an object, but only a device that has one lists its methods:
        /// look at <see cref="SupportsBackgroundLight"/>.
        /// </summary>
        public YeelightBackgroundLight Background => this._background;

        /// <summary>
        /// Gets whether there is a control connection to the device right now.
        /// </summary>
        public bool IsConnected => this._connection?.IsConnected == true;

        /// <summary>
        /// Gets the remaining time of the sleep timer (<see cref="SetSleepTimerAsync"/>), or <see cref="TimeSpan.Zero"/> when there is none.
        /// </summary>
        public TimeSpan SleepTimerRemaining => TimeSpan.FromMinutes(this._delayOff);

        /// <summary>
        /// Gets whether the device is in music mode (see <see cref="StartMusicModeAsync"/>).
        /// </summary>
        public bool IsMusicModeOn => this._musicOn;

        /// <summary>
        /// Gets the brightness of the night light mode of a ceiling light, a percentage (<c>0</c> until the device has reported it).
        /// </summary>
        public int NightLightBrightness => this._nightLightBrightness;

        /// <summary>
        /// Gets the mode of a ceiling light (daylight or moonlight), or <c>null</c> for a device that has not reported one.
        /// </summary>
        public ActiveMode? ActiveMode => this._activeMode;

        /// <summary>
        /// Gets when anything was last heard from the device: an announcement, an answer to a search, or a message on its connection. This
        /// does not raise <see cref="YeelightLight.PropertyChanged"/>.
        /// </summary>
        public DateTimeOffset LastSeen => new(Interlocked.Read(ref this._lastSeen), TimeSpan.Zero);

        /// <inheritdoc/>
        internal override YeelightDevice Owner => this;

        /// <summary>
        /// Gets whether the device lists a method among those that it supports.
        /// </summary>
        /// <param name="method">The method, as the protocol names it (<c>set_power</c>).</param>
        /// <returns><c>true</c> when the device lists it; <c>false</c> when it does not, and when it has not said what it supports.</returns>
        public bool Supports(string method)
        {
            ArgumentNullException.ThrowIfNull(method);
            return Array.IndexOf(this._support, method) >= 0;
        }

        /// <summary>
        /// Makes sure that there is a connection to the device, and waits until there is. The client does this by itself for every device
        /// that it finds (see <see cref="YeelightClientOptions.AutoConnect"/>), and a command makes the connection when there is none.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>A task that ends when the connection is there.</returns>
        /// <exception cref="YeelightException">There was no connection within <see cref="YeelightClientOptions.ConnectTimeout"/>: the
        /// connection is still tried (and the exception has the kind <see cref="YeelightFailureKind.NotConnected"/>), or the device was
        /// forgotten by the client.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            var connection = this.EnsureConnection()
                ?? throw new YeelightException(YeelightError.Create(YeelightFailureKind.NotConnected, "The light cannot be connected: it was forgotten, or its location is not usable."), null);
            if (!await connection.WaitConnectedAsync(this.Context.Options.ConnectTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new YeelightException(
                    YeelightError.Create(YeelightFailureKind.NotConnected, $"No connection to the light was made within {this.Context.Options.ConnectTimeout.TotalSeconds:0.#} seconds ({connection.Endpoint}). Is LAN Control switched on for the light?"),
                    null);
            }
        }

        /// <summary>
        /// Closes the connection to the device, which frees one of its four. The next command, or <see cref="ConnectAsync"/>, makes a new one.
        /// </summary>
        /// <returns>A task that ends when the connection is closed.</returns>
        public async Task DisconnectAsync()
        {
            YeelightConnection? connection;
            lock (this._gate)
            {
                connection = this._connection;
                this._connection = null;
            }

            if (connection is null)
            {
                return;
            }

            var wasConnected = connection.IsConnected;
            this.Detach(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            if (wasConnected)
            {
                this.Notify(nameof(this.IsConnected));
            }
        }

        /// <summary>
        /// Reads all the properties of the device (all the names that the protocol defines) and takes them over, so that the properties of
        /// this object are what the device says they are now. The client does this when a connection is made (see
        /// <see cref="YeelightClientOptions.RefreshOnConnect"/>), and the device reports every change after that.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the properties are taken over.</returns>
        /// <exception cref="YeelightException">The command did not succeed.</exception>
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return this.GetPropertiesAsync(YeelightProperties.All, cancellationToken);
        }

        /// <summary>
        /// Reads properties of the device with <c>get_prop</c>. What the device says is taken over by this object as well.
        /// </summary>
        /// <param name="names">The names of the properties, as the protocol has them: <c>power</c>, <c>bright</c>, <c>ct</c>, <c>rgb</c>, <c>hue</c>,
        /// <c>sat</c>, <c>color_mode</c>, <c>flowing</c>, <c>delayoff</c>, <c>flow_params</c>, <c>music_on</c>, <c>name</c>, <c>bg_power</c>,
        /// <c>bg_flowing</c>, <c>bg_flow_params</c>, <c>bg_ct</c>, <c>bg_lmode</c>, <c>bg_bright</c>, <c>bg_rgb</c>, <c>bg_hue</c>, <c>bg_sat</c>,
        /// <c>nl_br</c> and <c>active_mode</c>.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>The values by name, as text. A property that the device does not know has an empty value, as in the protocol.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="names"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="names"/> is empty, or has a name that is.</exception>
        /// <exception cref="YeelightException">The command did not succeed.</exception>
        public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(names);
            var list = names.ToArray();
            if (list.Length == 0 || list.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Name at least one property, and no property without a name.", nameof(names));
            }

            if (!this.AllowsMethod("get_prop"))
            {
                throw new NotSupportedException("The light does not support 'get_prop': it is not one of the methods that it lists.");
            }

            return await this.ReadPropertiesAsync(list, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets what <c>get_prop</c> says about the properties and takes it over; over the connection that is given, or over the one of the device.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, string>> ReadPropertiesAsync(string[] list, YeelightConnection? connection, CancellationToken cancellationToken)
        {
            connection ??= this.EnsureConnection();
            if (connection is null)
            {
                YeelightResult.Failed(YeelightFailureKind.NotConnected, "The light was forgotten by the client, or its location is not usable.").EnsureSuccess("get_prop");
            }

            // The values are taken over on the thread that reads the connection, as the answer comes in: whatever the light sent after the
            // answer is newer than it, and must not be overwritten by it, which the continuation of this method (it runs later) would do.
            Dictionary<string, string>? values = null;
            var result = await connection!.SendAsync(
                "get_prop",
                list,
                cancellationToken,
                answer =>
                {
                    if (!answer.Success)
                    {
                        return;
                    }

                    var read = new Dictionary<string, string>(list.Length, StringComparer.Ordinal);
                    for (var index = 0; index < list.Length; index++)
                    {
                        read[list[index]] = index < answer.Values.Count ? YeelightProtocol.ToText(answer.Values[index]) ?? string.Empty : string.Empty;
                    }

                    this.MarkSeen();
                    this.ApplyProperties(read);
                    values = read;
                }).ConfigureAwait(false);
            result.EnsureSuccess("get_prop");
            return values ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gives the device a name, which is stored on the device and reported when it is found (the Yeelight app keeps its own names in the
        /// cloud, so the two can differ).
        /// </summary>
        /// <param name="name">The name, at most 64 bytes in UTF-8. The specification suggests to encode a name with characters that are not
        /// ASCII with Base64 first.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The name is longer than 64 bytes.</exception>
        public Task SetNameAsync(string name, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (Encoding.UTF8.GetByteCount(name) > 64)
            {
                throw new ArgumentException("A name is at most 64 bytes long.", nameof(name));
            }

            return this.SendAsync("set_name", [name], cancellationToken);
        }

        /// <summary>
        /// Switches the main light and the background light, as they are, at the same time: on when it was off and the other way round. This
        /// is the <c>dev_toggle</c> method, which a device that has no background light may not list.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="NotSupportedException">The device lists the methods that it supports, and <c>dev_toggle</c> is not one of them.</exception>
        public Task ToggleAllAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("dev_toggle", [], cancellationToken);
        }

        /// <summary>
        /// Starts a sleep timer: the light is switched off after the time. A device has one timer, and a new one replaces it. Only a light
        /// that is on accepts it.
        /// </summary>
        /// <param name="duration">The time until the light is switched off, in whole minutes (a part of a minute is rounded up), at least one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The duration is shorter than a minute, or longer than the protocol can hold.</exception>
        public Task SetSleepTimerAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            if (duration < TimeSpan.FromMinutes(1) || duration.TotalMinutes > int.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "A sleep timer is at least a minute long.");
            }

            return this.SendAsync("cron_add", [0, (int)Math.Ceiling(duration.TotalMinutes)], cancellationToken);
        }

        /// <summary>
        /// Gets the sleep timer of the device.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>The length of the timer, or <c>null</c> when the device has none.</returns>
        /// <exception cref="YeelightException">The command did not succeed.</exception>
        public async Task<TimeSpan?> GetSleepTimerAsync(CancellationToken cancellationToken = default)
        {
            if (!this.AllowsMethod("cron_get"))
            {
                throw new NotSupportedException("The light does not support 'cron_get': it is not one of the methods that it lists.");
            }

            var result = await this.ExecuteAsync("cron_get", [0], cancellationToken).ConfigureAwait(false);
            result.EnsureSuccess("cron_get");
            foreach (var element in result.Values)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("delay", out var delay) && YeelightProtocol.TryGetInt32(delay, out var minutes))
                {
                    return TimeSpan.FromMinutes(minutes);
                }
            }

            return null;
        }

        /// <summary>
        /// Stops the sleep timer.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        public Task CancelSleepTimerAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("cron_del", [0], cancellationToken);
        }

        /// <summary>
        /// Puts the device in music mode: the device makes a connection to this computer, and takes the commands that are sent over it without
        /// a limit on their number (and without answering them), which is what it takes to follow music or a screen.
        /// </summary>
        /// <remarks>
        /// This starts a TCP server on this computer, tells the device where it is, and waits for the device to connect to it (the firewall
        /// of this computer has to let it through: a connection from the device to the port that it is told). Only a connection that comes from
        /// the address of the device is accepted. Dispose the session to leave music mode; the session also ends when the device closes the
        /// connection.
        /// </remarks>
        /// <param name="localAddress">The address of this computer that the device connects to: it has to be reachable from the device. When
        /// <c>null</c> it is the address that this computer uses to talk to the device, which is the right one on every network.</param>
        /// <param name="port">The TCP port to listen on, or <c>0</c> for a free one.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The session, to send commands to the device over.</returns>
        /// <exception cref="NotSupportedException">The device does not list <c>set_music</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not a port.</exception>
        /// <exception cref="YeelightException">The command did not succeed, or the device did not connect.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
        public async Task<YeelightMusicSession> StartMusicModeAsync(IPAddress? localAddress = null, int port = 0, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            if (!this.AllowsMethod("set_music"))
            {
                throw new NotSupportedException("The light does not support 'set_music': it is not one of the methods that it lists.");
            }

            await this.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var address = localAddress ?? this._connection?.LocalAddress
                ?? throw new YeelightException(YeelightError.Create(YeelightFailureKind.NotConnected, "The address of this computer on the network of the light is not known."), "set_music");
            var peer = this._address
                ?? throw new YeelightException(YeelightError.Create(YeelightFailureKind.NotConnected, "The address of the light is not known."), "set_music");
            var session = await YeelightMusicSession.StartAsync(this, address, port, peer, cancellationToken).ConfigureAwait(false);
            lock (this._gate)
            {
                if (!this._closed)
                {
                    this._musicSessions.Add(session);
                    _ = session.Completion.ContinueWith(
                        _ =>
                        {
                            lock (this._gate)
                            {
                                this._musicSessions.Remove(session);
                            }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return session;
                }
            }

            // The client was disposed while the light was connecting: the session ends with it.
            await session.DisposeAsync().ConfigureAwait(false);
            throw new YeelightException(YeelightError.Create(YeelightFailureKind.NotConnected, "The client was disposed."), "set_music");
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var name = this._name.Length > 0 ? this._name : this._model;
            return $"{name} ({this.Id}) at {this._location}";
        }

        /// <inheritdoc/>
        internal override async Task<YeelightResult> ExecuteAsync(string method, IReadOnlyList<object?> parameters, CancellationToken cancellationToken)
        {
            var connection = this.EnsureConnection();
            if (connection is null)
            {
                return YeelightResult.Failed(YeelightFailureKind.NotConnected, "The light was forgotten by the client, or its location is not usable.");
            }

            return await connection.SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        internal override bool AllowsMethod(string method)
        {
            var support = this._support;
            return support.Length == 0 || Array.IndexOf(support, method) >= 0;
        }

        /// <summary>
        /// Takes over what an announcement says about the device: its identity, its state, and a new address (the connection follows it).
        /// </summary>
        internal void Apply(DeviceAnnouncement announcement)
        {
            // A light that is connected is not moved by an announcement: it does not have two addresses, and a connection that works says where
            // it is. Anyone on the network can announce the id of a light from its own address, and would take the light over this way. A light
            // that really got a new address has lost its connection (the keep-alive finds that out), and the next announcement moves it.
            // Deciding and moving the connection are one step, with the change of the address of the device: a connection that comes up in
            // between is not cancelled, and a connection that is made after it is made to the new address.
            var relocated = !string.Equals(this._location, announcement.Location, StringComparison.Ordinal);
            var addressChanged = false;
            var portChanged = false;
            if (relocated)
            {
                var address = IPAddress.TryParse(announcement.Host, out var parsed) ? parsed : null;
                var refused = false;
                lock (this._gate)
                {
                    if (this.IsMove(announcement) && this._connection is { } current && !current.TryUpdateEndpoint(announcement.Host, announcement.Port))
                    {
                        refused = true;
                    }
                    else
                    {
                        addressChanged = !Equals(this._address, address);
                        portChanged = this._port != announcement.Port;
                        this._location = announcement.Location;
                        this._host = announcement.Host;
                        this._port = announcement.Port;
                        this._address = address;
                    }
                }

                if (refused)
                {
                    if (!string.Equals(this._ignoredMove, announcement.Location, StringComparison.Ordinal))
                    {
                        this._ignoredMove = announcement.Location;
                        this.Context.Logger.LogWarning(
                            "{Light}: An announcement says that it is at {Location}, but it is connected at {Current}. The announcement is ignored.",
                            this.Id,
                            announcement.Location,
                            this._location);
                    }

                    return;
                }

                this._ignoredMove = null;
            }

            this.MarkSeen();
            if (announcement.Model.Length > 0 && this._model != announcement.Model)
            {
                this._model = announcement.Model;
                this.Notify(nameof(this.Model));
            }

            if (announcement.FirmwareVersion.Length > 0 && this._firmwareVersion != announcement.FirmwareVersion)
            {
                this._firmwareVersion = announcement.FirmwareVersion;
                this.Notify(nameof(this.FirmwareVersion));
            }

            if (announcement.Support.Count > 0 && !this._support.AsSpan().SequenceEqual(announcement.Support.ToArray()))
            {
                var support = announcement.Support.ToArray();
                this._support = support;
                Volatile.Write(ref this._supportedMethods, Array.AsReadOnly(support));
                this.Notify(nameof(this.SupportedMethods));
            }

            this.ApplyProperties(announcement.Properties);

            if (relocated)
            {
                this.Notify(nameof(this.Location));
                if (addressChanged)
                {
                    this.Notify(nameof(this.Address));
                }

                if (portChanged)
                {
                    this.Notify(nameof(this.Port));
                }
            }
        }

        /// <summary>
        /// Makes the connection, when there is none, and starts it: what the client does for a device that it found.
        /// </summary>
        internal void Connect()
        {
            _ = this.EnsureConnection();
        }

        /// <summary>
        /// Closes the connection for good: the device was forgotten, or the client is disposed.
        /// </summary>
        internal async Task CloseAsync()
        {
            // A music session ends with the client, and tells the light so over the connection that is still there.
            YeelightMusicSession[] music;
            lock (this._gate)
            {
                music = [.. this._musicSessions];
                this._musicSessions.Clear();
            }

            foreach (var session in music)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            YeelightConnection? connection;
            lock (this._gate)
            {
                this._closed = true;
                connection = this._connection;
                this._connection = null;
                music = [.. this._musicSessions];
                this._musicSessions.Clear();
            }

            this._lifetime.Cancel();
            foreach (var session in music)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            if (connection is not null)
            {
                this.Detach(connection);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Takes over properties that the device reported (in an announcement, in <c>props</c>, in the answer to <c>get_prop</c>): those of the
        /// main light, those of the background light, and those of the device.
        /// </summary>
        internal void ApplyProperties(IEnumerable<KeyValuePair<string, string>> values)
        {
            foreach (var (key, value) in values)
            {
                if (this._background.ApplyBackgroundProperty(key, value) || this.ApplyState(key, value))
                {
                    continue;
                }

                switch (key)
                {
                    case YeelightProperties.Name:
                        var name = TextLimits.Truncate(value, 256);
                        if (!string.Equals(this._name, name, StringComparison.Ordinal))
                        {
                            this._name = name;
                            this.Notify(nameof(this.Name));
                        }

                        break;
                    case YeelightProperties.DelayOff:
                        if (LightState.TryNumber(value, 0, int.MaxValue / 2, out var minutes) && this._delayOff != minutes)
                        {
                            this._delayOff = minutes;
                            this.Notify(nameof(this.SleepTimerRemaining));
                        }

                        break;
                    case YeelightProperties.MusicOn:
                        if (value is "0" or "1" && this._musicOn != (value == "1"))
                        {
                            this._musicOn = value == "1";
                            this.Notify(nameof(this.IsMusicModeOn));
                        }

                        break;
                    case YeelightProperties.NightLightBrightness:
                        if (LightState.TryNumber(value, 0, 100, out var nightLight) && this._nightLightBrightness != nightLight)
                        {
                            this._nightLightBrightness = nightLight;
                            this.Notify(nameof(this.NightLightBrightness));
                        }

                        break;
                    case YeelightProperties.ActiveMode:
                        if (LightState.TryNumber(value, 0, 1, out var mode) && this._activeMode != (ActiveMode)mode)
                        {
                            this._activeMode = (ActiveMode)mode;
                            this.Notify(nameof(this.ActiveMode));
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// Gets the connection, making it (and starting it) when there is none. <c>null</c> when the device was closed or its location is
        /// not usable.
        /// </summary>
        private YeelightConnection? EnsureConnection()
        {
            YeelightConnection? connection;
            var created = false;
            lock (this._gate)
            {
                if (this._closed)
                {
                    return null;
                }

                connection = this._connection;
                if (connection is null)
                {
                    if (this._host.Length == 0 || this._port is < 1 or > 65535)
                    {
                        this.Context.Logger.LogWarning("{Light}: The location '{Location}' cannot be used.", this.Id, this._location);
                        return null;
                    }

                    connection = new YeelightConnection(this._host, this._port, this.Context.Connection, this.Context.Logger, this.Id);
                    connection.PropsReceived += this.OnPropsReceived;
                    connection.ConnectedChanged += this.OnConnectedChanged;
                    this._connection = connection;
                    created = true;
                }
            }

            if (created)
            {
                connection.Start();
            }

            return connection;
        }

        private void Detach(YeelightConnection connection)
        {
            connection.PropsReceived -= this.OnPropsReceived;
            connection.ConnectedChanged -= this.OnConnectedChanged;
        }

        /// <summary>Whether an announcement puts the device at another address (or port) than where it is. Called under the lock.</summary>
        private bool IsMove(DeviceAnnouncement announcement)
        {
            return !string.Equals(this._host, announcement.Host, StringComparison.OrdinalIgnoreCase) || this._port != announcement.Port;
        }

        private void OnConnectedChanged(bool connected)
        {
            this.MarkSeen();
            this.Notify(nameof(this.IsConnected));
            if (!connected || !this.Context.Options.RefreshOnConnect)
            {
                return;
            }

            // The refresh is for the connection that was just made: one that was closed, or replaced by a later one, by the time that it runs
            // is not asked for it. (This runs on the thread that made the connection, before it makes another.)
            YeelightConnection? connection;
            lock (this._gate)
            {
                connection = this._connection;
            }

            if (connection is not null)
            {
                var epoch = connection.Epoch;
                _ = Task.Run(() => this.RefreshAfterConnectAsync(connection, epoch));
            }
        }

        private async Task RefreshAfterConnectAsync(YeelightConnection connection, int epoch)
        {
            try
            {
                if (!this.IsCurrent(connection) || connection.Epoch != epoch || !connection.IsConnected)
                {
                    return;
                }

                if (!this.AllowsMethod("get_prop"))
                {
                    return;
                }

                await this.ReadPropertiesAsync(YeelightProperties.All.ToArray(), connection, this._lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is YeelightException or NotSupportedException or OperationCanceledException)
            {
                this.Context.Logger.LogDebug("{Light}: The properties could not be read after connecting: {Reason}", this.Id, ex.Message);
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning(ex, "{Light}: The properties could not be read after connecting.", this.Id);
            }
        }

        private bool IsCurrent(YeelightConnection connection)
        {
            lock (this._gate)
            {
                return ReferenceEquals(this._connection, connection);
            }
        }

        private void OnPropsReceived(IReadOnlyDictionary<string, JsonElement> properties)
        {
            var values = new List<KeyValuePair<string, string>>(properties.Count);
            foreach (var (key, element) in properties)
            {
                if (YeelightProtocol.ToText(element) is { } text)
                {
                    values.Add(new KeyValuePair<string, string>(key, text));
                }
            }

            this.MarkSeen();
            this.ApplyProperties(values);
        }

        private void MarkSeen()
        {
            Interlocked.Exchange(ref this._lastSeen, this.Context.Time.GetUtcNow().UtcTicks);
        }
    }
}
