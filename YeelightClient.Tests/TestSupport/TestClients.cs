namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Net;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Makes the clients of the tests. A client that a test makes is always held to the loopback: whatever discovery it starts, it
    /// searches, announces and listens there and nowhere else, so that no test sends a packet to the real network (and so to the lights
    /// of a person who runs the tests: a light that somebody uses is not a test bench). Make every client with this, never with
    /// <c>new YeelightClient()</c>.
    /// </summary>
    internal static class TestClients
    {
        /// <summary>
        /// Makes a client whose discovery is held to the loopback.
        /// </summary>
        /// <param name="options">The settings. The port of the discovery is the port of <paramref name="searchTarget"/> when there is one.</param>
        /// <param name="logger">Where the client logs.</param>
        /// <param name="searchTarget">The simulated light that the multicast search is sent to (instead of the multicast group); without one the search goes to a port where nothing listens.</param>
        /// <param name="broadcastTarget">The simulated light that the broadcast search is sent to; without one no broadcast is sent.</param>
        /// <param name="time">The clock of the client.</param>
        /// <param name="adjust">Changes the settings of the discovery that the client starts (the interval of the look at the addresses of the computer, for one); what comes out is checked to keep to the loopback.</param>
        public static YeelightClient Create(
            YeelightClientOptions? options = null,
            ILogger? logger = null,
            SimulatedBulb? searchTarget = null,
            SimulatedBulb? broadcastTarget = null,
            TimeProvider? time = null,
            Func<YeelightDiscoveryOptions, YeelightDiscoveryOptions>? adjust = null)
        {
            var settings = options ?? new YeelightClientOptions();
            return new YeelightClient(
                settings,
                logger,
                discovery =>
                {
                    var held = discovery with
                    {
                        UseMulticast = false,
                        GetLocalAddresses = () => [IPAddress.Loopback],
                        SearchEndpoint = searchTarget?.DiscoveryEndPoint ?? new IPEndPoint(IPAddress.Loopback, 9),
                        UseBroadcast = broadcastTarget is not null,
                        BroadcastEndpoint = broadcastTarget?.DiscoveryEndPoint,
                        SearchBurstSpacing = TimeSpan.FromMilliseconds(50),
                        ListenAddress = IPAddress.Loopback,
                        ListenPort = 0,
                    };
                    return LoopbackDiscovery.Safe(adjust is null ? held : adjust(held));
                },
                time);
        }
    }
}
