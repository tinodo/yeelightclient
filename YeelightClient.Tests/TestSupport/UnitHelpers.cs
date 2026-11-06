namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;

    /// <summary>
    /// A clock whose timers are as manual as its time: nothing fires until a test moves the clock past it, so that a <c>Task.Delay</c> or a
    /// <c>WaitAsync</c> on it takes no real time at all (the timers of <see cref="ManualTimeProvider"/> are the real ones).
    /// </summary>
    internal sealed class UnitClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly object _gate = new();
        private readonly List<FakeTimer> _timers = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>The number of timers that are waiting to fire.</summary>
        public int PendingTimers
        {
            get
            {
                lock (this._gate)
                {
                    return this._timers.Count(timer => timer.Due is not null);
                }
            }
        }

        public override long GetTimestamp()
        {
            lock (this._gate)
            {
                return this._ticks;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (this._gate)
            {
                return Epoch.AddTicks(this._ticks);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state);
            lock (this._gate)
            {
                this._timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>
        /// Moves the clock forward, and fires the timers that fall due on the way, in the order of their time (the clock reads the time of
        /// a timer while its callback runs).
        /// </summary>
        public void Advance(TimeSpan time)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(time, TimeSpan.Zero);
            long target;
            lock (this._gate)
            {
                target = this._ticks + time.Ticks;
            }

            while (true)
            {
                FakeTimer? next;
                lock (this._gate)
                {
                    next = this._timers.Where(timer => timer.Due is { } due && due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
                    if (next is null)
                    {
                        this._ticks = target;
                        return;
                    }

                    this._ticks = Math.Max(this._ticks, next.Due!.Value);
                    next.Fired();
                }

                next.Callback(next.State);
            }
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly UnitClock _clock;
            private long _period;

            public FakeTimer(UnitClock clock, TimerCallback callback, object? state)
            {
                this._clock = clock;
                this.Callback = callback;
                this.State = state;
            }

            public TimerCallback Callback { get; }

            public object? State { get; }

            public long? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (this._clock._gate)
                {
                    if (!this._clock._timers.Contains(this))
                    {
                        return false;
                    }

                    this.Due = dueTime == Timeout.InfiniteTimeSpan ? null : this._clock._ticks + Math.Max(0, dueTime.Ticks);
                    this._period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                    return true;
                }
            }

            public void Fired()
            {
                this.Due = this._period > 0 ? this.Due + this._period : null;
            }

            public void Dispose()
            {
                lock (this._clock._gate)
                {
                    this.Due = null;
                    this._clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                this.Dispose();
                return default;
            }
        }
    }

    /// <summary>
    /// The messages of the discovery as a light sends them (the answer to a search, and the announcement that it makes by itself), made of
    /// their parts, for the tests that read them.
    /// </summary>
    internal static class UnitMessages
    {
        public const string Id = "0x000000000015243f";

        public const string Location = "yeelight://192.168.1.239:55443";

        public const string Support = "get_prop set_default set_power toggle set_bright start_cf stop_cf set_scene cron_add cron_get cron_del set_ct_abx set_rgb";

        /// <summary>The answer to a search (<c>HTTP/1.1 200 OK</c>), with the headers of the specification.</summary>
        public static string Response(
            string location = Location,
            string id = Id,
            string model = "color",
            string firmware = "18",
            string support = Support,
            string name = "my_bulb",
            string lineBreak = "\r\n")
        {
            return string.Join(
                lineBreak,
                "HTTP/1.1 200 OK",
                "Cache-Control: max-age=3600",
                "Date: ",
                "Ext: ",
                $"Location: {location}",
                "Server: POSIX UPnP/1.0 YGLC/1",
                $"id: {id}",
                $"model: {model}",
                $"fw_ver: {firmware}",
                $"support: {support}",
                "power: on",
                "bright: 100",
                "color_mode: 2",
                "ct: 4000",
                "rgb: 16711680",
                "hue: 100",
                "sat: 35",
                $"name: {name}",
                string.Empty);
        }

        /// <summary>The announcement that a light makes by itself (<c>NOTIFY * HTTP/1.1</c>), with the headers of the specification.</summary>
        public static string Advertisement(
            string location = Location,
            string id = Id,
            string model = "color",
            string firmware = "18",
            string support = Support,
            string name = "my_bulb",
            string lineBreak = "\r\n")
        {
            return string.Join(
                lineBreak,
                "NOTIFY * HTTP/1.1",
                "Host: 239.255.255.250:1982",
                "Cache-Control: max-age=3600",
                $"Location: {location}",
                "NTS: ssdp:alive",
                "Server: POSIX, UPnP/1.0 YGLC/1",
                $"id: {id}",
                $"model: {model}",
                $"fw_ver: {firmware}",
                $"support: {support}",
                "power: on",
                "bright: 100",
                "color_mode: 2",
                "ct: 4000",
                "rgb: 16711680",
                "hue: 100",
                "sat: 35",
                $"name: {name}",
                string.Empty);
        }

        /// <summary>Reads a message that has to be an announcement.</summary>
        public static DeviceAnnouncement Parse(string message)
        {
            Assert.True(DeviceAnnouncement.TryParse(message, out var announcement), "The message should be read as the announcement of a light.");
            return announcement!;
        }
    }

    /// <summary>
    /// Clients and devices that never touch the network: a device that a client takes over from an announcement does not connect (the client
    /// is made with <see cref="YeelightClientOptions.AutoConnect"/> off), so a test can look at what a device does with what it is told
    /// without a light, a socket or a thread. Never send a command to such a device: its address is not the loopback.
    /// </summary>
    internal static class UnitDevices
    {
        /// <summary>Makes a client that holds back from connecting to whatever it finds (and that is held to the loopback like every client of the tests).</summary>
        public static YeelightClient Client(TimeProvider? time = null, ILogger? logger = null, int maxDevices = 1000)
        {
            return TestClients.Create(new YeelightClientOptions { AutoConnect = false, MaxDevices = maxDevices }, logger, time: time);
        }

        /// <summary>Gives the client a light, as an announcement would.</summary>
        public static YeelightDevice Add(YeelightClient client, string? message = null)
        {
            Assert.False(client.Options.AutoConnect, "A device of a unit test must not connect to anything.");
            return client.Apply(UnitMessages.Parse(message ?? UnitMessages.Response())) ?? throw new InvalidOperationException("The client is disposed.");
        }

        /// <summary>
        /// Waits until the events that were raised for a light before this call have been delivered: the dispatcher of the client delivers
        /// them one at a time and in order, so what comes back after a notification that is posted now has been delivered too.
        /// </summary>
        public static async Task FlushAsync(YeelightLight light)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            light.Context.Dispatcher.Post(() => done.TrySetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// A TCP port on the loopback where nothing listens and nobody else can bind: a socket holds it without ever listening, so a connection to
    /// it is refused. (The port of <c>FreePort.Tcp()</c> was free a moment ago, and a test that runs in parallel can bind it before the connection
    /// that was meant for nobody is made.)
    /// </summary>
    internal sealed class DeadPort : IDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public DeadPort()
        {
            this._socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            this.Port = ((IPEndPoint)this._socket.LocalEndPoint!).Port;
        }

        /// <summary>Gets the port.</summary>
        public int Port { get; }

        public void Dispose()
        {
            this._socket.Dispose();
        }
    }

    /// <summary>
    /// What a light told the application: the names of the properties of <see cref="YeelightLight.PropertyChanged"/>, in order.
    /// </summary>
    internal sealed class PropertyLog
    {
        private readonly object _gate = new();
        private readonly List<string> _names = [];

        public PropertyLog(YeelightLight light)
        {
            light.PropertyChanged += (_, args) =>
            {
                lock (this._gate)
                {
                    this._names.Add(args.PropertyName ?? string.Empty);
                }
            };
        }

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._names];
                }
            }
        }
    }
}
