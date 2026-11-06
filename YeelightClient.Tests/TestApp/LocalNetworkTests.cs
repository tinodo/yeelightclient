namespace Yeelight.Tests.TestApp
{
    using System.Net;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The address of this computer that a light in music mode connects to.</summary>
    public sealed class LocalNetworkTests
    {
        [Fact]
        public void ForTheLoopback_ItIsTheLoopback()
        {
            Assert.Equal(IPAddress.Loopback, LocalNetwork.LocalAddressFor(IPAddress.Loopback));
        }

        [Fact]
        public void ForAnAddressInTheFormOfAnIpv6Address_ItIsStillAnIpv4Address()
        {
            var local = LocalNetwork.LocalAddressFor(IPAddress.Loopback.MapToIPv6());

            Assert.Equal(IPAddress.Loopback, local);
            Assert.False(local!.IsIPv4MappedToIPv6);
        }

        [Fact]
        public void ForAPrivateAddress_ItIsAnAddressOfThisComputer_AndNothingIsSent()
        {
            // A connected UDP socket sends nothing: the address is found without a packet leaving this computer. The answer depends on
            // the networks that the computer is on, so all that is asked of it is that it is an IPv4 address when there is one.
            var local = LocalNetwork.LocalAddressFor(IPAddress.Parse("192.168.1.50"));

            Assert.True(local is null || local.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        }

        [Theory]
        [InlineData("192.168.1.50", "192.168.1.50", 1982)]
        [InlineData("192.168.1.50:55000", "192.168.1.50", 55000)]
        [InlineData("  127.0.0.1:1  ", "127.0.0.1", 1)]
        [InlineData("127.0.0.1:65535", "127.0.0.1", 65535)]
        [InlineData("::1", "::1", 1982)]
        [InlineData("[::1]", "::1", 1982)]
        [InlineData("[::1]:55000", "::1", 55000)]
        [InlineData("fe80::1234", "fe80::1234", 1982)]
        public void AnAddressThatIsTyped_IsReadWithItsPort(string text, string address, int port)
        {
            Assert.True(LocalNetwork.TryParseEndpoint(text, 1982, out var parsed, out var parsedPort, out var error), error);

            Assert.Equal(IPAddress.Parse(address), parsed);
            Assert.Equal(port, parsedPort);
            Assert.Empty(error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("lamp.local")]
        [InlineData("192.168.1.50:")]
        [InlineData("192.168.1.50:0")]
        [InlineData("192.168.1.50:65536")]
        [InlineData("192.168.1.50:abc")]
        [InlineData("192.168.1.50:-5")]
        [InlineData("1.2.3.4:5:6")]
        [InlineData("[::1")]
        [InlineData("[::1]x")]
        [InlineData("[::1]:")]
        [InlineData("[lamp]:55000")]
        public void AnAddressThatIsNoAddress_IsRefusedWithAnExplanation(string? text)
        {
            Assert.False(LocalNetwork.TryParseEndpoint(text, 1982, out var parsed, out _, out var error));

            Assert.Null(parsed);
            Assert.NotEmpty(error);
        }
    }
}
