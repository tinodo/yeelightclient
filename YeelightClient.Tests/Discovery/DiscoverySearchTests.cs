namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The searching half of the discovery, on its own (no client): what is sent, to whom, how often, and what is done with the answers.
    /// Every search goes to a simulated light or to a responder on the loopback; the multicast group and the broadcast address of a network
    /// are never used.
    /// </summary>
    public class DiscoverySearchTests
    {
        private const string SearchText = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n";

        private static EventRecorder<DeviceAnnouncement> Record(YeelightDiscovery discovery)
        {
            var recorder = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += recorder.Add;
            return recorder;
        }

        private static TimeSpan[] SearchTimes(ScriptedResponder responder)
        {
            return [.. responder.Datagrams.Where(datagram => datagram.Text.StartsWith("M-SEARCH", StringComparison.Ordinal)).Select(datagram => datagram.At)];
        }

        [Fact]
        public async Task ASearch_FindsALight_AndTheAnnouncementSaysWhatTheLightAnswered()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x00000000AB12CD34", Model = "stripe", Name = "Desk" });
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: bulb.DiscoveryEndPoint));
            var found = Record(discovery);

            discovery.Start();
            await found.WaitForCountAsync(1, "the answer of the light");

            var announcement = found.Items[0];
            Assert.Equal("0x00000000AB12CD34", announcement.Id);
            Assert.Equal("stripe", announcement.Model);
            Assert.Equal(bulb.Location, announcement.Location);
            Assert.Equal("127.0.0.1", announcement.Host);
            Assert.Equal(bulb.ControlPort, announcement.Port);
            Assert.False(announcement.IsAdvertisement);
            Assert.Equal("Desk", announcement.Properties["name"]);
            Assert.Contains("set_music", announcement.Support);
        }

        [Fact]
        public async Task TheSearchMessage_IsExactlyWhatTheSpecificationSays_AndTheBroadcastIsTheSameMessage()
        {
            await using var multicast = new ScriptedResponder();
            await using var broadcast = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: multicast.EndPoint, broadcast: broadcast.EndPoint));

            discovery.Start();
            await Wait.UntilAsync(() => multicast.SearchCount >= 1 && broadcast.SearchCount >= 1, "the search to arrive both ways");

            Assert.Equal(SearchText, multicast.Datagrams[0].Text);
            Assert.Equal(SearchText, broadcast.Datagrams[0].Text);
        }

        [Fact]
        public async Task ASearch_IsAnsweredByALightThatFollowsTheSpecification()
        {
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: bulb.DiscoveryEndPoint));
            var found = Record(discovery);

            discovery.Start();
            await found.WaitForCountAsync(1, "the answer of the light");

            // The simulated light drops a search that does not follow the rules, as the real ones do: that it answers is the proof.
            Assert.Equal(SearchText, bulb.Searches[0]);
        }

        [Fact]
        public async Task TheFirstSearchIsSentAtOnce_AndTheNextOneWaitsForTheInterval()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromHours(1)));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the first search");

            await Wait.NotHappensAsync(() => responder.SearchCount > 1, "a second search long before the interval is over", TimeSpan.FromMilliseconds(600));
        }

        [Fact]
        public async Task TheSearchIsRepeatedAtTheInterval()
        {
            await using var responder = new ScriptedResponder();
            var interval = TimeSpan.FromMilliseconds(300);
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: interval));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 4, "four searches at an interval of 300 ms");

            var times = SearchTimes(responder);
            for (var i = 1; i < 4; i++)
            {
                Assert.InRange(times[i] - times[i - 1], TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [Fact]
        public async Task ABurst_SendsTheSearchThatManyTimes_ASpacingApart_AndThenStops()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, burst: 3, spacing: TimeSpan.FromMilliseconds(150)));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 3, "the three searches of the burst");

            var times = SearchTimes(responder);
            Assert.InRange(times[1] - times[0], TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
            Assert.InRange(times[2] - times[1], TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
            await Wait.NotHappensAsync(() => responder.SearchCount > 3, "a fourth search of the burst", TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public async Task ABurstGoesOutOnBothPaths()
        {
            await using var multicast = new ScriptedResponder();
            await using var broadcast = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: multicast.EndPoint, broadcast: broadcast.EndPoint, burst: 3));

            discovery.Start();
            await Wait.UntilAsync(() => multicast.SearchCount >= 3 && broadcast.SearchCount >= 3, "three searches on both paths");

            await Wait.NotHappensAsync(() => multicast.SearchCount > 3 || broadcast.SearchCount > 3, "more searches than the burst");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task ABurstThatIsNotPositive_StillSendsOneSearch(int burst)
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, burst: burst));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");

            await Wait.NotHappensAsync(() => responder.SearchCount > 1, "a second search");
        }

        [Fact]
        public async Task SearchNow_SendsASearchAtOnce_EveryTimeItIsCalledAfterTheLastOneWasSent()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, interval: TimeSpan.FromHours(1)));
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the first search");

            discovery.SearchNow();
            await Wait.UntilAsync(() => responder.SearchCount >= 2, "the search that was asked for");
            discovery.SearchNow();
            await Wait.UntilAsync(() => responder.SearchCount >= 3, "the second search that was asked for");

            await Wait.NotHappensAsync(() => responder.SearchCount > 3, "a search that nobody asked for");
        }

        [Fact]
        public async Task SearchNowCalledOften_WhileABurstGoesOut_IsOneMoreBurst()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, burst: 2, spacing: TimeSpan.FromMilliseconds(500), interval: TimeSpan.FromHours(1)));
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the first search of the first burst");

            for (var i = 0; i < 5; i++)
            {
                discovery.SearchNow();
            }

            await Wait.UntilAsync(() => responder.SearchCount >= 4, "the second burst");
            await Wait.NotHappensAsync(() => responder.SearchCount > 4, "a third burst for the calls that came together", TimeSpan.FromMilliseconds(800));
        }

        [Fact]
        public async Task SearchNowBeforeStart_DoesNotMakeTheFirstSearchTwo()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, burst: 2, interval: TimeSpan.FromHours(1)));

            discovery.SearchNow();
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 2, "the first burst");

            await Wait.NotHappensAsync(() => responder.SearchCount > 2, "a second burst for the call that came first", TimeSpan.FromMilliseconds(600));
        }

        [Fact]
        public async Task ADiscoveryThatOnlyListens_IgnoresSearchNow()
        {
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener to be there");

            discovery.SearchNow();
            discovery.SearchNow();

            Assert.Empty(discovery.SearchEndPoints);
            Assert.Equal(1, discovery.Generations);
        }

        [Fact]
        public async Task EveryAnswerIsReported_AndTheSameLightAnswersEverySearchOfABurst()
        {
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: bulb.DiscoveryEndPoint, burst: 3));
            var found = Record(discovery);

            discovery.Start();
            await found.WaitForCountAsync(3, "an answer to each of the three searches");

            await Wait.NotHappensAsync(() => found.Count > 3, "an answer that nobody asked for");
            Assert.All(found.Items, announcement => Assert.Equal(bulb.Id, announcement.Id));
        }

        [Fact]
        public async Task ALightThatDoesNotHearTheMulticast_ButAnswersTheBroadcast_IsFound()
        {
            await using var multicast = new ScriptedResponder();
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: multicast.EndPoint, broadcast: bulb.DiscoveryEndPoint));
            var found = Record(discovery);

            discovery.Start();
            await found.WaitForCountAsync(1, "the answer to the broadcast");

            Assert.Equal(bulb.Id, found.Items[0].Id);
            Assert.True(multicast.SearchCount >= 1, "The search is also sent to the multicast group: a light that hears it answers that.");
            Assert.Equal(SearchText, bulb.Searches[0]);
        }

        [Fact]
        public async Task NoBroadcastIsSent_WhenBroadcastIsOff_AndSuchALightIsNotFound()
        {
            await using var multicast = new ScriptedResponder();
            await using var bulb = new SimulatedBulb();
            var options = LoopbackDiscovery.Search(target: multicast.EndPoint, broadcast: bulb.DiscoveryEndPoint) with { UseBroadcast = false };
            await using var discovery = LoopbackDiscovery.Create(options);
            var found = Record(discovery);

            discovery.Start();
            await Wait.UntilAsync(() => multicast.SearchCount >= 1, "the search to arrive on the multicast path");

            await Wait.NotHappensAsync(() => bulb.SearchCount > 0 || found.Count > 0, "a broadcast, or the light that answers it", TimeSpan.FromMilliseconds(600));
        }

        [Fact]
        public async Task TwoLights_OneOnEachPath_AreBothFound()
        {
            await using var heardOnMulticast = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x000000000000000a" });
            await using var heardOnBroadcast = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x000000000000000b" });
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: heardOnMulticast.DiscoveryEndPoint, broadcast: heardOnBroadcast.DiscoveryEndPoint));
            var found = Record(discovery);

            discovery.Start();
            await found.WaitUntilAsync(items => items.Select(item => item.Id).Distinct().Count() == 2, "an answer from both lights");

            string[] expected = ["0x000000000000000a", "0x000000000000000b"];
            Assert.Equal(expected, found.Items.Select(item => item.Id).Distinct().Order(StringComparer.Ordinal).ToArray());
        }

        [FactIfLoopbackAlias]
        public async Task TheSearchGoesOutFromEveryAddressOfTheComputer()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, addresses: () => [IPAddress.Loopback, LoopbackAlias.Address]));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 2, "a search from both addresses");

            var sources = responder.Datagrams.Select(datagram => datagram.From.Address).Distinct().OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray();
            IPAddress[] expected = [IPAddress.Loopback, LoopbackAlias.Address];
            Assert.Equal(expected, sources);
            Assert.Equal(2, discovery.SearchEndPoints.Count);
        }

        [Fact]
        public async Task AnAddressThatCannotBeUsed_IsSkipped_AndTheOthersSearch()
        {
            await using var responder = new ScriptedResponder();

            // An address of the documentation range (RFC 5737): no network interface has it, so it cannot be bound, and nothing is sent from it.
            var unusable = IPAddress.Parse("203.0.113.77");
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, addresses: () => [unusable, IPAddress.Loopback]));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search from the address that works");

            var searcher = Assert.Single(discovery.SearchEndPoints);
            Assert.Equal(IPAddress.Loopback, searcher.Address);
            Assert.Equal(1, discovery.Generations);
        }

        [Fact]
        public async Task TheSameAddressTwice_IsOneSocket()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint, addresses: () => [IPAddress.Loopback, IPAddress.Loopback]));

            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");

            Assert.Single(discovery.SearchEndPoints);
            await Wait.NotHappensAsync(() => responder.SearchCount > 1, "a second search from the same address");
        }

        [Fact]
        public async Task ALateAnswer_ToTheSocketThatSentTheSearch_IsStillHeard()
        {
            await using var responder = new ScriptedResponder();
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1 && discovery.SearchEndPoints.Count == 1, "the search");

            // A light that is slow to answer (it was asleep) answers the search that was sent a while ago: to the end point that sent it.
            await bulb.SendDatagramAsync(discovery.SearchEndPoints[0], bulb.Message(advertisement: false));

            await found.WaitForCountAsync(1, "the late answer");
            Assert.Equal(bulb.Id, found.Items[0].Id);
        }

        [Fact]
        public async Task ADiscoveryThatWasNotStarted_DoesNothing()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));

            discovery.SearchNow();

            await Wait.NotHappensAsync(() => responder.Datagrams.Count > 0 || discovery.Generations > 0, "a search from a discovery that is not started", TimeSpan.FromMilliseconds(400));
            Assert.Empty(discovery.SearchEndPoints);
            Assert.Null(discovery.ListenEndPoint);
        }

        [Fact]
        public async Task TheDiscoveryDoesNotAnswerWhatItHears_ItOnlyAsks()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1 && discovery.SearchEndPoints.Count == 1, "the search");

            await responder.SendAsync(discovery.SearchEndPoints[0], Announcements.Message("0x1", "yeelight://127.0.0.1:55443", advertisement: false));
            await found.WaitForCountAsync(1, "the answer");

            await Wait.NotHappensAsync(() => responder.Datagrams.Count > 1, "something that is sent back to a light that answered", TimeSpan.FromMilliseconds(500));
        }
    }
}
