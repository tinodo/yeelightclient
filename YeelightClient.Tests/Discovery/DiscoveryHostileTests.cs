namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What a hostile or misconfigured neighbour can do with the messages that the discovery trusts: it can say that it is a light, and
    /// it can say that the light is somewhere else. A message that names an address other than the one that it came from is dropped, so that
    /// nobody can make the client connect to a computer that did not send it. Nothing here connects to anything but the loopback.
    /// </summary>
    public class DiscoveryHostileTests
    {
        private static EventRecorder<DeviceAnnouncement> Record(YeelightDiscovery discovery)
        {
            var recorder = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += recorder.Add;
            return recorder;
        }

        private static string Good(string id = "0xGOOD", bool advertisement = false) => Announcements.Message(id, "yeelight://127.0.0.1:55443", advertisement, ("model", "color"));

        [Theory]
        [InlineData("yeelight://127.0.0.2:55443")]
        [InlineData("yeelight://127.0.0.2:22")]
        [InlineData("yeelight://192.168.1.1:22")]
        [InlineData("yeelight://10.0.0.1:55443")]
        [InlineData("yeelight://172.16.0.9:3389")]
        [InlineData("yeelight://203.0.113.9:55443")]
        [InlineData("yeelight://localhost:55443")]
        [InlineData("yeelight://example.test:55443")]
        [InlineData("yeelight://[::1]:55443")]
        [InlineData("yeelight://[::ffff:127.0.0.1]:55443")]
        [InlineData("yeelight://127.0.0.1:0")]
        [InlineData("yeelight://127.0.0.1:65536")]
        [InlineData("yeelight://127.0.0.1")]
        [InlineData("yeelight://:55443")]
        [InlineData("yeelight://user@127.0.0.1:55443")]
        [InlineData("yeelight://127.0.0.1:55443,127.0.0.2:22")]
        [InlineData("http://127.0.0.1:55443")]
        [InlineData("127.0.0.1:55443")]
        [InlineData("")]
        public async Task AnAnswerThatDoesNotNameTheAddressItCameFrom_IsDropped_AndTheDiscoveryGoesOn(string location)
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var searcher = responder.Datagrams[0].From;

            await responder.SendAsync(searcher, Announcements.Message("0xEVIL", location, advertisement: false, ("model", "color")));
            await responder.SendAsync(searcher, Good());

            await found.WaitForCountAsync(1, "the answer that is right");
            Assert.Equal("0xGOOD", Assert.Single(found.Items).Id);
        }

        [Fact]
        public async Task AnAnnouncementOfALightOnItsOwnAddress_IsTakenWhateverPortItNames()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");

            // The port of the control service is the light's to choose: only the host is checked. What a neighbour gets by naming a strange
            // port on its own address is a connection to itself, which it could have asked for anyway.
            await responder.SendAsync(discovery.ListenEndPoint!, Announcements.Message("0xODD", "yeelight://127.0.0.1:22", advertisement: true, ("model", "color")));

            await found.WaitForCountAsync(1, "the announcement");
            Assert.Equal(22, found.Items[0].Port);
        }

        [FactIfLoopbackAlias]
        public async Task AMessageFromAnotherAddress_ThatNamesTheAddressOfTheLight_IsDropped_ButOneThatNamesItsOwnIsTaken()
        {
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;

            // A neighbour at 127.0.0.2 says that the light is at 127.0.0.1 (where a real light is): it is not heard.
            await Announcements.SendFromAsync(LoopbackAlias.Address, listener, Encoding.UTF8.GetBytes(Announcements.Message("0xSPOOF", "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color"))));
            await Announcements.SendFromAsync(LoopbackAlias.Address, listener, Encoding.UTF8.GetBytes(Announcements.Message("0xOWN", "yeelight://127.0.0.2:55443", advertisement: true, ("model", "color"))));

            await found.WaitForCountAsync(1, "the announcement of the neighbour about itself");
            var announcement = Assert.Single(found.Items);
            Assert.Equal("0xOWN", announcement.Id);
            Assert.Equal("127.0.0.2", announcement.Host);
        }

        [FactIfLoopbackAlias]
        public async Task ALightThatPointsTheClientAtAnotherComputer_IsNeverConnectedTo_WhetherItAnswersASearch()
        {
            await using var honeypot = new Honeypot();
            await using var responder = new ScriptedResponder();
            await using var real = new SimulatedBulb(new SimulatedBulbOptions { Id = "0xREAL" });
            await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false }, adjust: options => options with { SearchEndpoint = responder.EndPoint });
            client.StartDiscovery();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var searcher = responder.Datagrams[0].From;

            await responder.SendAsync(searcher, Announcements.Message("0xEVIL", $"yeelight://127.0.0.2:{honeypot.Port}", advertisement: false, ("model", "color"), ("support", Announcements.Support)));
            await responder.SendAsync(searcher, Announcements.Message(real.Id, real.Location, advertisement: false, ("model", "color"), ("support", Announcements.Support)));

            await Wait.UntilAsync(() => client.FindDevice("0xREAL") is { IsConnected: true }, "the real light to be found and connected");
            Assert.Null(client.FindDevice("0xEVIL"));
            Assert.Single(client.Devices);
            await Wait.NotHappensAsync(() => honeypot.Connections > 0, "a connection to the address that the neighbour named", TimeSpan.FromMilliseconds(700));
        }

        [FactIfLoopbackAlias]
        public async Task ALightThatPointsTheClientAtAnotherComputer_IsNeverConnectedTo_WhetherItAnnouncesItself()
        {
            await using var honeypot = new Honeypot();
            await using var responder = new ScriptedResponder();
            await using var real = new SimulatedBulb(new SimulatedBulbOptions { Id = "0xREAL" });
            await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false });
            client.StartListening();
            await Wait.UntilAsync(() => ClientInternals.ListenEndPoint(client) is not null, "the listener");
            var listener = ClientInternals.ListenEndPoint(client)!;

            await responder.SendAsync(listener, Announcements.Message("0xEVIL", $"yeelight://127.0.0.2:{honeypot.Port}", advertisement: true, ("model", "color"), ("support", Announcements.Support)));
            await real.AnnounceAsync(listener);

            await Wait.UntilAsync(() => client.FindDevice("0xREAL") is { IsConnected: true }, "the real light to be found and connected");
            Assert.Null(client.FindDevice("0xEVIL"));
            Assert.Single(client.Devices);
            await Wait.NotHappensAsync(() => honeypot.Connections > 0, "a connection to the address that the neighbour named", TimeSpan.FromMilliseconds(700));
        }

        [Fact]
        public async Task AFloodOfDatagrams_DoesNotStopTheDiscovery()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;
            var junk = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n\r\n");
            var valid = Encoding.UTF8.GetBytes(Good("0xFLOOD", advertisement: true));

            for (var i = 0; i < 5000; i++)
            {
                await responder.SendAsync(listener, i % 3 == 0 ? valid : junk);
                if (i % 200 == 0)
                {
                    await Task.Yield();
                }
            }

            // The flood can overflow the buffer of the socket and lose datagrams (that is UDP): what counts is that the next one is heard.
            await Repeat.UntilAsync(
                () => responder.SendAsync(listener, Good("0xAFTER", advertisement: true)),
                () => found.Items.Any(item => item.Id == "0xAFTER"),
                "the announcement after the flood");
        }

        private sealed class Honeypot : IAsyncDisposable
        {
            private readonly TcpListener _listener = new(LoopbackAlias.Address, 0);
            private readonly Task _accepting;
            private int _connections;

            public Honeypot()
            {
                this._listener.Start();
                this.Port = ((IPEndPoint)this._listener.LocalEndpoint).Port;
                this._accepting = Task.Run(this.AcceptAsync);
            }

            public int Port { get; }

            public int Connections => Volatile.Read(ref this._connections);

            public async ValueTask DisposeAsync()
            {
                this._listener.Stop();
                await this._accepting.ConfigureAwait(false);
            }

            private async Task AcceptAsync()
            {
                try
                {
                    while (true)
                    {
                        using var client = await this._listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        Interlocked.Increment(ref this._connections);
                    }
                }
                catch (Exception)
                {
                    // The listener was stopped.
                }
            }
        }
    }
}
