namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Makes the discovery of the tests that build a <see cref="YeelightDiscovery"/> themselves (and not through a client): held to the
    /// loopback, like the clients that <see cref="TestClients"/> makes. A search and a broadcast go to a simulated light or to a port where
    /// nothing listens, the multicast group is never joined or used, and the sockets are on the loopback address. A discovery that is
    /// made any other way is refused: a light that somebody uses is not a test bench.
    /// </summary>
    internal static class LoopbackDiscovery
    {
        /// <summary>A port where nothing listens (the discard port): where a search goes that nobody is meant to answer.</summary>
        public static readonly IPEndPoint DeadEndPoint = new(IPAddress.Loopback, 9);

        /// <summary>
        /// Makes the options of a discovery that searches.
        /// </summary>
        /// <param name="target">Where the search is sent, instead of the multicast group.</param>
        /// <param name="broadcast">Where the search is sent as a broadcast, when it is (nothing is broadcast without this).</param>
        /// <param name="burst">How many times a search is sent when it is due.</param>
        /// <param name="interval">The time between two searches.</param>
        /// <param name="spacing">The time between the searches of a burst.</param>
        /// <param name="addressRefresh">How often the addresses of the computer are looked at.</param>
        /// <param name="addresses">The addresses of this computer to search from.</param>
        public static YeelightDiscoveryOptions Search(
            IPEndPoint? target = null,
            IPEndPoint? broadcast = null,
            int burst = 1,
            TimeSpan? interval = null,
            TimeSpan? spacing = null,
            TimeSpan? addressRefresh = null,
            Func<IReadOnlyList<IPAddress>>? addresses = null)
        {
            return Safe(new YeelightDiscoveryOptions
            {
                Search = true,
                UseMulticast = false,
                Port = DeadEndPoint.Port,
                SearchEndpoint = target ?? DeadEndPoint,
                UseBroadcast = broadcast is not null,
                BroadcastEndpoint = broadcast,
                SearchBurst = burst,
                SearchBurstSpacing = spacing ?? TimeSpan.FromMilliseconds(20),
                SearchInterval = interval ?? TimeSpan.FromHours(1),
                AddressRefreshInterval = addressRefresh ?? TimeSpan.FromSeconds(15),
                ListenAddress = IPAddress.Loopback,
                GetLocalAddresses = addresses ?? (() => [IPAddress.Loopback]),
            });
        }

        /// <summary>
        /// Makes the options of a discovery that listens to announcements, on a port that the system chooses.
        /// </summary>
        public static YeelightDiscoveryOptions Listen(Func<IReadOnlyList<IPAddress>>? addresses = null, int port = 0)
        {
            return Safe(new YeelightDiscoveryOptions
            {
                Listen = true,
                UseMulticast = false,
                UseBroadcast = false,
                Port = DeadEndPoint.Port,
                ListenAddress = IPAddress.Loopback,
                ListenPort = port,
                GetLocalAddresses = addresses ?? (() => [IPAddress.Loopback]),
            });
        }

        /// <summary>Makes a discovery of options that were checked.</summary>
        public static YeelightDiscovery Create(YeelightDiscoveryOptions options, ILogger? logger = null)
        {
            return new YeelightDiscovery(Safe(options), logger);
        }

        /// <summary>
        /// Makes a discovery that is only used to probe a light, which sends to the address that it is given and nowhere else.
        /// </summary>
        /// <param name="probePort">The UDP port that the light answers on.</param>
        /// <param name="spacing">The time between the searches that a probe sends while it waits.</param>
        public static YeelightDiscovery ForProbing(int probePort, TimeSpan? spacing = null, ILogger? logger = null)
        {
            var options = new YeelightDiscoveryOptions
            {
                UseMulticast = false,
                UseBroadcast = false,
                Port = DeadEndPoint.Port,
                ProbePort = probePort,
                SearchBurstSpacing = spacing ?? TimeSpan.FromMilliseconds(50),
                GetLocalAddresses = () => [IPAddress.Loopback],
            };
            return new YeelightDiscovery(Safe(options), logger);
        }

        /// <summary>
        /// Checks that options keep to the loopback.
        /// </summary>
        /// <exception cref="InvalidOperationException">The options would send or listen somewhere else.</exception>
        public static YeelightDiscoveryOptions Safe(YeelightDiscoveryOptions options)
        {
            if (options.UseMulticast)
            {
                throw new InvalidOperationException("A test must not use the multicast group.");
            }

            if (options.GetLocalAddresses is null)
            {
                throw new InvalidOperationException("A test must say which addresses it searches from: those of the networks that the computer is on are not for tests.");
            }

            if (options.Search && (options.SearchEndpoint is not { } search || !IPAddress.IsLoopback(search.Address)))
            {
                throw new InvalidOperationException("A test must send its search to the loopback.");
            }

            if (options.UseBroadcast && (options.BroadcastEndpoint is not { } broadcast || !IPAddress.IsLoopback(broadcast.Address)))
            {
                throw new InvalidOperationException("A test must send its broadcast to the loopback, and never to the broadcast address of a network.");
            }

            if (options.Listen && !IPAddress.IsLoopback(options.ListenAddress))
            {
                throw new InvalidOperationException("A test must listen on the loopback.");
            }

            return options;
        }
    }
}
