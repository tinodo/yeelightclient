namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The parts of the discovery that look at the networks of this computer: from which addresses a search goes out, and where its broadcast
    /// goes. Nothing here sends anything: the networks are read, a discovery that computes where a broadcast would go is never started, one
    /// that is started has nothing to search or listen for (or is held to the loopback like every discovery of the tests), and an address
    /// that no computer has (TEST-NET-3, which is kept for documentation, RFC 5737) takes the place of a real one where a socket would be
    /// made, so that making it fails on the spot.
    /// </summary>
    public class DiscoveryNetworkTests
    {
        private static readonly IPAddress NobodysAddress = IPAddress.Parse("203.0.113.77");

        private static readonly MethodInfo FindBroadcast = typeof(YeelightDiscovery).GetMethod("FindBroadcastAddress", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("YeelightDiscovery has no method 'FindBroadcastAddress' any more: update this test.");

        private static readonly MethodInfo BroadcastTargets = typeof(YeelightDiscovery).GetMethod("GetBroadcastTargets", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("YeelightDiscovery has no method 'GetBroadcastTargets' any more: update this test.");

        private static IPAddress? BroadcastOf(IPAddress address) => (IPAddress?)FindBroadcast.Invoke(null, [address]);

        private static IReadOnlyList<IPEndPoint> TargetsOf(YeelightDiscoveryOptions options, IPAddress address)
        {
            using var discovery = new YeelightDiscovery(options);
            return (IReadOnlyList<IPEndPoint>)BroadcastTargets.Invoke(discovery, [address])!;
        }

        private static string[] AddressesForSearching()
        {
            var addresses = new List<string>();
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || !network.SupportsMulticast || network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                addresses.AddRange(network.GetIPProperties().UnicastAddresses
                    .Select(unicast => unicast.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && address.GetAddressBytes() is not [169, 254, ..])
                    .Select(address => address.ToString()));
            }

            return [.. addresses.Order(StringComparer.Ordinal)];
        }

        // No computer has this address (see above): a test that uses it as one has nothing to do when this one does, which is not going to happen.
        private static bool NobodysAddressIsTaken => ThisComputerHas(NobodysAddress);

        private static bool ThisComputerHas(IPAddress address)
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(network => network.GetIPProperties().UnicastAddresses.Any(unicast => unicast.Address.Equals(address)));
        }

        [Fact]
        public void TheAddressesToSearchFrom_AreTheIpv4AddressesOfTheNetworksThatAreUp_AndNoOther()
        {
            var before = AddressesForSearching();

            var found = YeelightDiscovery.GetNetworkAddresses().Select(address => address.ToString()).Order(StringComparer.Ordinal).ToArray();

            var after = AddressesForSearching();
            Assert.True(found.SequenceEqual(before) || found.SequenceEqual(after), $"The addresses are [{string.Join(", ", found)}], and the networks have [{string.Join(", ", before)}].");
            Assert.All(YeelightDiscovery.GetNetworkAddresses(), address =>
            {
                Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
                Assert.False(IPAddress.IsLoopback(address));
                Assert.False(address.GetAddressBytes() is [169, 254, ..], $"{address} is a link-local address, which no search goes out from.");
            });
        }

        [Fact]
        public void EveryAddressOfThisComputer_HasTheBroadcastAddressOfItsOwnNetwork()
        {
            var checkedAddresses = 0;
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var unicast in network.GetIPProperties().UnicastAddresses.Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    var expected = YeelightDiscovery.BroadcastAddress(unicast.Address, unicast.IPv4Mask);

                    Assert.Equal(expected, BroadcastOf(unicast.Address));
                    checkedAddresses++;
                }
            }

            Assert.True(checkedAddresses > 0 || NetworkInterface.GetAllNetworkInterfaces().Length == 0, "This computer has no address at all, not even the loopback.");
        }

        [Fact]
        public void AnAddressThatNoNetworkHas_HasNoBroadcastAddress()
        {
            if (NobodysAddressIsTaken)
            {
                return;
            }

            Assert.Null(BroadcastOf(NobodysAddress));
        }

        [Fact]
        public void WithoutABroadcast_ThereIsNoTargetForIt()
        {
            var targets = TargetsOf(new YeelightDiscoveryOptions { UseBroadcast = false }, IPAddress.Loopback);

            Assert.Empty(targets);
        }

        [Fact]
        public void ABroadcastEndpointThatIsGiven_IsTheOnlyTarget_WhateverTheAddress()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 4321);

            var targets = TargetsOf(new YeelightDiscoveryOptions { BroadcastEndpoint = endpoint }, NobodysAddress);

            Assert.Equal([endpoint], targets);
        }

        [Fact]
        public void TheBroadcastTargets_AreTheBroadcastAddressOfTheNetwork_AndTheOneForEveryone_OnThePortOfTheDiscovery()
        {
            var network = BroadcastOf(IPAddress.Loopback);

            var targets = TargetsOf(new YeelightDiscoveryOptions { Port = 4321 }, IPAddress.Loopback);

            var everyone = new IPEndPoint(IPAddress.Broadcast, 4321);
            var expected = network is null || network.Equals(IPAddress.Broadcast) ? new[] { everyone } : [new IPEndPoint(network, 4321), everyone];
            Assert.Equal(expected, targets);
        }

        [Fact]
        public void ANetworkThatIsNotKnown_IsBroadcastToEveryoneAlone()
        {
            if (NobodysAddressIsTaken)
            {
                return;
            }

            var targets = TargetsOf(new YeelightDiscoveryOptions(), NobodysAddress);

            Assert.Equal([new IPEndPoint(IPAddress.Broadcast, YeelightDiscovery.DefaultPort)], targets);
        }

        [Fact]
        public async Task ADiscoveryThatHasNothingToSearchOrToListen_LooksAtTheNetworksOfThisComputer_AndMakesNoSocket()
        {
            // The addresses are those of the networks that this computer is on (that is what is looked at), and with nothing to search and
            // nothing to listen for no socket is made, so that nothing can be sent.
            await using var discovery = new YeelightDiscovery(new YeelightDiscoveryOptions { Search = false, Listen = false });

            discovery.Start();

            await Wait.UntilAsync(() => discovery.Generations == 1, "the first look at the networks");
            Assert.Empty(discovery.SearchEndPoints);
            Assert.Null(discovery.ListenEndPoint);
        }

        [Fact]
        public async Task AnAddressThatNoComputerHas_GetsNoSearcher_AndTheLogSaysSo()
        {
            if (NobodysAddressIsTaken)
            {
                return;
            }

            var logger = new ListLogger();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(spacing: TimeSpan.FromMilliseconds(10), addresses: () => [NobodysAddress]), logger);

            discovery.Start();

            await Wait.UntilAsync(() => discovery.Generations == 1, "the first generation of sockets");
            Assert.Empty(discovery.SearchEndPoints);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("No search from 203.0.113.77", StringComparison.Ordinal));
        }
    }
}
