namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A computer that moves between networks (Wi-Fi comes and goes, a VPN connects, the computer wakes up) must not lose its lights: the
    /// discovery looks at the addresses of the computer and makes its sockets again when they changed. A test changes the answer of
    /// <see cref="YeelightDiscoveryOptions.GetLocalAddresses"/> under a running discovery.
    /// </summary>
    public class DiscoveryAddressChangeTests
    {
        private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(40);

        private static async Task SettleAsync() => await Task.Delay(200);

        private sealed class Addresses
        {
            private volatile IReadOnlyList<IPAddress> _current;
            private volatile bool _fails;
            private int _calls;

            public Addresses(params IPAddress[] initial)
            {
                this._current = initial;
            }

            public bool Fails
            {
                get => this._fails;
                set => this._fails = value;
            }

            public int Calls => Volatile.Read(ref this._calls);

            public void Set(params IPAddress[] addresses)
            {
                this._current = addresses;
            }

            public IReadOnlyList<IPAddress> Get()
            {
                Interlocked.Increment(ref this._calls);
                if (this._fails)
                {
                    throw new InvalidOperationException("The networks cannot be read right now.");
                }

                return this._current;
            }
        }

        [Fact]
        public async Task WhenTheAddressesChange_TheSocketsAreMadeAgain_AndTheSearchesGoOn()
        {
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromMilliseconds(100), addressRefresh: Refresh, addresses: addresses.Get));
            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1 && responder.SearchCount >= 2, "searches from the first sockets");

            addresses.Set();
            await Wait.UntilAsync(() => discovery.Generations == 2 && discovery.SearchEndPoints.Count == 0, "the sockets to be made again for a computer without a network");
            await SettleAsync();
            var whileOffline = responder.SearchCount;
            await Wait.NotHappensAsync(() => responder.SearchCount > whileOffline, "a search from a computer that has no address", TimeSpan.FromMilliseconds(500));

            addresses.Set(IPAddress.Loopback);
            await Wait.UntilAsync(() => discovery.Generations == 3 && discovery.SearchEndPoints.Count == 1, "the sockets to be made again when the network is back");
            await Wait.UntilAsync(() => responder.SearchCount >= whileOffline + 2, "searches from the new sockets");
        }

        [Fact]
        public async Task TheSameAddresses_RepeatedOrInAnotherOrder_DoNotMakeTheSocketsAgain()
        {
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, addressRefresh: Refresh, addresses: addresses.Get));
            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1, "the first sockets");
            var calls = addresses.Calls;

            addresses.Set(IPAddress.Loopback, IPAddress.Loopback);
            await Wait.UntilAsync(() => addresses.Calls >= calls + 5, "the addresses to be looked at a few times", TimeSpan.FromSeconds(5));

            Assert.Equal(1, discovery.Generations);
        }

        [FactIfLoopbackAlias]
        public async Task AnAddressThatAppears_GetsASearcherOfItsOwn_AndOneThatGoesIsLetGo()
        {
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromMilliseconds(100), addressRefresh: Refresh, addresses: addresses.Get));
            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1 && responder.SearchCount >= 1, "the first search");

            addresses.Set(IPAddress.Loopback, LoopbackAlias.Address);
            await Wait.UntilAsync(() => discovery.Generations == 2 && discovery.SearchEndPoints.Count == 2, "a searcher for each address");
            await Wait.UntilAsync(() => responder.Datagrams.Any(datagram => datagram.From.Address.Equals(LoopbackAlias.Address)), "a search from the new address");

            var oldEndPoints = discovery.SearchEndPoints.ToArray();
            addresses.Set(LoopbackAlias.Address);
            await Wait.UntilAsync(() => discovery.Generations == 3 && discovery.SearchEndPoints.Count == 1, "the searcher of the address that went");
            Assert.Equal(LoopbackAlias.Address, discovery.SearchEndPoints[0].Address);
            await Wait.UntilAsync(() => oldEndPoints.All(PortBlocker.IsFree), "the sockets of the earlier addresses to be closed");
        }

        [Fact]
        public async Task AnAddressListThatCannotBeRead_IsLogged_AndTheDiscoveryRecoversWhenItCan()
        {
            var logger = new ListLogger();
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromMilliseconds(100), addressRefresh: Refresh, addresses: addresses.Get), logger);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1 && responder.SearchCount >= 1, "the first search");

            addresses.Fails = true;
            await Wait.UntilAsync(() => discovery.Generations == 2 && discovery.SearchEndPoints.Count == 0, "the sockets to be made again, for no address");
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("not known", StringComparison.Ordinal));
            await SettleAsync();
            var before = responder.SearchCount;

            addresses.Fails = false;
            await Wait.UntilAsync(() => discovery.Generations == 3 && responder.SearchCount > before + 1, "the searches to go on when the addresses can be read again");
        }

        [Fact]
        public async Task AComputerWhoseAddressesCannotBeReadAtTheStart_IsFoundLater()
        {
            await using var bulb = new SimulatedBulb();
            var addresses = new Addresses(IPAddress.Loopback) { Fails = true };
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: bulb.DiscoveryEndPoint, interval: TimeSpan.FromMilliseconds(100), addressRefresh: Refresh, addresses: addresses.Get));
            var found = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += found.Add;

            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1, "the first sockets, for no address");
            Assert.Empty(discovery.SearchEndPoints);
            await Wait.NotHappensAsync(() => found.Count > 0 || bulb.SearchCount > 0, "a search from a computer without an address");

            addresses.Fails = false;
            await found.WaitForCountAsync(1, "the light, once the addresses can be read");
            Assert.Equal(bulb.Id, found.Items[0].Id);
        }

        [Fact]
        public async Task ADiscoveryThatListens_MakesItsListenerAgain_AndHearsAnnouncementsOnTheNewOne()
        {
            await using var bulb = new SimulatedBulb();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen(addresses.Get) with { AddressRefreshInterval = Refresh });
            var found = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += found.Add;
            discovery.Start();
            await Wait.UntilAsync(() => discovery.Generations == 1 && discovery.ListenEndPoint is not null, "the first listener");
            var first = discovery.ListenEndPoint!;

            addresses.Set();
            await Wait.UntilAsync(() => discovery.Generations == 2, "the listener to be made again");
            var second = discovery.ListenEndPoint!;
            await Repeat.UntilAsync(() => bulb.AnnounceAsync(discovery.ListenEndPoint!), () => found.Count >= 1, "an announcement on the new listener");

            Assert.Equal(bulb.Id, found.Items[0].Id);
            if (first.Port != second.Port)
            {
                await Wait.UntilAsync(() => PortBlocker.IsFree(first), "the first listener to be closed");
            }
        }

        [Fact]
        public async Task TheAddressesAreLookedAtAtTheInterval_NotMoreOften()
        {
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, addressRefresh: TimeSpan.FromMilliseconds(250), addresses: addresses.Get));

            discovery.Start();
            await Task.Delay(1100);

            Assert.InRange(addresses.Calls, 3, 10);
        }

        [Fact]
        public async Task ANetworkThatComesAndGoesManyTimes_DoesNotEndTheDiscovery_OrLeakSockets()
        {
            await using var responder = new ScriptedResponder();
            var addresses = new Addresses(IPAddress.Loopback);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromMilliseconds(50), addressRefresh: TimeSpan.FromMilliseconds(10), addresses: addresses.Get));
            var seen = new HashSet<IPEndPoint>();
            discovery.Start();

            for (var i = 0; i < 60; i++)
            {
                addresses.Set(i % 2 == 0 ? [] : [IPAddress.Loopback]);
                foreach (var endPoint in discovery.SearchEndPoints)
                {
                    seen.Add(endPoint);
                }

                await Task.Delay(15);
            }

            addresses.Set(IPAddress.Loopback);
            await Wait.UntilAsync(() => discovery.SearchEndPoints.Count == 1, "the searcher of the network that stayed");
            var before = responder.SearchCount;
            await Wait.UntilAsync(() => responder.SearchCount >= before + 2, "searches after the network settled");
            Assert.True(discovery.Generations > 2, $"The sockets were made {discovery.Generations} times: the changes of the network were not followed.");

            var last = discovery.SearchEndPoints.ToArray();
            await discovery.DisposeAsync();

            await Wait.UntilAsync(() => seen.Concat(last).All(PortBlocker.IsFree), "every socket that the discovery made to be closed");
        }
    }
}
