namespace Yeelight.Tests.Unit
{
    using System;
    using Xunit;

    /// <summary>
    /// The location of a light, <c>yeelight://192.168.1.239:55443</c>: the address and the port of its control service.
    /// </summary>
    public class YeelightLocationTests
    {
        [Theory]
        [InlineData("yeelight://192.168.1.239:55443", "192.168.1.239", 55443)]
        [InlineData("yeelight://10.0.0.1:1", "10.0.0.1", 1)]
        [InlineData("yeelight://10.0.0.1:65535", "10.0.0.1", 65535)]
        [InlineData("yeelight://bulb.local:55443", "bulb.local", 55443)]
        [InlineData("yeelight://bulb:80", "bulb", 80)]
        [InlineData("yeelight://192.168.1.239:00080", "192.168.1.239", 80)]
        public void TryParse_ASchemeAnAddressAndAPort_AreSplit(string location, string host, int port)
        {
            Assert.True(YeelightLocation.TryParse(location, out var parsedHost, out var parsedPort));
            Assert.Equal(host, parsedHost);
            Assert.Equal(port, parsedPort);
        }

        [Theory]
        [InlineData("YEELIGHT://192.168.1.239:55443")]
        [InlineData("Yeelight://192.168.1.239:55443")]
        [InlineData("yEeLiGhT://192.168.1.239:55443")]
        public void TryParse_TheCaseOfTheScheme_DoesNotMatter(string location)
        {
            Assert.True(YeelightLocation.TryParse(location, out var host, out var port));
            Assert.Equal("192.168.1.239", host);
            Assert.Equal(55443, port);
        }

        [Fact]
        public void TryParse_ATrailingSlash_IsLeftOut()
        {
            Assert.True(YeelightLocation.TryParse("yeelight://192.168.1.239:55443/", out var host, out var port));
            Assert.Equal("192.168.1.239", host);
            Assert.Equal(55443, port);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("80", true)]
        [InlineData("65535", true)]
        [InlineData("0", false)]
        [InlineData("00", false)]
        [InlineData("65536", false)]
        [InlineData("100000", false)]
        [InlineData("2147483648", false)]
        [InlineData("99999999999999999999", false)]
        [InlineData("-1", false)]
        [InlineData("+80", false)]
        [InlineData(" 80", false)]
        [InlineData("80 ", false)]
        [InlineData("8 0", false)]
        [InlineData("8.0", false)]
        [InlineData("0x50", false)]
        [InlineData("eighty", false)]
        [InlineData("", false)]
        [InlineData("\u0668\u0660", false)]
        public void TryParse_ThePort_IsWholeNumberOfDigitsFrom1To65535(string port, bool valid)
        {
            Assert.Equal(valid, YeelightLocation.TryParse($"yeelight://192.168.1.239:{port}", out _, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("192.168.1.239:55443")]
        [InlineData("//192.168.1.239:55443")]
        [InlineData("http://192.168.1.239:55443")]
        [InlineData("https://192.168.1.239:55443")]
        [InlineData("yeelight:192.168.1.239:55443")]
        [InlineData("yeelight:/192.168.1.239:55443")]
        [InlineData("yeelights://192.168.1.239:55443")]
        [InlineData("xyeelight://192.168.1.239:55443")]
        [InlineData("yeelight://")]
        [InlineData("yeelight:///")]
        [InlineData("yeelight://192.168.1.239")]
        [InlineData("yeelight://192.168.1.239:")]
        [InlineData("yeelight://:55443")]
        [InlineData("yeelight://:")]
        [InlineData("yeelight://192.168.1.239:55443/path")]
        [InlineData("yeelight://192.168.1.239:55443?query")]
        [InlineData("yeelight://user@192.168.1.239:55443/x")]
        public void TryParse_WhatIsNotALocation_IsRefused_AndLeavesNothingBehind(string? location)
        {
            Assert.False(YeelightLocation.TryParse(location, out var host, out var port));
            Assert.Equal(string.Empty, host);
            Assert.Equal(0, port);
        }

        [Theory]
        [InlineData(" yeelight://192.168.1.239:55443")]
        [InlineData("yeelight://192.168.1.239:55443 ")]
        [InlineData("\tyeelight://192.168.1.239:55443")]
        public void TryParse_WhiteSpaceAroundTheLocation_IsNotIgnored(string location)
        {
            // The announcements trim their header values before they get here: this does not guess at what a caller means.
            Assert.False(YeelightLocation.TryParse(location, out _, out _));
        }

        [Theory]
        [InlineData("yeelight://[fe80::1]:55443", "[fe80::1]", 55443)]
        [InlineData("yeelight://fe80::1:55443", "fe80::1", 55443)]
        [InlineData("yeelight://::1:55443", "::1", 55443)]
        [InlineData("yeelight://[::1]:55443", "[::1]", 55443)]
        public void TryParse_AnIpv6LiteralOrWhatLooksLikeOne_TakesTheLastColonForTheOneOfThePort(string location, string host, int port)
        {
            Assert.True(YeelightLocation.TryParse(location, out var parsedHost, out var parsedPort));
            Assert.Equal(host, parsedHost);
            Assert.Equal(port, parsedPort);
        }

        [Fact]
        public void TryParse_ABracketedIpv6LiteralWithoutAPort_IsRefused()
        {
            Assert.False(YeelightLocation.TryParse("yeelight://[fe80::1]", out var host, out var port));
            Assert.Equal(string.Empty, host);
            Assert.Equal(0, port);
        }

        [Fact]
        public void Format_MakesTheLocationOfAnAddressAndAPort()
        {
            Assert.Equal("yeelight://192.168.1.239:55443", YeelightLocation.Format("192.168.1.239", 55443));
            Assert.Equal("yeelight://bulb.local:1", YeelightLocation.Format("bulb.local", 1));
        }

        [Theory]
        [InlineData("192.168.1.239", 55443)]
        [InlineData("10.0.0.1", 1)]
        [InlineData("10.0.0.1", 65535)]
        [InlineData("bulb.local", 80)]
        [InlineData("::1", 55443)]
        public void Format_ThenTryParse_GivesTheAddressAndThePortBack(string host, int port)
        {
            Assert.True(YeelightLocation.TryParse(YeelightLocation.Format(host, port), out var parsedHost, out var parsedPort));

            Assert.Equal(host, parsedHost);
            Assert.Equal(port, parsedPort);
        }
    }
}
