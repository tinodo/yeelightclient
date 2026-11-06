namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What arrives on the discovery port that is not the announcement of a light: other programs, other protocols, broken lights and
    /// people who send junk. None of it may end the discovery, and none of it may be taken for a light; the announcement that follows it is
    /// heard as if nothing had happened.
    /// </summary>
    public class DiscoveryJunkTests
    {
        private const string Location = "yeelight://127.0.0.1:55443";

        private static readonly int LargeSize = OperatingSystem.IsMacOS() ? 9000 : 65507;

        private static readonly Dictionary<string, byte[]> Junk = BuildJunk();

        public static TheoryData<string> JunkNames
        {
            get
            {
                var names = new TheoryData<string>();
                foreach (var name in Junk.Keys)
                {
                    names.Add(name);
                }

                return names;
            }
        }

        private static string Valid(string id, bool advertisement = true) => Announcements.Message(id, Location, advertisement, ("model", "color"), ("support", Announcements.Support));

        private static EventRecorder<DeviceAnnouncement> Record(YeelightDiscovery discovery)
        {
            var recorder = new EventRecorder<DeviceAnnouncement>();
            discovery.Announced += recorder.Add;
            return recorder;
        }

        private static string Padded(int totalBytes)
        {
            var baseLength = Encoding.ASCII.GetByteCount(Announcements.Message("0xPAD", Location, true, ("model", "color"), ("x-padding", string.Empty)));
            var text = Announcements.Message("0xPAD", Location, true, ("model", "color"), ("x-padding", new string('a', totalBytes - baseLength)));
            Assert.Equal(totalBytes, Encoding.ASCII.GetByteCount(text));
            return text;
        }

        private static Dictionary<string, byte[]> BuildJunk()
        {
            var junk = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            void AddText(string name, string text) => junk.Add(name, Encoding.UTF8.GetBytes(text));

            var random = new Random(20241106);
            var noise = new byte[2048];
            random.NextBytes(noise);
            var noiseAfterAStartLine = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n").Concat(noise).ToArray();

            junk.Add("an empty datagram", []);
            junk.Add("a single NUL", [0]);
            junk.Add("the biggest datagram, all A", Enumerable.Repeat((byte)'A', LargeSize).ToArray());
            junk.Add("the biggest datagram, all NUL", new byte[LargeSize]);
            junk.Add("4000 NULs", new byte[4000]);
            junk.Add("random binary", noise);
            junk.Add("a start line and then random binary", noiseAfterAStartLine);
            AddText("an HTTP 404", "HTTP/1.1 404 Not Found\r\nLocation: yeelight://127.0.0.1:55443\r\nid: 0x1\r\n\r\n");
            AddText("an HTTP 200 without a location", "HTTP/1.1 200 OK\r\nid: 0x1\r\nmodel: color\r\n");
            AddText("an HTTP 200 without an id", "HTTP/1.1 200 OK\r\nLocation: yeelight://127.0.0.1:55443\r\nmodel: color\r\n");
            AddText("an HTTP 200 with an empty id", "HTTP/1.1 200 OK\r\nLocation: yeelight://127.0.0.1:55443\r\nid:\r\n");
            AddText("an id of 65 characters", Announcements.Message(new string('x', 65), Location, true));
            AddText("a goodbye", Announcements.Message("0x1", Location, true).Replace("NTS: ssdp:alive", "NTS: ssdp:byebye", StringComparison.Ordinal));
            AddText("an answer that says byebye", Announcements.Message("0x1", Location, false) + "NTS: ssdp:byebye\r\n");
            AddText("a notify without a location", "NOTIFY * HTTP/1.1\r\nHost: 239.255.255.250:1982\r\nNTS: ssdp:alive\r\nid: 0x1\r\n");
            AddText("a search of another controller", "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n");
            AddText("a UPnP router announcing itself", "NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nCACHE-CONTROL: max-age=1800\r\nLOCATION: http://192.168.1.1:49152/rootDesc.xml\r\nNT: upnp:rootdevice\r\nNTS: ssdp:alive\r\nUSN: uuid:00000000-0000-0000-0000-000000000000::upnp:rootdevice\r\n\r\n");
            AddText("only a start line", "HTTP/1.1 200 OK");
            AddText("only the word NOTIFY", "NOTIFY");
            AddText("lines that are not headers", "HTTP/1.1 200 OK\r\nthis is not a header\r\nneither is this one\r\n");
            AddText("a thousand empty lines", "HTTP/1.1 200 OK" + string.Concat(Enumerable.Repeat("\r\n", 1000)));
            AddText("a control message", "{\"id\":1,\"method\":\"set_power\",\"params\":[\"on\",\"smooth\",500]}\r\n");
            AddText("a props message", "{\"method\":\"props\",\"params\":{\"power\":\"off\"}}\r\n");
            AddText("text that is not a message", "hello, is this the light?");
            AddText("a valid message of 4097 bytes", Padded(4097));
            AddText("a valid message after a start line that is not one", "  HTTP/1.1 200 OK\r\n" + Valid("0x1", advertisement: false).Split("\r\n", 2)[1]);
            return junk;
        }

        [Theory]
        [MemberData(nameof(JunkNames))]
        public async Task Junk_IsDropped_AndTheAnnouncementThatFollowsIsHeard(string name)
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;

            await responder.SendAsync(listener, Junk[name]);
            await Repeat.UntilAsync(() => responder.SendAsync(listener, Valid("0xSENTINEL")), () => found.Count >= 1, "the announcement after the junk");

            Assert.All(found.Items, announcement => Assert.Equal("0xSENTINEL", announcement.Id));
            Assert.Equal(1, discovery.Generations);
        }

        [Fact]
        public async Task AllTheJunk_InARow_AsTheAnswerToASearch_DoesNotEndTheSearch()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var searcher = responder.Datagrams[0].From;

            foreach (var junk in Junk.Values)
            {
                await responder.SendAsync(searcher, junk);

                // Small datagrams that follow a big one can be lost when the buffer of the socket is full: the sentinel goes on until it is heard.
                var heard = found.Count;
                await Repeat.UntilAsync(() => responder.SendAsync(searcher, Valid("0xSENTINEL", advertisement: false)), () => found.Count > heard, "the answer after the junk");
            }

            Assert.All(found.Items, announcement => Assert.Equal("0xSENTINEL", announcement.Id));
            Assert.Equal(1, discovery.Generations);
            discovery.SearchNow();
            await Wait.UntilAsync(() => responder.SearchCount >= 2, "the search that is asked for after all that junk");
        }

        [Fact]
        public async Task AnAnnouncementOfExactly4096Bytes_IsTaken_AndOneOfAByteMoreIsDropped()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;

            await responder.SendAsync(listener, Padded(4097));
            await responder.SendAsync(listener, Padded(4096));

            await found.WaitForCountAsync(1, "the announcement of 4096 bytes");
            await Wait.NotHappensAsync(() => found.Count > 1, "the announcement that is one byte too long");
        }

        [Fact]
        public async Task AnIdOf64Characters_IsTaken_AndOneOf65IsDropped()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;
            var longest = new string('b', 64);

            await responder.SendAsync(listener, Announcements.Message(new string('c', 65), Location, true));
            await responder.SendAsync(listener, Announcements.Message(longest, Location, true));

            await found.WaitForCountAsync(1, "the announcement with the id of 64 characters");
            Assert.Equal(longest, Assert.Single(found.Items).Id);
        }

        [Fact]
        public async Task AHugeName_IsCutShort_SoTheLightCannotBeMadeToHoldAnythingBig()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");

            await responder.SendAsync(discovery.ListenEndPoint!, Announcements.Message("0x1", Location, true, ("name", new string('n', 3000)), ("bright", new string('9', 600))));

            await found.WaitForCountAsync(1, "the announcement");
            Assert.True(found.Items[0].Properties["name"].Length <= 256, $"The name was kept at {found.Items[0].Properties["name"].Length} characters.");
            Assert.True(found.Items[0].Properties["bright"].Length <= 256);
        }

        [Fact]
        public async Task InvalidUtf8_InAnAnnouncement_DoesNotEndTheDiscovery()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;
            var broken = Encoding.ASCII.GetBytes("NOTIFY * HTTP/1.1\r\nLocation: yeelight://127.0.0.1:55443\r\nNTS: ssdp:alive\r\nid: 0xBROKEN\r\nname: ")
                .Concat(new byte[] { 0xFF, 0xFE, 0xC0, 0x80, 0xED, 0xA0, 0x80 })
                .Concat(Encoding.ASCII.GetBytes("\r\n"))
                .ToArray();

            await responder.SendAsync(listener, broken);
            await Repeat.UntilAsync(() => responder.SendAsync(listener, Valid("0xSENTINEL")), () => found.Items.Any(item => item.Id == "0xSENTINEL"), "the announcement after the broken one");

            // Whatever was made of the broken one (it can be taken, with replacement characters), it holds no NUL.
            foreach (var announcement in found.Items)
            {
                Assert.All(announcement.Properties.Values.Append(announcement.Id), text => Assert.DoesNotContain('\0', text));
            }
        }

        [Fact]
        public async Task AFloodOfJunkFromManySenders_DoesNotKeepTheRealAnswerFromBeingHeard()
        {
            await using var responder = new ScriptedResponder();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Search(target: responder.EndPoint));
            var found = Record(discovery);
            discovery.Start();
            await Wait.UntilAsync(() => responder.SearchCount >= 1, "the search");
            var searcher = responder.Datagrams[0].From;

            // What is cut short at 1500 bytes stays junk, except the message that is only too long: that one is left out.
            var pool = Junk.Where(pair => !pair.Key.Contains("4097", StringComparison.Ordinal)).Select(pair => pair.Value.Take(1500).ToArray()).ToArray();
            var senders = Enumerable.Range(0, 8).Select(number => Task.Run(async () =>
            {
                await using var sender = new ScriptedResponder();
                for (var i = 0; i < 300; i++)
                {
                    await sender.SendAsync(searcher, pool[(number + i) % pool.Length]);
                }
            })).ToArray();
            await Task.WhenAll(senders).WithTimeoutAsync("the junk to be sent");

            await Repeat.UntilAsync(() => responder.SendAsync(searcher, Valid("0xSENTINEL", advertisement: false)), () => found.Count >= 1, "the real answer after the flood");
            Assert.All(found.Items, announcement => Assert.Equal("0xSENTINEL", announcement.Id));
        }
    }
}
