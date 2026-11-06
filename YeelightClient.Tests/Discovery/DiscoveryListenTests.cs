namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The listening half of the discovery: the announcements that the lights send by themselves. The listener of a test is on the
    /// loopback, on a port that the system chooses, and the multicast group is not joined: the lights of the tests announce to that port.
    /// </summary>
    public class DiscoveryListenTests
    {
        private static EventRecorder<DeviceAnnouncement> Record(YeelightDiscovery discovery)
        {
            var recorder = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += recorder.Add;
            return recorder;
        }

        private static async Task<IPEndPoint> StartedAsync(YeelightDiscovery discovery)
        {
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener to be there");
            return discovery.ListenEndPoint!;
        }

        [Fact]
        public async Task AnAnnouncement_IsTaken_AndSaysThatTheLightSentItByItself()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x00000000000000c1", Name = "Hall" });
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);

            await bulb.AnnounceAsync(listener);
            await found.WaitForCountAsync(1, "the announcement");

            var announcement = found.Items[0];
            Assert.Equal("0x00000000000000c1", announcement.Id);
            Assert.True(announcement.IsAdvertisement);
            Assert.Equal(bulb.Location, announcement.Location);
            Assert.Equal("Hall", announcement.Properties["name"]);
        }

        [Fact]
        public async Task ALightThatChangesAndAnnouncesAgain_IsReportedEveryTime()
        {
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);

            await bulb.AnnounceAsync(listener);
            await bulb.ChangeAsync(("bright", "42"));
            await bulb.AnnounceAsync(listener);

            await found.WaitForCountAsync(2, "both announcements");
            Assert.Equal("100", found.Items[0].Properties["bright"]);
            Assert.Equal("42", found.Items[1].Properties["bright"]);
        }

        [Fact]
        public async Task ManyLightsAnnouncing_AreAllTaken()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);

            for (var i = 0; i < 40; i++)
            {
                await responder.SendAsync(listener, Announcements.Message($"0x{i:x16}", "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color")));
                await Wait.UntilAsync(() => found.Count > i, $"announcement number {i}");
            }

            Assert.Equal(40, found.Items.Select(item => item.Id).Distinct().Count());
        }

        [Fact]
        public async Task TheListener_NeverAnswersWhatItHears()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);

            await responder.SendAsync(listener, Announcements.Message("0x1", "yeelight://127.0.0.1:55443", advertisement: true));
            await responder.SendAsync(listener, "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n");
            await found.WaitForCountAsync(1, "the announcement");

            await Wait.NotHappensAsync(() => responder.Datagrams.Count > 0, "a reply to a datagram that was not asked for", TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public async Task AnAnnouncementThatSaysTheLightIsGoingAway_IsDropped()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);
            var goodbye = Announcements.Message("0xBYE", "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color")).Replace("NTS: ssdp:alive", "NTS: ssdp:byebye", StringComparison.Ordinal);

            await responder.SendAsync(listener, goodbye);
            await responder.SendAsync(listener, Announcements.Message("0xHELLO", "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color")));

            await found.WaitForCountAsync(1, "the announcement that follows");
            Assert.Equal("0xHELLO", Assert.Single(found.Items).Id);
        }

        [Fact]
        public async Task AnAnnouncementThatNamesAnotherHost_IsDropped_AndTheNextOneIsTaken()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            var listener = await StartedAsync(discovery);

            await responder.SendAsync(listener, Announcements.Message("0xEVIL", "yeelight://127.0.0.2:22", advertisement: true, ("model", "color")));
            await responder.SendAsync(listener, Announcements.Message("0xGOOD", "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color")));

            await found.WaitForCountAsync(1, "the announcement that is right");
            Assert.Equal("0xGOOD", Assert.Single(found.Items).Id);
        }

        [Fact]
        public async Task ListeningAndSearching_InOneDiscovery_BothWork()
        {
            await using var bulb = new SimulatedBulb();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: bulb.DiscoveryEndPoint) with { Listen = true, ListenPort = 0 });
            var found = Record(discovery);

            var listener = await StartedAsync(discovery);
            await found.WaitForCountAsync(1, "the answer to the search");
            await bulb.AnnounceAsync(listener);
            await found.WaitForCountAsync(2, "the announcement");

            Assert.False(found.Items[0].IsAdvertisement);
            Assert.True(found.Items[1].IsAdvertisement);
            Assert.Single(discovery.SearchEndPoints);
            Assert.NotEqual(listener.Port, discovery.SearchEndPoints[0].Port);
        }

        [Fact]
        public async Task AHandlerThatThrows_DoesNotStopTheListener()
        {
            await using var responder = new ScriptedResponder();
            var logger = new ListLogger();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen(), logger);
            var calls = new HitCounter();
            discovery.Announced += _ =>
            {
                if (calls.Increment() == 1)
                {
                    throw new InvalidOperationException("The handler fails.");
                }
            };
            var listener = await StartedAsync(discovery);

            await responder.SendAsync(listener, Announcements.Message("0x1", "yeelight://127.0.0.1:55443", advertisement: true));
            await Wait.UntilAsync(() => calls.Value >= 1, "the first announcement");
            await responder.SendAsync(listener, Announcements.Message("0x2", "yeelight://127.0.0.1:55443", advertisement: true));

            await Wait.UntilAsync(() => calls.Value >= 2, "the announcement after the one that made the handler fail");
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("could not be handled", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheListenSocketIsReleased_WhenTheDiscoveryIsDisposed()
        {
            var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var listener = await StartedAsync(discovery);
            Assert.False(PortBlocker.IsFree(listener), "The listener should hold its port while it runs.");

            await discovery.DisposeAsync();

            await Wait.UntilAsync(() => PortBlocker.IsFree(listener), "the port of the listener to be free again");
        }

        [Fact]
        public async Task TwoListeners_CanShareAPort_AsProgramsThatListenToTheLightsDo()
        {
            await using var first = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var shared = await StartedAsync(first);
            await using var second = LoopbackDiscovery.Create(LoopbackDiscovery.Listen(port: shared.Port));

            var other = await StartedAsync(second);

            Assert.Equal(shared.Port, other.Port);
            Assert.Equal(1, second.Generations);
        }

        [Fact]
        public async Task ADiscoveryThatCannotMakeItsSockets_KeepsTrying_AndStopsAtOnceWhenItIsDisposed()
        {
            var logger = new ListLogger();

            // 70000 is not a port: the sockets cannot be made, however often it is tried.
            var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen(port: 70000), logger);
            discovery.Start();

            await Wait.UntilAsync(() => logger.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("The discovery failed", StringComparison.Ordinal)), "the failure to be logged");
            Assert.Equal(0, discovery.Generations);
            Assert.Null(discovery.ListenEndPoint);

            var stopping = discovery.DisposeAsync().AsTask();
            await stopping.WithTimeoutAsync("the discovery to stop while it waits to try again", TimeSpan.FromSeconds(1.5));
        }

        [Fact]
        public async Task ABusyListenPort_IsTriedAgain_AndTheDiscoveryListensWhenItIsFree()
        {
            var logger = new ListLogger();
            using var blocker = PortBlocker.Hold();
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen(port: blocker.EndPoint.Port), logger);
            var found = Record(discovery);

            discovery.Start();
            await Wait.UntilAsync(() => logger.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("The discovery failed", StringComparison.Ordinal)), "the failure to be logged");
            Assert.Equal(0, discovery.Generations);
            blocker.Dispose();

            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener to be made when the port is free", TimeSpan.FromSeconds(10));
            await responder.SendAsync(discovery.ListenEndPoint!, Announcements.Message("0x1", "yeelight://127.0.0.1:55443", advertisement: true));
            await found.WaitForCountAsync(1, "the announcement on the port that was busy");
        }
    }
}
