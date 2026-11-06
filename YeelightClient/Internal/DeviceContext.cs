namespace Yeelight
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// What the lights of one client share: its settings, its log, the dispatcher of its events and its clock.
    /// </summary>
    internal sealed class DeviceContext
    {
        public DeviceContext(YeelightClientOptions options, ILogger logger, EventDispatcher dispatcher, TimeProvider time, Action<YeelightDevice, YeelightLight, string> stateChanged)
        {
            this.Options = options;
            this.Logger = logger;
            this.Dispatcher = dispatcher;
            this.Time = time;
            this.StateChanged = stateChanged;
            this.Connection = new YeelightConnectionOptions
            {
                ConnectTimeout = options.ConnectTimeout,
                CommandTimeout = options.CommandTimeout,
                KeepAliveInterval = options.KeepAliveInterval,
                CommandsPerMinute = options.CommandsPerMinute,
                Time = time,
            };
        }

        public YeelightClientOptions Options { get; }

        public ILogger Logger { get; }

        public EventDispatcher Dispatcher { get; }

        public TimeProvider Time { get; }

        /// <summary>The times and limits of the connection to a light.</summary>
        public YeelightConnectionOptions Connection { get; }

        /// <summary>Tells the client that a property of a light changed (it raises <see cref="YeelightClient.DeviceStateChanged"/>).</summary>
        public Action<YeelightDevice, YeelightLight, string> StateChanged { get; }
    }
}
