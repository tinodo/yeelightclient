namespace Yeelight.Tests.Unit
{
    using System.Net.Sockets;
    using Xunit;

    /// <summary>
    /// The socket of a connection: a light is an IPv4 device, so a light that is given by its address gets an IPv4 socket. The socket that
    /// <see cref="TcpClient"/> makes by default takes both IPv4 and IPv6 and connects to an IPv4 address through its IPv6 side, which software that
    /// watches IPv6 makes about ten times slower on some computers.
    /// </summary>
    public class YeelightConnectionSocketTests
    {
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("192.168.1.50")]
        [InlineData("10.0.0.7")]
        [InlineData("169.254.12.34")]
        public void ALightThatIsGivenByAnIpv4Address_GetsAnIpv4Socket(string host)
        {
            using var client = YeelightConnection.CreateClient(host);

            Assert.Equal(AddressFamily.InterNetwork, client.Client.AddressFamily);
            Assert.True(client.NoDelay);
        }

        [Theory]
        [InlineData("localhost")]
        [InlineData("bulb.local")]
        [InlineData("::1")]
        [InlineData("::ffff:192.168.1.50")]
        public void AHostThatIsNotAnIpv4Address_KeepsTheSocketThatTakesBothFamilies(string host)
        {
            using var client = YeelightConnection.CreateClient(host);

            // On a computer without IPv6 (or with IPv6 switched off for the process) every socket is an IPv4 one.
            var expected = Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            Assert.Equal(expected, client.Client.AddressFamily);
            Assert.True(client.NoDelay);
        }
    }
}
