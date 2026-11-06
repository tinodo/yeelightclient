namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// Asking one address whether a light is there, for networks that do not pass multicast: the search goes straight to the address, is sent
    /// again while the probe waits, and only an answer from that address (naming that address) counts.
    /// </summary>
    public class DiscoveryProbeTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private static string Answer(string id, string host = "127.0.0.1") => Announcements.Message(id, $"yeelight://{host}:55443", advertisement: false, ("model", "color"), ("support", Announcements.Support));

        [Fact]
        public async Task AProbe_FindsTheLightAtAnAddress_AndSaysWhatItAnswered()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x00000000000000f1", Model = "ceiling" });
            using var discovery = LoopbackDiscovery.ForProbing(bulb.DiscoveryPort);

            var announcement = await discovery.ProbeAsync(IPAddress.Loopback, Patience);

            Assert.NotNull(announcement);
            Assert.Equal("0x00000000000000f1", announcement.Id);
            Assert.Equal("ceiling", announcement.Model);
            Assert.Equal(bulb.Location, announcement.Location);
            Assert.False(announcement.IsAdvertisement);
        }

        [Fact]
        public async Task AProbe_ThatIsNotAnswered_GivesNull_WhenTheTimeIsUp()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            using var discovery = LoopbackDiscovery.ForProbing(bulb.DiscoveryPort);
            var watch = Stopwatch.StartNew();

            var announcement = await discovery.ProbeAsync(IPAddress.Loopback, TimeSpan.FromMilliseconds(400));

            Assert.Null(announcement);
            Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5));
            Assert.True(bulb.SearchCount >= 1);
        }

        [Fact]
        public async Task AProbe_SendsTheSearchAgainWhileItWaits_FromTheSameSocket()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port, TimeSpan.FromMilliseconds(50));

            var announcement = await discovery.ProbeAsync(IPAddress.Loopback, TimeSpan.FromMilliseconds(600));

            Assert.Null(announcement);
            Assert.True(responder.SearchCount >= 4, $"The search was sent {responder.SearchCount} times in 600 ms at an interval of 50 ms.");
            Assert.Single(responder.Datagrams.Select(datagram => datagram.From).Distinct());
        }

        [Fact]
        public async Task AProbe_TakesTheAnswerThatComesLate_AndStopsSearching()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            using var discovery = LoopbackDiscovery.ForProbing(bulb.DiscoveryPort, TimeSpan.FromMilliseconds(50));

            var probing = discovery.ProbeAsync(IPAddress.Loopback, Patience);
            await Wait.UntilAsync(() => bulb.SearchCount >= 3, "the search to be sent three times");
            Assert.False(probing.IsCompleted);
            bulb.AnswersSearches = true;

            var announcement = await probing.WithTimeoutAsync("the probe to take the late answer");
            Assert.Equal(bulb.Id, announcement?.Id);
            var sent = bulb.SearchCount;
            await Wait.NotHappensAsync(() => bulb.SearchCount > sent + 1, "searches after the probe is over");
        }

        [Fact]
        public async Task AProbe_IgnoresAnAnswerThatNamesAnotherAddress_AndTakesTheRightOneThatComesLater()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port);
            var probing = discovery.ProbeAsync(IPAddress.Loopback, Patience);
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var prober = responder.Datagrams[0].From;

            await responder.SendAsync(prober, Answer("0xEVIL", "127.0.0.2"));
            await Task.Delay(300);
            Assert.False(probing.IsCompleted, "An answer that names another address is not the answer of the light that was asked.");
            await responder.SendAsync(prober, Answer("0xGOOD"));

            var announcement = await probing.WithTimeoutAsync("the probe to take the right answer");
            Assert.Equal("0xGOOD", announcement?.Id);
        }

        [FactIfLoopbackAlias]
        public async Task AProbe_IgnoresAnAnswerFromAnotherAddress_EvenWhenItNamesThatAddressItself()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port);
            var probing = discovery.ProbeAsync(IPAddress.Loopback, Patience);
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var prober = responder.Datagrams[0].From;

            // A neighbour that heard the search (or guessed the port) answers for itself, and then pretends to be the light that was asked.
            await Announcements.SendFromAsync(LoopbackAlias.Address, prober, Encoding.UTF8.GetBytes(Answer("0xNEIGHBOUR", "127.0.0.2")));
            await Announcements.SendFromAsync(LoopbackAlias.Address, prober, Encoding.UTF8.GetBytes(Answer("0xPRETENDER", "127.0.0.1")));
            await Task.Delay(300);
            Assert.False(probing.IsCompleted, "Only an answer from the address that was asked counts.");
            await responder.SendAsync(prober, Answer("0xGOOD"));

            var announcement = await probing.WithTimeoutAsync("the probe to take the right answer");
            Assert.Equal("0xGOOD", announcement?.Id);
        }

        [Fact]
        public async Task AProbe_SkipsJunkThatComesFirst()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port);
            var probing = discovery.ProbeAsync(IPAddress.Loopback, Patience);
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var prober = responder.Datagrams[0].From;

            await responder.SendAsync(prober, Array.Empty<byte>());
            await responder.SendAsync(prober, "HTTP/1.1 404 Not Found\r\n\r\n");
            await responder.SendAsync(prober, new byte[3000]);
            await responder.SendAsync(prober, "{\"id\":1,\"result\":[\"ok\"]}\r\n");
            await responder.SendAsync(prober, Answer("0xGOOD"));

            var announcement = await probing.WithTimeoutAsync("the probe to take the right answer");
            Assert.Equal("0xGOOD", announcement?.Id);
        }

        [Fact]
        public async Task AProbe_RefusesAnAddressThatIsNotOnTheLocalNetwork_BeforeItSendsAnything()
        {
            using var discovery = LoopbackDiscovery.ForProbing(LoopbackDiscovery.DeadEndPoint.Port);

            // The addresses are of the ranges that are set apart for documentation (RFC 5737, RFC 3849): nothing is ever there.
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Parse("203.0.113.1"), TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Parse("198.51.100.1"), TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Parse("192.0.2.1"), TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Parse("2001:db8::1"), TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.IPv6Loopback, TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Any, TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => discovery.ProbeAsync(IPAddress.Broadcast, TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ArgumentNullException>(() => discovery.ProbeAsync(null!, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task AProbe_IsCancelledAtOnce_AndSendsNothingMore()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port, TimeSpan.FromMilliseconds(50));
            using var cancel = new CancellationTokenSource();
            var probing = discovery.ProbeAsync(IPAddress.Loopback, Patience, cancel.Token);
            await Wait.UntilAsync(() => responder.SearchCount >= 2, "the search to be sent again");

            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing.WithTimeoutAsync("the probe to end", TimeSpan.FromSeconds(2)));
            await Task.Delay(150);
            var sent = responder.SearchCount;
            await Wait.NotHappensAsync(() => responder.SearchCount > sent, "searches after the cancel");
        }

        [Fact]
        public async Task AProbe_ThatIsAlreadyCancelled_EndsAtOnce()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.ProbeAsync(IPAddress.Loopback, Patience, cancel.Token).WithTimeoutAsync("the probe to end", TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public async Task AProbe_ToAPortWhereNothingListens_GivesNull_WithinTheTime()
        {
            using var discovery = LoopbackDiscovery.ForProbing(FreePort.Udp(), TimeSpan.FromMilliseconds(50));
            var watch = Stopwatch.StartNew();

            var announcement = await discovery.ProbeAsync(IPAddress.Loopback, TimeSpan.FromMilliseconds(600));

            // Where the system tells that the port is closed (an ICMP error) the probe ends at once; elsewhere it waits for the time to pass.
            Assert.Null(announcement);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task TheSocketOfAProbe_IsClosedWhenTheProbeIsOver()
        {
            await using var responder = new ScriptedResponder();
            using var discovery = LoopbackDiscovery.ForProbing(responder.Port);

            await discovery.ProbeAsync(IPAddress.Loopback, TimeSpan.FromMilliseconds(250));

            var socket = responder.Datagrams[0].From;
            await Wait.UntilAsync(() => PortBlocker.IsFree(socket), "the socket of the probe to be closed");
        }

        [Fact]
        public async Task ManyProbesAtOnce_EachGetTheAnswerForThem()
        {
            await using var bulb = new SimulatedBulb();
            using var discovery = LoopbackDiscovery.ForProbing(bulb.DiscoveryPort);

            var probes = Enumerable.Range(0, 30).Select(_ => discovery.ProbeAsync(IPAddress.Loopback, Patience)).ToArray();
            var answers = await Task.WhenAll(probes).WithTimeoutAsync("the probes to end");

            Assert.All(answers, announcement => Assert.Equal(bulb.Id, announcement?.Id));
        }

        [Fact]
        public async Task TwoProbes_ForTwoLights_DoNotMixUpTheirAnswers()
        {
            await using var first = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000001" });
            await using var second = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000002" });
            using var askFirst = LoopbackDiscovery.ForProbing(first.DiscoveryPort);
            using var askSecond = LoopbackDiscovery.ForProbing(second.DiscoveryPort);

            var one = askFirst.ProbeAsync(IPAddress.Loopback, Patience);
            var two = askSecond.ProbeAsync(IPAddress.Loopback, Patience);

            Assert.Equal("0x0000000000000001", (await one.WithTimeoutAsync("the first probe"))?.Id);
            Assert.Equal("0x0000000000000002", (await two.WithTimeoutAsync("the second probe"))?.Id);
        }
    }
}
