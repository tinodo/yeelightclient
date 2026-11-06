namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Text;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What a light says about itself, in the answer to a search and in its own announcement (section 3 of the Yeelight Inter-Operation
    /// Specification, https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf). Anyone on the network can send such a message,
    /// so it is read defensively, and the address in it is only believed when it is the address that the message came from.
    /// </summary>
    public class DeviceAnnouncementTests
    {
        private static readonly string[] KnownProperties = ["power", "bright", "color_mode", "ct", "rgb", "hue", "sat", "name"];

        private static string Message(params string[] lines)
        {
            return string.Join("\r\n", lines) + "\r\n";
        }

        private static string Minimal(string startLine = "HTTP/1.1 200 OK", params string[] more)
        {
            return Message([startLine, "Location: yeelight://192.168.1.2:55443", "id: 0x1", .. more]);
        }

        private static bool TryParse(string? message)
        {
            return DeviceAnnouncement.TryParse(message, out _);
        }

        [Fact]
        public void TryParse_TheAnswerToASearch_IsRead()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response());

            Assert.Equal("yeelight://192.168.1.239:55443", announcement.Location);
            Assert.Equal("192.168.1.239", announcement.Host);
            Assert.Equal(55443, announcement.Port);
            Assert.Equal("0x000000000015243f", announcement.Id);
            Assert.Equal("color", announcement.Model);
            Assert.Equal("18", announcement.FirmwareVersion);
            Assert.Equal(UnitMessages.Support.Split(' '), announcement.Support);
            Assert.False(announcement.IsAdvertisement);
            Assert.Equal("on", announcement.Properties["power"]);
            Assert.Equal("100", announcement.Properties["bright"]);
            Assert.Equal("2", announcement.Properties["color_mode"]);
            Assert.Equal("4000", announcement.Properties["ct"]);
            Assert.Equal("16711680", announcement.Properties["rgb"]);
            Assert.Equal("100", announcement.Properties["hue"]);
            Assert.Equal("35", announcement.Properties["sat"]);
            Assert.Equal("my_bulb", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_TheAnnouncementOfALight_IsReadAndKnownForOne()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Advertisement(name: "Desk"));

            Assert.True(announcement.IsAdvertisement);
            Assert.Equal("Desk", announcement.Properties["name"]);
            Assert.Equal("0x000000000015243f", announcement.Id);
            Assert.Equal("192.168.1.239", announcement.Host);
            Assert.Equal(55443, announcement.Port);
            Assert.Equal(UnitMessages.Support.Split(' '), announcement.Support);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TryParse_OnlyThePropertiesOfALightAreKept_NotTheOtherHeaders(bool advertisement)
        {
            var announcement = UnitMessages.Parse(advertisement ? UnitMessages.Advertisement() : UnitMessages.Response());

            Assert.Equal(KnownProperties.Order(), announcement.Properties.Keys.Order());
        }

        [Fact]
        public void TryParse_OfWhatTheMessageLeavesOut_HasNothing()
        {
            var announcement = UnitMessages.Parse(Minimal("NOTIFY * HTTP/1.1", "NTS: ssdp:alive"));

            Assert.Empty(announcement.Properties);
            Assert.Empty(announcement.Support);
            Assert.Equal(string.Empty, announcement.Model);
            Assert.Equal(string.Empty, announcement.FirmwareVersion);
        }

        [Fact]
        public void TryParse_EveryPropertyOfTheSpecification_IsKept_AndNothingElse()
        {
            var announcement = UnitMessages.Parse(
                Minimal(
                    "HTTP/1.1 200 OK",
                    "power: on", "bright: 50", "ct: 2700", "rgb: 255", "hue: 10", "sat: 20", "color_mode: 3", "flowing: 1", "delayoff: 5",
                    "flow_params: 0,0,1000,2,2700,100", "music_on: 1", "name: n", "bg_power: off", "bg_flowing: 0", "bg_flow_params: x",
                    "bg_ct: 3000", "bg_lmode: 2", "bg_bright: 40", "bg_rgb: 1", "bg_hue: 2", "bg_sat: 3", "nl_br: 7", "active_mode: 1",
                    "bg_foo: 1", "foo: 1", "x-power: on", "Server: POSIX"));

            Assert.Equal(YeelightProperties.All.Order(), announcement.Properties.Keys.Order());
            Assert.Equal("0,0,1000,2,2700,100", announcement.Properties["flow_params"]);
            Assert.Equal("7", announcement.Properties["nl_br"]);
        }

        [Fact]
        public void TryParse_TheNamesOfTheHeaders_AreNotCaseSensitive_AndTheKeysAreTheNamesOfTheProperties()
        {
            var announcement = UnitMessages.Parse(
                Message(
                    "HTTP/1.1 200 OK",
                    "LOCATION: yeelight://10.0.0.8:55443",
                    "ID: 0xABC",
                    "Model: color",
                    "FW_VER: 20",
                    "Support: set_rgb",
                    "POWER: off",
                    "Bright: 42",
                    "Color_Mode: 1",
                    "NAME: Lamp"));

            Assert.Equal("10.0.0.8", announcement.Host);
            Assert.Equal("0xABC", announcement.Id);
            Assert.Equal("color", announcement.Model);
            Assert.Equal("20", announcement.FirmwareVersion);
            Assert.Equal(["set_rgb"], announcement.Support);
            Assert.Equal("off", announcement.Properties["power"]);
            Assert.Equal("42", announcement.Properties["bright"]);
            Assert.Equal("1", announcement.Properties["color_mode"]);
            Assert.Equal("Lamp", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_TheValuesOfTheHeaders_AreCaseSensitive()
        {
            var announcement = UnitMessages.Parse(Minimal("HTTP/1.1 200 OK", "power: ON", "name: MixedCase"));

            Assert.Equal("ON", announcement.Properties["power"]);
            Assert.Equal("MixedCase", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_LineFeedsAlone_EndTheLinesToo()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(lineBreak: "\n"));

            Assert.Equal("0x000000000015243f", announcement.Id);
            Assert.Equal("my_bulb", announcement.Properties["name"]);
            Assert.Equal("100", announcement.Properties["bright"]);
        }

        [Fact]
        public void TryParse_WhiteSpaceAroundANameAndAValue_IsNotPartOfThem()
        {
            var announcement = UnitMessages.Parse(Message("HTTP/1.1 200 OK", "  Location :   yeelight://192.168.1.2:55443   ", "id:0x1", "name :\t Lamp \t"));

            Assert.Equal("yeelight://192.168.1.2:55443", announcement.Location);
            Assert.Equal("0x1", announcement.Id);
            Assert.Equal("Lamp", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_AValueKeepsItsColonsAndSpaces()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(name: "Living: main  lamp"));

            Assert.Equal("Living: main  lamp", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_AnEmptyName_IsAName()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(name: string.Empty));

            Assert.Equal(string.Empty, announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_OfARepeatedHeader_TheFirstOneCounts()
        {
            var announcement = UnitMessages.Parse(Message("HTTP/1.1 200 OK", "Location: yeelight://192.168.1.2:55443", "Location: yeelight://10.9.9.9:1", "id: 0x1", "id: 0x2", "name: one", "NAME: two"));

            Assert.Equal("192.168.1.2", announcement.Host);
            Assert.Equal("0x1", announcement.Id);
            Assert.Equal("one", announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_ALineThatIsNotAHeader_IsSkipped()
        {
            var announcement = UnitMessages.Parse(Message("HTTP/1.1 200 OK", "no colon here", ": value without a name", "Location: yeelight://192.168.1.2:55443", string.Empty, "id: 0x1", "   "));

            Assert.Equal("0x1", announcement.Id);
        }

        [Theory]
        [InlineData("set_rgb set_hsv", new[] { "set_rgb", "set_hsv" })]
        [InlineData("  set_rgb   set_hsv  ", new[] { "set_rgb", "set_hsv" })]
        [InlineData("set_rgb\tset_hsv", new[] { "set_rgb", "set_hsv" })]
        [InlineData("set_rgb \t set_hsv\t", new[] { "set_rgb", "set_hsv" })]
        [InlineData("set_rgb", new[] { "set_rgb" })]
        [InlineData("", new string[0])]
        [InlineData("   ", new string[0])]
        [InlineData("\t", new string[0])]
        public void TryParse_TheSupportedMethods_AreSeparatedByWhiteSpaceOfAnyKind(string support, string[] expected)
        {
            var announcement = UnitMessages.Parse(Minimal("HTTP/1.1 200 OK", $"support: {support}"));

            Assert.Equal(expected, announcement.Support);
        }

        [Fact]
        public void TryParse_ANameOfMoreThan256Characters_IsCutOff()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(name: new string('n', 300)));

            Assert.Equal(new string('n', 256), announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_ANameOf256Characters_IsKept()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(name: new string('n', 256)));

            Assert.Equal(new string('n', 256), announcement.Properties["name"]);
        }

        [Fact]
        public void TryParse_AnyPropertyOfMoreThan256Characters_IsCutOff()
        {
            var announcement = UnitMessages.Parse(Minimal("HTTP/1.1 200 OK", "power: " + new string('x', 1000), "flow_params: " + new string('1', 1000)));

            Assert.Equal(256, announcement.Properties["power"].Length);
            Assert.Equal(256, announcement.Properties["flow_params"].Length);
        }

        [Fact]
        public void TryParse_ANameThatIsCutOff_DoesNotEndInHalfOfASurrogatePair()
        {
            // The 256th character is the first half of a pair: cutting there leaves text that cannot be written as UTF-8 or as JSON.
            var name = new string('x', 255) + "\U0001F4A1";

            var announcement = UnitMessages.Parse(UnitMessages.Response(name: name));

            var kept = announcement.Properties["name"];
            Assert.True(kept.Length <= 256);
            Assert.False(char.IsHighSurrogate(kept[^1]), "The name ends in half of a surrogate pair.");
            Assert.Equal(kept, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(kept)));
        }

        [Fact]
        public void TryParse_TheModelAndTheFirmwareVersion_AreWhatTheMessageSays()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(model: "ceiling10", firmware: "2.0.6_0058"));

            Assert.Equal("ceiling10", announcement.Model);
            Assert.Equal("2.0.6_0058", announcement.FirmwareVersion);
        }

        [Theory]
        [InlineData("HTTP/1.1 200 OK")]
        [InlineData("HTTP/1.1 200")]
        [InlineData("NOTIFY * HTTP/1.1")]
        [InlineData("NOTIFY * HTTP/1.1 ")]
        public void TryParse_TheStartLinesOfTheSpecification_Count(string startLine)
        {
            Assert.True(TryParse(Minimal(startLine)));
        }

        [Theory]
        [InlineData("M-SEARCH * HTTP/1.1")]
        [InlineData("HTTP/1.1 404 Not Found")]
        [InlineData("HTTP/1.1 500 Internal Server Error")]
        [InlineData("HTTP/1.0 200 OK")]
        [InlineData("HTTP/2 200 OK")]
        [InlineData(" HTTP/1.1 200 OK")]
        [InlineData("http/1.1 200 OK")]
        [InlineData("NOTIFY * HTTP/1.0")]
        [InlineData("notify * http/1.1")]
        [InlineData("NOTIFY / HTTP/1.1")]
        [InlineData("OK")]
        [InlineData("garbage")]
        [InlineData("")]
        [InlineData("Location: yeelight://192.168.1.2:55443")]
        public void TryParse_AnotherStartLine_IsNotAnAnnouncement(string startLine)
        {
            Assert.False(DeviceAnnouncement.TryParse(Minimal(startLine), out var announcement));
            Assert.Null(announcement);
        }

        [Fact]
        public void TryParse_ASearchOfSomeoneElse_IsNotAnAnnouncement()
        {
            // The search that this client sends itself comes back to it when it is sent to the multicast group: it is not a light.
            var search = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n";

            Assert.False(TryParse(search));
        }

        [Theory]
        [InlineData("ssdp:alive", true)]
        [InlineData("SSDP:ALIVE", true)]
        [InlineData("ssdp:byebye", false)]
        [InlineData("ssdp:update", false)]
        [InlineData("alive", false)]
        [InlineData("", false)]
        public void TryParse_ANotificationSubtypeThatIsNotAlive_IsNotAnAnnouncement(string subtype, bool accepted)
        {
            Assert.Equal(accepted, TryParse(Minimal("NOTIFY * HTTP/1.1", $"NTS: {subtype}")));
            Assert.Equal(accepted, TryParse(Minimal("HTTP/1.1 200 OK", $"nts: {subtype}")));
        }

        [Fact]
        public void TryParse_WithoutANotificationSubtype_IsFine()
        {
            Assert.True(TryParse(Minimal("NOTIFY * HTTP/1.1")));
        }

        [Theory]
        [InlineData("yeelight://192.168.1.2:55443", "192.168.1.2", 55443)]
        [InlineData("YEELIGHT://192.168.1.2:55443", "192.168.1.2", 55443)]
        [InlineData("yeelight://192.168.1.2:55443/", "192.168.1.2", 55443)]
        [InlineData("yeelight://192.168.1.2:1", "192.168.1.2", 1)]
        [InlineData("yeelight://192.168.1.2:65535", "192.168.1.2", 65535)]
        [InlineData("yeelight://bulb.local:55443", "bulb.local", 55443)]
        public void TryParse_TheLocation_IsSplitIntoTheAddressAndThePort(string location, string host, int port)
        {
            var announcement = UnitMessages.Parse(Message("HTTP/1.1 200 OK", $"Location: {location}", "id: 0x1"));

            Assert.Equal(location, announcement.Location);
            Assert.Equal(host, announcement.Host);
            Assert.Equal(port, announcement.Port);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("http://192.168.1.2:80")]
        [InlineData("192.168.1.2:55443")]
        [InlineData("yeelight:192.168.1.2:55443")]
        [InlineData("yeelight:/192.168.1.2:55443")]
        [InlineData("yeelight://")]
        [InlineData("yeelight://192.168.1.2")]
        [InlineData("yeelight://192.168.1.2:")]
        [InlineData("yeelight://192.168.1.2:0")]
        [InlineData("yeelight://192.168.1.2:65536")]
        [InlineData("yeelight://192.168.1.2:99999999999")]
        [InlineData("yeelight://192.168.1.2:-1")]
        [InlineData("yeelight://192.168.1.2:+80")]
        [InlineData("yeelight://192.168.1.2:http")]
        [InlineData("yeelight://192.168.1.2:80/path")]
        [InlineData("yeelight://:55443")]
        public void TryParse_ALocationThatIsNotOne_IsNotAnAnnouncement(string location)
        {
            Assert.False(TryParse(Message("HTTP/1.1 200 OK", $"Location: {location}", "id: 0x1")));
        }

        [Fact]
        public void TryParse_WithoutALocation_IsNotAnAnnouncement()
        {
            Assert.False(TryParse(Message("HTTP/1.1 200 OK", "id: 0x1", "model: color")));
        }

        [Fact]
        public void TryParse_WithoutAnId_IsNotAnAnnouncement()
        {
            Assert.False(TryParse(Message("HTTP/1.1 200 OK", "Location: yeelight://192.168.1.2:55443", "model: color")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParse_AnEmptyId_IsNotAnAnnouncement(string id)
        {
            Assert.False(TryParse(Message("HTTP/1.1 200 OK", "Location: yeelight://192.168.1.2:55443", $"id: {id}")));
        }

        [Fact]
        public void TryParse_AnIdOf64Characters_IsFine_AndOneOf65IsNot()
        {
            Assert.True(TryParse(Message("HTTP/1.1 200 OK", "Location: yeelight://192.168.1.2:55443", "id: " + new string('1', 64))));
            Assert.False(TryParse(Message("HTTP/1.1 200 OK", "Location: yeelight://192.168.1.2:55443", "id: " + new string('1', 65))));
        }

        [Fact]
        public void TryParse_AMessageOf4096Characters_IsRead_AndOneCharacterLongerIsNot()
        {
            var message = UnitMessages.Response();
            var padding = 4096 - message.Length - "x-padding: \r\n".Length;
            Assert.True(padding > 0);
            var exact = message + "x-padding: " + new string('a', padding) + "\r\n";
            Assert.Equal(4096, exact.Length);

            Assert.True(TryParse(exact));
            Assert.False(TryParse(exact + "x"));
        }

        [Fact]
        public void TryParse_NullAndEmpty_AreNotAnAnnouncement()
        {
            Assert.False(DeviceAnnouncement.TryParse(null, out var none));
            Assert.Null(none);
            Assert.False(DeviceAnnouncement.TryParse(string.Empty, out var empty));
            Assert.Null(empty);
        }

        [Fact]
        public void TryParse_BinaryData_IsNotAnAnnouncementAndNeverThrows()
        {
            var text = string.Concat(Enumerable.Range(0, 3000).Select(number => (char)(number % 256)));

            Assert.False(TryParse(text));
        }

        [Fact]
        public void TryParse_RandomText_NeverThrows()
        {
            var random = new Random(1982);
            string[] pieces = ["HTTP/1.1 200 OK", "NOTIFY * HTTP/1.1", "\r\n", "\n", "\r", ":", " ", "Location", "yeelight://", "192.168.1.2", ":55443", "id", "0x1", "NTS", "ssdp:alive", "support", "name", "é", "\u0000"];
            for (var round = 0; round < 3000; round++)
            {
                var text = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => pieces[random.Next(pieces.Length)]));

                var exception = Record.Exception(() => DeviceAnnouncement.TryParse(text, out _));

                Assert.True(exception is null, $"TryParse threw {exception?.GetType().Name} for: {text}");
            }
        }

        [Fact]
        public void IsFrom_TheAddressInTheMessage_MustBeTheAddressItCameFrom()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response("yeelight://192.168.1.239:55443"));

            Assert.True(announcement.IsFrom(IPAddress.Parse("192.168.1.239")));
            Assert.False(announcement.IsFrom(IPAddress.Parse("192.168.1.240")));
            Assert.False(announcement.IsFrom(IPAddress.Parse("192.168.1.238")));
            Assert.False(announcement.IsFrom(IPAddress.Parse("10.0.0.1")));
        }

        [Fact]
        public void IsFrom_AnAddressThatPointsAtAnotherComputer_IsNeverTrusted()
        {
            // The message of somebody who wants this program to connect to a computer of his choice.
            var announcement = UnitMessages.Parse(UnitMessages.Response("yeelight://192.168.1.7:55443"));

            Assert.False(announcement.IsFrom(IPAddress.Parse("192.168.1.66")));
        }

        [Fact]
        public void IsFrom_AnAddressThatAnIpv6SocketReportsForAnIpv4Sender_Counts()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response("yeelight://192.168.1.239:55443"));

            Assert.True(announcement.IsFrom(IPAddress.Parse("192.168.1.239").MapToIPv6()));
            Assert.False(announcement.IsFrom(IPAddress.Parse("192.168.1.240").MapToIPv6()));
        }

        [Theory]
        [InlineData("yeelight://8.8.8.8:55443", "8.8.8.8")]
        [InlineData("yeelight://1.1.1.1:55443", "1.1.1.1")]
        [InlineData("yeelight://239.255.255.250:55443", "239.255.255.250")]
        [InlineData("yeelight://0.0.0.0:55443", "0.0.0.0")]
        [InlineData("yeelight://255.255.255.255:55443", "255.255.255.255")]
        public void IsFrom_ADeviceOutsideTheLocalNetwork_IsNeverTrusted_EvenWhenTheMessageCameFromThere(string location, string source)
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(location));

            Assert.False(announcement.IsFrom(IPAddress.Parse(source)));
        }

        [Fact]
        public void IsFrom_ANameIsNotAnAddress()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response("yeelight://bulb.example.com:55443"));

            Assert.False(announcement.IsFrom(IPAddress.Parse("192.168.1.5")));
        }

        [Theory]
        [InlineData("yeelight://[fe80::1]:55443", "fe80::1")]
        [InlineData("yeelight://fe80::1:55443", "fe80::1")]
        [InlineData("yeelight://[::1]:55443", "::1")]
        [InlineData("yeelight://::1:55443", "::1")]
        public void IsFrom_AnIpv6Address_IsNotTrusted(string location, string source)
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response(location));

            Assert.False(announcement.IsFrom(IPAddress.Parse(source)));
        }

        [Fact]
        public void IsFrom_TheLoopback_IsTheLocalNetworkOfTheTests()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response("yeelight://127.0.0.1:55443"));

            Assert.True(announcement.IsFrom(IPAddress.Loopback));
            Assert.True(announcement.IsFrom(IPAddress.Loopback.MapToIPv6()));
            Assert.False(announcement.IsFrom(IPAddress.IPv6Loopback));
        }

        [Fact]
        public void IsFrom_ASourceThatIsNull_IsRefused()
        {
            var announcement = UnitMessages.Parse(UnitMessages.Response());

            var refusal = Assert.Throws<ArgumentNullException>(() => announcement.IsFrom(null!));

            Assert.Equal("source", refusal.ParamName);
        }

        [Theory]
        [InlineData("10.0.0.0", true)]
        [InlineData("10.1.2.3", true)]
        [InlineData("10.255.255.255", true)]
        [InlineData("9.255.255.255", false)]
        [InlineData("11.0.0.0", false)]
        [InlineData("127.0.0.0", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.255.255.255", true)]
        [InlineData("126.255.255.255", false)]
        [InlineData("128.0.0.0", false)]
        [InlineData("172.16.0.0", true)]
        [InlineData("172.20.1.1", true)]
        [InlineData("172.31.255.255", true)]
        [InlineData("172.15.255.255", false)]
        [InlineData("172.32.0.0", false)]
        [InlineData("172.0.0.1", false)]
        [InlineData("192.168.0.0", true)]
        [InlineData("192.168.1.239", true)]
        [InlineData("192.168.255.255", true)]
        [InlineData("192.167.255.255", false)]
        [InlineData("192.169.0.0", false)]
        [InlineData("169.254.0.0", true)]
        [InlineData("169.254.10.1", true)]
        [InlineData("169.254.255.255", true)]
        [InlineData("169.253.255.255", false)]
        [InlineData("169.255.0.0", false)]
        [InlineData("100.64.0.0", true)]
        [InlineData("100.100.100.100", true)]
        [InlineData("100.127.255.255", true)]
        [InlineData("100.63.255.255", false)]
        [InlineData("100.128.0.0", false)]
        [InlineData("0.0.0.0", false)]
        [InlineData("1.1.1.1", false)]
        [InlineData("8.8.8.8", false)]
        [InlineData("192.0.2.1", false)]
        [InlineData("198.18.0.1", false)]
        [InlineData("224.0.0.251", false)]
        [InlineData("239.255.255.250", false)]
        [InlineData("255.255.255.255", false)]
        public void IsLocalNetworkAddress_RecognizesThePrivateRangesAndTheirNeighbours(string address, bool expected)
        {
            Assert.Equal(expected, DeviceAnnouncement.IsLocalNetworkAddress(IPAddress.Parse(address)));
        }

        [Fact]
        public void IsLocalNetworkAddress_AgreesWithTheRangesForEveryFirstTwoParts()
        {
            // The ranges: private (RFC 1918), loopback, link-local (RFC 3927) and shared (RFC 6598, which some VPNs use).
            IPNetwork[] ranges =
            [
                new(IPAddress.Parse("10.0.0.0"), 8),
                new(IPAddress.Parse("127.0.0.0"), 8),
                new(IPAddress.Parse("172.16.0.0"), 12),
                new(IPAddress.Parse("192.168.0.0"), 16),
                new(IPAddress.Parse("169.254.0.0"), 16),
                new(IPAddress.Parse("100.64.0.0"), 10),
            ];
            foreach (var first in Enumerable.Range(0, 256))
            {
                foreach (var second in Enumerable.Range(0, 256))
                {
                    foreach (var (third, fourth) in new[] { (0, 0), (1, 1), (255, 255) })
                    {
                        var address = new IPAddress([(byte)first, (byte)second, (byte)third, (byte)fourth]);
                        var expected = ranges.Any(range => range.Contains(address));
                        var actual = DeviceAnnouncement.IsLocalNetworkAddress(address);

                        if (actual != expected)
                        {
                            Assert.Fail($"{address} is {(expected ? string.Empty : "not ")}an address of a local network, but IsLocalNetworkAddress says {actual}.");
                        }
                    }
                }
            }
        }

        [Theory]
        [InlineData("::")]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("fd00::1")]
        [InlineData("fc00::1")]
        [InlineData("2001:db8::1")]
        public void IsLocalNetworkAddress_AnIpv6Address_IsNot(string address)
        {
            Assert.False(DeviceAnnouncement.IsLocalNetworkAddress(IPAddress.Parse(address)));
        }

        [Fact]
        public void IsLocalNetworkAddress_Null_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => DeviceAnnouncement.IsLocalNetworkAddress(null!));

            Assert.Equal("address", refusal.ParamName);
        }
    }
}
