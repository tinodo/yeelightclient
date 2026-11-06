namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A command that a simulated light received, as it was sent.
    /// </summary>
    /// <param name="Id">The number of the command.</param>
    /// <param name="Method">The method.</param>
    /// <param name="Parameters">The parameters.</param>
    /// <param name="Raw">The line, as it came in.</param>
    /// <param name="OverMusic">Whether it came in over the music mode connection.</param>
    internal sealed record SimulatedRequest(int Id, string Method, JsonElement[] Parameters, string Raw, bool OverMusic = false);

    /// <summary>
    /// What a simulated light is made of.
    /// </summary>
    internal sealed record SimulatedBulbOptions
    {
        public const string AllMethods =
            "get_prop set_default set_power toggle set_bright start_cf stop_cf set_scene cron_add cron_get cron_del set_ct_abx set_rgb set_hsv set_adjust set_music set_name " +
            "adjust_bright adjust_ct adjust_color";

        public const string BackgroundMethods =
            "bg_set_rgb bg_set_hsv bg_set_ct_abx bg_start_cf bg_stop_cf bg_set_scene bg_set_default bg_set_power bg_set_bright bg_set_adjust bg_toggle dev_toggle " +
            "bg_adjust_bright bg_adjust_ct bg_adjust_color";

        public string Id { get; init; } = "0x000000000015243f";

        public string Model { get; init; } = "color";

        public string FirmwareVersion { get; init; } = "18";

        public string Name { get; init; } = "my_bulb";

        /// <summary>The methods that the light lists, separated by spaces.</summary>
        public string Support { get; init; } = AllMethods;

        /// <summary>Whether the light has a background light (and lists its methods).</summary>
        public bool HasBackground { get; init; }

        /// <summary>The TCP port of the control service (0 takes a free one).</summary>
        public int ControlPort { get; init; }

        /// <summary>Whether the light answers a search, and announces itself, over UDP.</summary>
        public bool Discoverable { get; init; } = true;

        /// <summary>The number of commands a minute that a connection may send before the light says <c>client quota exceeded</c>.</summary>
        public int CommandsPerMinute { get; init; } = 60;

        /// <summary>The number of connections that the light accepts: it closes the fifth.</summary>
        public int MaxConnections { get; init; } = 4;
    }

    /// <summary>
    /// A Yeelight device on the loopback: it answers a search over UDP and takes connections over TCP, and behaves the way the specification
    /// says (and the way the real lights that were measured do): a state that the commands change, a report of every change to every
    /// connection, the commands that only an "on" light accepts, a quota on every connection, four connections at most, and music mode.
    /// </summary>
    /// <remarks>
    /// It is made for tests and for the test app: nothing here leaves the loopback. A test can make things go wrong with
    /// <see cref="ReplyOverride"/> (another answer than the right one), <see cref="Delay"/>, <see cref="ChunkSize"/>,
    /// <see cref="CloseImmediately"/> and <see cref="DropConnections"/>.
    /// </remarks>
    internal sealed class SimulatedBulb : IAsyncDisposable
    {
        private readonly SimulatedBulbOptions _options;
        private readonly TcpListener _tcp;
        private readonly UdpClient? _udp;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<SimulatedRequest> _requests = new();
        private readonly List<Client> _clients = [];
        private readonly List<string> _searches = [];
        private readonly Dictionary<string, string> _properties;
        private readonly HashSet<string> _support;
        private readonly object _gate = new();
        private readonly Task _accepting;
        private readonly Task? _answering;
        private int _connections;
        private Client? _music;

        public SimulatedBulb(SimulatedBulbOptions? options = null)
        {
            this._options = options ?? new SimulatedBulbOptions();
            this.Name = this._options.Name;
            this._support = new HashSet<string>(
                (this._options.Support + (this._options.HasBackground ? " " + SimulatedBulbOptions.BackgroundMethods : string.Empty))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            this._properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["power"] = "on",
                ["bright"] = "100",
                ["ct"] = "4000",
                ["rgb"] = "16711680",
                ["hue"] = "100",
                ["sat"] = "35",
                ["color_mode"] = "2",
                ["flowing"] = "0",
                ["delayoff"] = "0",
                ["flow_params"] = string.Empty,
                ["music_on"] = "0",
                ["name"] = this._options.Name,
                ["nl_br"] = "0",
                ["active_mode"] = "0",
            };
            if (this._options.HasBackground)
            {
                this._properties["bg_power"] = "off";
                this._properties["bg_flowing"] = "0";
                this._properties["bg_flow_params"] = string.Empty;
                this._properties["bg_ct"] = "3000";
                this._properties["bg_lmode"] = "2";
                this._properties["bg_bright"] = "50";
                this._properties["bg_rgb"] = "255";
                this._properties["bg_hue"] = "240";
                this._properties["bg_sat"] = "100";
            }

            this._tcp = new TcpListener(IPAddress.Loopback, this._options.ControlPort);
            this._tcp.Start();
            this.ControlPort = ((IPEndPoint)this._tcp.LocalEndpoint).Port;
            this._accepting = Task.Run(this.AcceptAsync);

            if (this._options.Discoverable)
            {
                this._udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                this.DiscoveryPort = ((IPEndPoint)this._udp.Client.LocalEndPoint!).Port;
                this._answering = Task.Run(this.AnswerAsync);
            }
        }

        private sealed class Client(TcpClient tcp, bool music)
        {
            private readonly object _order = new();
            private Task _tail = Task.CompletedTask;

            public TcpClient Tcp { get; } = tcp;

            public bool Music { get; } = music;

            public SemaphoreSlim WriteLock { get; } = new(1, 1);

            public Queue<long> Window { get; } = new();

            /// <summary>Runs the work after the work that was queued before it, so that what is sent over one connection goes out in the order it was decided.</summary>
            public void Enqueue(Func<Task> work)
            {
                lock (this._order)
                {
                    this._tail = RunAfterAsync(this._tail, work);
                }
            }

            private static async Task RunAfterAsync(Task previous, Func<Task> work)
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    await work().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The connection ended: there is nothing to send to.
                }
            }
        }

        /// <summary>The id of the light.</summary>
        public string Id => this._options.Id;

        /// <summary>The model of the light.</summary>
        public string Model => this._options.Model;

        /// <summary>The name of the light, as <c>set_name</c> left it.</summary>
        public string Name { get; private set; }

        /// <summary>The TCP port of the control service.</summary>
        public int ControlPort { get; }

        /// <summary>The UDP port that the light answers searches on (0 when it is not discoverable).</summary>
        public int DiscoveryPort { get; }

        /// <summary>Where a search is sent to reach the light.</summary>
        public IPEndPoint DiscoveryEndPoint => new(IPAddress.Loopback, this.DiscoveryPort);

        /// <summary>The location of the light, as it says it in an answer.</summary>
        public string Location => $"yeelight://127.0.0.1:{this.ControlPort}";

        /// <summary>The number of control connections that are open right now, as far as the light knows.</summary>
        public int ClientCount
        {
            get
            {
                lock (this._gate)
                {
                    return this._clients.Count(client => !client.Music);
                }
            }
        }

        /// <summary>The number of control connections that were made to the light.</summary>
        public int ConnectionCount => Volatile.Read(ref this._connections);

        /// <summary>Whether a music mode connection is open.</summary>
        public bool InMusicMode
        {
            get
            {
                lock (this._gate)
                {
                    return this._music is not null;
                }
            }
        }

        /// <summary>Every command that the light received, in order.</summary>
        public IReadOnlyList<SimulatedRequest> Requests => [.. this._requests];

        /// <summary>The commands of one method.</summary>
        public IReadOnlyList<SimulatedRequest> RequestsOf(string method) => [.. this._requests.Where(request => request.Method == method)];

        /// <summary>The number of searches that the light received.</summary>
        public int SearchCount
        {
            get
            {
                lock (this._gate)
                {
                    return this._searches.Count;
                }
            }
        }

        /// <summary>Every search that the light received, as it came in.</summary>
        public IReadOnlyList<string> Searches
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._searches];
                }
            }
        }

        /// <summary>Whether the light answers a search. Off, it stays silent.</summary>
        public bool AnswersSearches { get; set; } = true;

        /// <summary>
        /// Chooses what the light sends back for a command, instead of the right answer: lines, without their line break. Returns
        /// <c>null</c> for the right answer.
        /// </summary>
        public Func<SimulatedRequest, IReadOnlyList<string>?>? ReplyOverride { get; set; }

        /// <summary>How long the light waits before it sends the answer to a command.</summary>
        public Func<SimulatedRequest, TimeSpan> Delay { get; set; } = _ => TimeSpan.Zero;

        /// <summary>The number of bytes that are written at a time (zero is all at once), to cut the messages into pieces.</summary>
        public int ChunkSize { get; set; }

        /// <summary>When set, a connection is closed as soon as it is accepted.</summary>
        public bool CloseImmediately { get; set; }

        /// <summary>Whether a light that is told to start music mode connects to the address that it is given. Off, it says <c>ok</c> and stays away.</summary>
        public bool ConnectsForMusic { get; set; } = true;

        /// <summary>Whether a command that only a light that is on accepts is refused by one that is off, as the specification says.</summary>
        public bool RequireOnForChanges { get; set; } = true;

        /// <summary>Raised on a thread of the thread pool whenever a property of the light changes.</summary>
        public event Action<string, string>? PropertyChanged;

        /// <summary>The value of a property, as the light reports it.</summary>
        public string Get(string property)
        {
            lock (this._gate)
            {
                return this._properties.TryGetValue(property, out var value) ? value : string.Empty;
            }
        }

        /// <summary>
        /// Changes properties as if somebody did it at the light (the switch on the wall, the app of Yeelight): the light reports them to
        /// every connection.
        /// </summary>
        public async Task ChangeAsync(params (string Property, string Value)[] changes)
        {
            Dictionary<string, string> changed;
            lock (this._gate)
            {
                changed = this.Apply(changes.Select(change => KeyValuePair.Create(change.Property, change.Value)));
            }

            await this.ReportAsync(changed).ConfigureAwait(false);
        }

        /// <summary>Sends a message that was not asked for (such as <c>props</c>) to every control connection.</summary>
        public async Task NotifyAsync(string json)
        {
            Client[] clients;
            lock (this._gate)
            {
                clients = [.. this._clients.Where(client => !client.Music)];
            }

            foreach (var client in clients)
            {
                await this.WriteAsync(client, json + "\r\n").ConfigureAwait(false);
            }
        }

        /// <summary>Sends an announcement (<c>NOTIFY * HTTP/1.1</c>) to an end point, from the address and port of the discovery of the light.</summary>
        public async Task AnnounceAsync(IPEndPoint target)
        {
            await this.SendDatagramAsync(target, this.Message(advertisement: true)).ConfigureAwait(false);
        }

        /// <summary>Sends a datagram to an end point, from the address and port of the discovery of the light.</summary>
        public async Task SendDatagramAsync(IPEndPoint target, string message)
        {
            if (this._udp is null)
            {
                throw new InvalidOperationException("The light is not discoverable.");
            }

            var bytes = Encoding.UTF8.GetBytes(message);
            await this._udp.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
        }

        /// <summary>The answer to a search, or the announcement, with the current state of the light.</summary>
        public string Message(bool advertisement, string? location = null)
        {
            string name;
            string power;
            string bright;
            string mode;
            string ct;
            string rgb;
            string hue;
            string sat;
            lock (this._gate)
            {
                name = this._properties["name"];
                power = this._properties["power"];
                bright = this._properties["bright"];
                mode = this._properties["color_mode"];
                ct = this._properties["ct"];
                rgb = this._properties["rgb"];
                hue = this._properties["hue"];
                sat = this._properties["sat"];
            }

            var head = advertisement
                ? "NOTIFY * HTTP/1.1\r\nHost: 239.255.255.250:1982\r\nCache-Control: max-age=3600\r\n" +
                  $"Location: {location ?? this.Location}\r\nNTS: ssdp:alive\r\nServer: POSIX, UPnP/1.0 YGLC/1\r\n"
                : "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: \r\nExt: \r\n" +
                  $"Location: {location ?? this.Location}\r\nServer: POSIX UPnP/1.0 YGLC/1\r\n";
            return head +
                $"id: {this._options.Id}\r\nmodel: {this._options.Model}\r\nfw_ver: {this._options.FirmwareVersion}\r\n" +
                $"support: {string.Join(' ', this._support.Order(StringComparer.Ordinal))}\r\n" +
                $"power: {power}\r\nbright: {bright}\r\ncolor_mode: {mode}\r\nct: {ct}\r\nrgb: {rgb}\r\nhue: {hue}\r\nsat: {sat}\r\nname: {name}\r\n";
        }

        /// <summary>Closes every connection, and keeps listening: the next connect is accepted.</summary>
        public void DropConnections()
        {
            lock (this._gate)
            {
                foreach (var client in this._clients)
                {
                    client.Tcp.Dispose();
                }

                this._clients.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            this._stop.Cancel();
            this._tcp.Stop();
            this._udp?.Dispose();
            this.DropConnections();
            foreach (var task in new[] { this._accepting, this._answering }.OfType<Task>())
            {
                try
                {
                    await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The loop ends by itself.
                }
            }
        }

        private async Task AcceptAsync()
        {
            while (!this._stop.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await this._tcp.AcceptTcpClientAsync(this._stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref this._connections);
                Client client;
                lock (this._gate)
                {
                    if (this.CloseImmediately || this._clients.Count(c => !c.Music) >= this._options.MaxConnections)
                    {
                        tcp.Dispose();
                        continue;
                    }

                    client = new Client(tcp, music: false);
                    this._clients.Add(client);
                }

                _ = Task.Run(() => this.ServeAsync(client));
            }
        }

        private async Task AnswerAsync()
        {
            var udp = this._udp!;
            while (!this._stop.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await udp.ReceiveAsync(this._stop.Token).ConfigureAwait(false);
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
                    continue;
                }
                catch (SocketException)
                {
                    return;
                }

                var text = Encoding.UTF8.GetString(received.Buffer);
                if (!text.StartsWith("M-SEARCH", StringComparison.Ordinal))
                {
                    continue;
                }

                lock (this._gate)
                {
                    this._searches.Add(text);
                }

                // The rules of the specification: the headers are not case sensitive, the values are, and anything else is dropped silently.
                var valid = text.Contains("MAN: \"ssdp:discover\"", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("ST: wifi_bulb", StringComparison.Ordinal);
                if (!valid || !this.AnswersSearches)
                {
                    continue;
                }

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(this.Message(advertisement: false));
                    await udp.SendAsync(bytes, bytes.Length, received.RemoteEndPoint).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    // The searcher went away.
                }
            }
        }

        private async Task ServeAsync(Client client)
        {
            try
            {
                using var reader = new StreamReader(client.Tcp.GetStream(), Encoding.UTF8);
                while (await reader.ReadLineAsync(this._stop.Token).ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    var request = Parse(line, client.Music);
                    this._requests.Enqueue(request);
                    this.Handle(client, request);
                }
            }
            catch (Exception)
            {
                // The connection ended.
            }
            finally
            {
                Dictionary<string, string> changed = [];
                lock (this._gate)
                {
                    this._clients.Remove(client);
                    if (ReferenceEquals(this._music, client))
                    {
                        this._music = null;
                        changed = this.Apply([KeyValuePair.Create("music_on", "0")]);
                    }
                }

                client.Tcp.Dispose();

                // The light leaves music mode, and reports that again.
                await this.ReportAsync(changed).ConfigureAwait(false);
            }
        }

        private void Handle(Client client, SimulatedRequest request)
        {
            if (client.Music)
            {
                // Music mode: nothing is answered, nothing is counted and nothing is reported.
                lock (this._gate)
                {
                    _ = this.Execute(request, out _, out _);
                }

                return;
            }

            IReadOnlyList<string> lines;
            Dictionary<string, string> changed = [];
            Func<Task>? afterReply = null;
            lock (this._gate)
            {
                if (this.OverQuota(client))
                {
                    lines = [Error(request.Id, -1, "client quota exceeded")];
                }
                else if (this.ReplyOverride?.Invoke(request) is { } custom)
                {
                    lines = custom;
                }
                else
                {
                    lines = [this.Execute(request, out changed, out afterReply)];
                }
            }

            var delay = this.Delay(request);
            Func<Task> deliver = async () =>
            {
                foreach (var line in lines)
                {
                    await this.WriteAsync(client, line + "\r\n").ConfigureAwait(false);
                }

                await this.ReportAsync(changed).ConfigureAwait(false);
                if (afterReply is not null)
                {
                    await afterReply().ConfigureAwait(false);
                }
            };

            if (delay > TimeSpan.Zero)
            {
                // A delayed answer is late on purpose: the answers to the commands after it can overtake it.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    await deliver().ConfigureAwait(false);
                });
            }
            else
            {
                // A light is one loop that answers in the order it decided: so are the answers and reports of one connection.
                client.Enqueue(deliver);
            }
        }

        private bool OverQuota(Client client)
        {
            var now = Environment.TickCount64;
            while (client.Window.Count > 0 && now - client.Window.Peek() >= 60_000)
            {
                client.Window.Dequeue();
            }

            if (client.Window.Count >= this._options.CommandsPerMinute)
            {
                return true;
            }

            client.Window.Enqueue(now);
            return false;
        }

        private async Task ReportAsync(Dictionary<string, string> changed)
        {
            if (changed.Count == 0)
            {
                return;
            }

            foreach (var pair in changed)
            {
                this.PropertyChanged?.Invoke(pair.Key, pair.Value);
            }

            // Under music mode no property is reported.
            if (this.InMusicMode)
            {
                return;
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["method"] = "props", ["params"] = changed });
            await this.NotifyAsync(json).ConfigureAwait(false);
        }

        private string Execute(SimulatedRequest request, out Dictionary<string, string> changed, out Func<Task>? afterReply)
        {
            changed = [];
            afterReply = null;
            var p = request.Parameters;
            var id = request.Id;
            var method = request.Method;
            if (!this._support.Contains(method))
            {
                return Error(id, -1, "unsupported method");
            }

            var background = method.StartsWith("bg_", StringComparison.Ordinal);
            var core = background ? method[3..] : method;
            string Key(string name) => background ? (name == "color_mode" ? "bg_lmode" : "bg_" + name) : name;
            var isOn = this._properties.GetValueOrDefault(Key("power")) == "on";
            string Ok() => $"{{\"id\":{id},\"result\":[\"ok\"]}}";
            string Invalid() => Error(id, -1, "invalid params");

            // Only a light that is on accepts these (the notes of the specification).
            if (this.RequireOnForChanges && !isOn && core is "set_ct_abx" or "set_rgb" or "set_hsv" or "set_bright" or "set_default" or "start_cf" or "cron_add")
            {
                return Error(id, -1, "general error");
            }

            switch (core)
            {
                case "get_prop":
                    if (background || p.Length == 0 || p.Any(value => value.ValueKind != JsonValueKind.String))
                    {
                        return Invalid();
                    }

                    var values = p.Select(value => JsonSerializer.Serialize(this._properties.GetValueOrDefault(value.GetString()!, string.Empty)));
                    return $"{{\"id\":{id},\"result\":[{string.Join(',', values)}]}}";

                case "set_power":
                    if (p.Length is < 3 or > 4 || !Text(p[0], out var power) || power is not ("on" or "off") || !Effect(p, 1))
                    {
                        return Invalid();
                    }

                    var change = new List<KeyValuePair<string, string>> { KeyValuePair.Create(Key("power"), power) };
                    if (p.Length == 4)
                    {
                        if (!Number(p[3], out var mode) || mode is < 0 or > 5 || power == "off")
                        {
                            return Invalid();
                        }

                        switch (mode)
                        {
                            case 1:
                                change.Add(KeyValuePair.Create(Key("color_mode"), "2"));
                                break;
                            case 2:
                                change.Add(KeyValuePair.Create(Key("color_mode"), "1"));
                                break;
                            case 3:
                                change.Add(KeyValuePair.Create(Key("color_mode"), "3"));
                                break;
                            case 4:
                                change.Add(KeyValuePair.Create(Key("flowing"), "1"));
                                break;
                            case 5:
                                change.Add(KeyValuePair.Create("active_mode", "1"));
                                break;
                        }
                    }

                    changed = this.Apply(change);
                    return Ok();

                case "toggle":
                    changed = this.Apply([KeyValuePair.Create(Key("power"), isOn ? "off" : "on")]);
                    return Ok();

                case "dev_toggle":
                    var next = isOn ? "off" : "on";
                    var both = new List<KeyValuePair<string, string>> { KeyValuePair.Create("power", next) };
                    if (this._properties.ContainsKey("bg_power"))
                    {
                        both.Add(KeyValuePair.Create("bg_power", next));
                    }

                    changed = this.Apply(both);
                    return Ok();

                case "set_bright":
                    if (p.Length != 3 || !Number(p[0], out var brightness) || brightness is < 1 or > 100 || !Effect(p, 1))
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create(Key("bright"), brightness.ToString(CultureInfo.InvariantCulture))]);
                    return Ok();

                case "set_ct_abx":
                    if (p.Length != 3 || !Number(p[0], out var kelvin) || kelvin is < 1700 or > 6500 || !Effect(p, 1))
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create(Key("ct"), kelvin.ToString(CultureInfo.InvariantCulture)), KeyValuePair.Create(Key("color_mode"), "2")]);
                    return Ok();

                case "set_rgb":
                    if (p.Length != 3 || !Number(p[0], out var rgb) || rgb is < 0 or > 0xFFFFFF || !Effect(p, 1))
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create(Key("rgb"), rgb.ToString(CultureInfo.InvariantCulture)), KeyValuePair.Create(Key("color_mode"), "1")]);
                    return Ok();

                case "set_hsv":
                    if (p.Length != 4 || !Number(p[0], out var hue) || hue is < 0 or > 359 || !Number(p[1], out var sat) || sat is < 0 or > 100 || !Effect(p, 2))
                    {
                        return Invalid();
                    }

                    changed = this.Apply([
                        KeyValuePair.Create(Key("hue"), hue.ToString(CultureInfo.InvariantCulture)),
                        KeyValuePair.Create(Key("sat"), sat.ToString(CultureInfo.InvariantCulture)),
                        KeyValuePair.Create(Key("color_mode"), "3")]);
                    return Ok();

                case "set_default":
                    return Ok();

                case "start_cf":
                    if (p.Length != 3 || !Number(p[0], out var count) || count < 0 || !Number(p[1], out var action) || action is < 0 or > 2 || !Text(p[2], out var expression) || !Flow(expression))
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create(Key("flowing"), "1"), KeyValuePair.Create(Key("flow_params"), $"{count},{action},{expression}")]);
                    return Ok();

                case "stop_cf":
                    changed = this.Apply([KeyValuePair.Create(Key("flowing"), "0"), KeyValuePair.Create(Key("flow_params"), string.Empty)]);
                    return Ok();

                case "set_scene":
                    return this.Scene(p, Key, id, out changed);

                case "cron_add":
                    if (p.Length != 2 || !Number(p[0], out var type) || type != 0 || !Number(p[1], out var minutes) || minutes < 1)
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create("delayoff", minutes.ToString(CultureInfo.InvariantCulture))]);
                    return Ok();

                case "cron_get":
                    if (p.Length != 1 || !Number(p[0], out var kind) || kind != 0)
                    {
                        return Invalid();
                    }

                    var delay = this._properties["delayoff"];
                    return delay == "0"
                        ? $"{{\"id\":{id},\"result\":[]}}"
                        : $"{{\"id\":{id},\"result\":[{{\"type\":0,\"delay\":{delay},\"mix\":0}}]}}";

                case "cron_del":
                    if (p.Length != 1 || !Number(p[0], out var deleted) || deleted != 0)
                    {
                        return Invalid();
                    }

                    changed = this.Apply([KeyValuePair.Create("delayoff", "0")]);
                    return Ok();

                case "set_adjust":
                    if (p.Length != 2 || !Text(p[0], out var direction) || direction is not ("increase" or "decrease" or "circle") || !Text(p[1], out var property) || property is not ("bright" or "ct" or "color")
                        || (property == "color" && direction != "circle"))
                    {
                        return Invalid();
                    }

                    changed = this.Adjust(Key, direction, property);
                    return Ok();

                case "adjust_bright":
                case "adjust_ct":
                case "adjust_color":
                    if (p.Length != 2 || !Number(p[0], out var percentage) || percentage is < -100 or > 100 || !Number(p[1], out var duration) || duration < 0)
                    {
                        return Invalid();
                    }

                    if (core == "adjust_bright")
                    {
                        var level = int.Parse(this._properties[Key("bright")], CultureInfo.InvariantCulture);
                        changed = this.Apply([KeyValuePair.Create(Key("bright"), Math.Clamp(level + percentage, 1, 100).ToString(CultureInfo.InvariantCulture))]);
                    }

                    return Ok();

                case "set_name":
                    if (p.Length != 1 || !Text(p[0], out var name) || Encoding.UTF8.GetByteCount(name) > 64)
                    {
                        return Invalid();
                    }

                    this.Name = name;
                    changed = this.Apply([KeyValuePair.Create("name", name)]);
                    return Ok();

                case "set_music":
                    if (p.Length == 1 && Number(p[0], out var off) && off == 0)
                    {
                        this._music?.Tcp.Dispose();
                        return Ok();
                    }

                    if (p.Length != 3 || !Number(p[0], out var on) || on != 1 || !Text(p[1], out var host) || !IPAddress.TryParse(host, out var address) || !Number(p[2], out var port) || port is < 1 or > 65535)
                    {
                        return Invalid();
                    }

                    if (this.ConnectsForMusic)
                    {
                        afterReply = () => this.ConnectMusicAsync(new IPEndPoint(address, port));
                    }

                    return Ok();
            }

            return Error(id, -1, "unsupported method");

            static bool Number(JsonElement element, out int value)
            {
                value = 0;
                return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
            }

            static bool Text(JsonElement element, out string value)
            {
                value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
                return element.ValueKind == JsonValueKind.String;
            }

            static bool Effect(JsonElement[] parameters, int index)
            {
                if (parameters.Length <= index + 1 || !Text(parameters[index], out var effect) || !Number(parameters[index + 1], out var time))
                {
                    return false;
                }

                return effect == "sudden" || (effect == "smooth" && time >= 30);
            }

            static bool Flow(string expression)
            {
                var parts = expression.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 0 || parts.Length % 4 != 0)
                {
                    return false;
                }

                for (var index = 0; index < parts.Length; index += 4)
                {
                    if (!int.TryParse(parts[index], out var time) || time < 50
                        || !int.TryParse(parts[index + 1], out var kind) || kind is not (1 or 2 or 7)
                        || !int.TryParse(parts[index + 2], out _) || !int.TryParse(parts[index + 3], out var level)
                        || (kind != 7 && level != -1 && (level < 1 || level > 100)))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private string Scene(JsonElement[] p, Func<string, string> key, int id, out Dictionary<string, string> changed)
        {
            changed = [];
            string Invalid() => Error(id, -1, "invalid params");
            if (p.Length < 3 || p[0].ValueKind != JsonValueKind.String)
            {
                return Invalid();
            }

            int N(int index) => p[index].ValueKind == JsonValueKind.Number && p[index].TryGetInt32(out var value) ? value : int.MinValue;
            var change = new List<KeyValuePair<string, string>> { KeyValuePair.Create(key("power"), "on") };
            switch (p[0].GetString())
            {
                case "color" when p.Length == 3 && N(1) is >= 0 and <= 0xFFFFFF && N(2) is >= 1 and <= 100:
                    change.Add(KeyValuePair.Create(key("rgb"), N(1).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("bright"), N(2).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("color_mode"), "1"));
                    break;
                case "hsv" when p.Length == 4 && N(1) is >= 0 and <= 359 && N(2) is >= 0 and <= 100 && N(3) is >= 1 and <= 100:
                    change.Add(KeyValuePair.Create(key("hue"), N(1).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("sat"), N(2).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("bright"), N(3).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("color_mode"), "3"));
                    break;
                case "ct" when p.Length == 3 && N(1) is >= 1700 and <= 6500 && N(2) is >= 1 and <= 100:
                    change.Add(KeyValuePair.Create(key("ct"), N(1).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("bright"), N(2).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create(key("color_mode"), "2"));
                    break;
                case "cf" when p.Length == 4 && N(1) >= 0 && N(2) is >= 0 and <= 2 && p[3].ValueKind == JsonValueKind.String:
                    change.Add(KeyValuePair.Create(key("flowing"), "1"));
                    change.Add(KeyValuePair.Create(key("flow_params"), $"{N(1)},{N(2)},{p[3].GetString()}"));
                    break;
                case "auto_delay_off" when p.Length == 3 && N(1) is >= 1 and <= 100 && N(2) >= 1:
                    change.Add(KeyValuePair.Create(key("bright"), N(1).ToString(CultureInfo.InvariantCulture)));
                    change.Add(KeyValuePair.Create("delayoff", N(2).ToString(CultureInfo.InvariantCulture)));
                    break;
                default:
                    return Invalid();
            }

            changed = this.Apply(change);
            return $"{{\"id\":{id},\"result\":[\"ok\"]}}";
        }

        private Dictionary<string, string> Adjust(Func<string, string> key, string direction, string property)
        {
            var name = key(property switch { "bright" => "bright", "ct" => "ct", _ => "hue" });
            var current = int.Parse(this._properties[name], CultureInfo.InvariantCulture);
            var (minimum, maximum, step) = property switch
            {
                "bright" => (1, 100, 10),
                "ct" => (1700, 6500, 480),
                _ => (0, 359, 30),
            };
            var next = direction switch
            {
                "increase" => Math.Min(maximum, current + step),
                "decrease" => Math.Max(minimum, current - step),
                _ => current + step > maximum ? minimum : current + step,
            };
            var change = new List<KeyValuePair<string, string>> { KeyValuePair.Create(name, next.ToString(CultureInfo.InvariantCulture)) };
            if (property == "color")
            {
                change.Add(KeyValuePair.Create(key("color_mode"), "3"));
            }

            return this.Apply(change);
        }

        /// <summary>Sets properties, and returns those that changed.</summary>
        private Dictionary<string, string> Apply(IEnumerable<KeyValuePair<string, string>> values)
        {
            var changed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                if (!this._properties.TryGetValue(key, out var current) || current != value)
                {
                    this._properties[key] = value;
                    changed[key] = value;
                }
            }

            return changed;
        }

        private async Task ConnectMusicAsync(IPEndPoint target)
        {
            try
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(target, this._stop.Token).ConfigureAwait(false);
                var client = new Client(tcp, music: true);
                lock (this._gate)
                {
                    this._music = client;
                    this._clients.Add(client);

                    // In music mode nothing is reported, so the change of this property is not either.
                    _ = this.Apply([KeyValuePair.Create("music_on", "1")]);
                }

                _ = Task.Run(() => this.ServeAsync(client));
            }
            catch (Exception)
            {
                // Nobody listens there: the light does not go into music mode.
            }
        }

        private static string Error(int id, int code, string message)
        {
            return $"{{\"id\":{id},\"error\":{{\"code\":{code},\"message\":{JsonSerializer.Serialize(message)}}}}}";
        }

        private static SimulatedRequest Parse(string line, bool music)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var parameters = root.TryGetProperty("params", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(value => value.Clone()).ToArray()
                : [];
            return new SimulatedRequest(root.GetProperty("id").GetInt32(), root.GetProperty("method").GetString() ?? string.Empty, parameters, line, music);
        }

        private async Task WriteAsync(Client client, string text)
        {
            try
            {
                await client.WriteLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var stream = client.Tcp.GetStream();
                    var bytes = Encoding.UTF8.GetBytes(text);
                    if (this.ChunkSize <= 0)
                    {
                        await stream.WriteAsync(bytes).ConfigureAwait(false);
                    }
                    else
                    {
                        for (var offset = 0; offset < bytes.Length; offset += this.ChunkSize)
                        {
                            await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(this.ChunkSize, bytes.Length - offset))).ConfigureAwait(false);
                            await stream.FlushAsync().ConfigureAwait(false);
                            await Task.Delay(1).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    client.WriteLock.Release();
                }
            }
            catch (Exception)
            {
                // The connection was closed.
            }
        }
    }
}
