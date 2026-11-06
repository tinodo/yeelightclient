namespace Yeelight
{
    using System;

    /// <summary>
    /// The settings of a <see cref="YeelightClient"/>.
    /// </summary>
    /// <remarks>
    /// The defaults suit most networks and most lights. The client reads the options once, when it is made: changing an options object
    /// afterwards changes nothing.
    /// </remarks>
    public sealed class YeelightClientOptions
    {
        /// <summary>
        /// Gets the default options.
        /// </summary>
        public static YeelightClientOptions Default { get; } = new();

        /// <summary>
        /// Gets whether the client connects to a light as soon as it is found. This is what keeps the state of the lights up to date (a
        /// light reports every change to the connections that it has). A light takes at most four connections
        /// (<see cref="YeelightLimits.ConnectionsPerLight"/>) and the apps of the same network need theirs too, so a client that only
        /// sends the odd command can set this to <c>false</c>: a light then connects when it gets a command, or when
        /// <see cref="YeelightDevice.ConnectAsync(System.Threading.CancellationToken)"/> is called. The default is <c>true</c>.
        /// </summary>
        public bool AutoConnect { get; init; } = true;

        /// <summary>
        /// Gets whether the client reads all the properties of a light each time that a connection to it is made, with one <c>get_prop</c>
        /// command. A light only tells what changes, so this is how the whole state is known. The default is <c>true</c>.
        /// </summary>
        public bool RefreshOnConnect { get; init; } = true;

        /// <summary>
        /// Gets how long a command waits for the answer of the light before it fails with <see cref="YeelightFailureKind.Timeout"/>.
        /// The default is 3 seconds.
        /// </summary>
        public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets how long the client waits for a connection to a light to be made before it gives up and tries again. The default is
        /// 3 seconds.
        /// </summary>
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets how long a connection may be quiet before the client asks the light something, to find out whether it is still there
        /// (a light that lost its power does not close its connection). <see cref="TimeSpan.Zero"/> turns it off. The default is
        /// 60 seconds.
        /// </summary>
        public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets the number of commands a minute that the client sends to a light on one connection. A light accepts 60
        /// (<see cref="YeelightLimits.CommandsPerMinute"/>) and answers <c>client quota exceeded</c> to more: the client holds a command
        /// back until it is its turn, and fails it with <see cref="YeelightFailureKind.RateLimited"/> when that would take more than
        /// two seconds. The default is 60.
        /// </summary>
        public int CommandsPerMinute { get; init; } = YeelightLimits.CommandsPerMinute;

        /// <summary>
        /// Gets whether a search is also sent as a broadcast, next to the multicast. Some lights never hear the multicast group (an access
        /// point or a router that does not pass it on to every WiFi client) and do answer the broadcast. The default is <c>true</c>.
        /// </summary>
        public bool UseBroadcast { get; init; } = true;

        /// <summary>
        /// Gets how many times a search is sent when it is due. UDP over WiFi loses packets, and a light that saves power can miss one.
        /// The default is 3, a second apart.
        /// </summary>
        public int SearchBurst { get; init; } = 3;

        /// <summary>
        /// Gets the UDP port that lights are searched on and that they announce themselves to. A light uses
        /// <see cref="YeelightLimits.DiscoveryPort"/> (1982); another port is for a simulator or a forwarded port.
        /// </summary>
        public int DiscoveryPort { get; init; } = YeelightLimits.DiscoveryPort;

        /// <summary>
        /// Gets the most lights that the client keeps. Anyone on the network can announce a light, so what the client keeps is bounded: once
        /// it has this many, the announcements of other lights are ignored (a warning is logged once). The default is 1000, which is more
        /// than any network has; a client for a home can set a lower one.
        /// </summary>
        public int MaxDevices { get; init; } = 1000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.CommandTimeout, TimeSpan.Zero, nameof(this.CommandTimeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(this.CommandTimeout, YeelightLimits.LongestTimer, nameof(this.CommandTimeout));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.ConnectTimeout, TimeSpan.Zero, nameof(this.ConnectTimeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(this.ConnectTimeout, YeelightLimits.LongestTimer, nameof(this.ConnectTimeout));
            ArgumentOutOfRangeException.ThrowIfLessThan(this.KeepAliveInterval, TimeSpan.Zero, nameof(this.KeepAliveInterval));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(this.KeepAliveInterval, YeelightLimits.LongestTimer, nameof(this.KeepAliveInterval));
            ArgumentOutOfRangeException.ThrowIfLessThan(this.CommandsPerMinute, 1, nameof(this.CommandsPerMinute));
            ArgumentOutOfRangeException.ThrowIfLessThan(this.SearchBurst, 1, nameof(this.SearchBurst));
            ArgumentOutOfRangeException.ThrowIfLessThan(this.DiscoveryPort, 1, nameof(this.DiscoveryPort));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(this.DiscoveryPort, 65535, nameof(this.DiscoveryPort));
            ArgumentOutOfRangeException.ThrowIfLessThan(this.MaxDevices, 1, nameof(this.MaxDevices));
        }
    }
}
