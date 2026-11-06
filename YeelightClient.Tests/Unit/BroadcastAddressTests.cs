namespace Yeelight.Tests.Unit
{
    using System;
    using System.Buffers.Binary;
    using System.Net;
    using System.Net.Sockets;
    using Xunit;

    /// <summary>
    /// The broadcast address of the network that an address of this computer is on: where the search of a light is sent as a broadcast, for
    /// the lights that never hear the multicast group.
    /// </summary>
    public class BroadcastAddressTests
    {
        private static IPAddress? Of(string address, string mask)
        {
            return YeelightDiscovery.BroadcastAddress(IPAddress.Parse(address), IPAddress.Parse(mask));
        }

        [Theory]
        [InlineData("192.168.1.157", "255.255.255.0", "192.168.1.255")]
        [InlineData("192.168.1.0", "255.255.255.0", "192.168.1.255")]
        [InlineData("192.168.1.255", "255.255.255.0", "192.168.1.255")]
        [InlineData("10.1.2.3", "255.0.0.0", "10.255.255.255")]
        [InlineData("172.16.5.9", "255.240.0.0", "172.31.255.255")]
        [InlineData("192.168.1.5", "255.255.255.128", "192.168.1.127")]
        [InlineData("192.168.1.200", "255.255.255.128", "192.168.1.255")]
        [InlineData("192.168.1.5", "255.255.255.252", "192.168.1.7")]
        [InlineData("10.20.30.40", "255.255.0.0", "10.20.255.255")]
        [InlineData("100.64.1.1", "255.192.0.0", "100.127.255.255")]
        [InlineData("169.254.3.4", "255.255.0.0", "169.254.255.255")]
        [InlineData("1.2.3.4", "0.0.0.0", "255.255.255.255")]
        public void TheBroadcastAddress_IsTheAddressWithAllTheBitsOfTheHostSet(string address, string mask, string expected)
        {
            Assert.Equal(IPAddress.Parse(expected), Of(address, mask));
        }

        [Theory]
        [InlineData("192.168.1.5", "255.255.255.254")]
        [InlineData("192.168.1.5", "255.255.255.255")]
        public void ANetworkOfOneOrTwoAddresses_HasNoBroadcastAddress(string address, string mask)
        {
            Assert.Null(Of(address, mask));
        }

        [Theory]
        [InlineData("::1", "255.255.255.0")]
        [InlineData("192.168.1.5", "ffff:ffff::")]
        [InlineData("fe80::1", "ffff:ffff:ffff:ffff::")]
        public void AnAddressOrAMaskThatIsNotIpv4_HasNoBroadcastAddress(string address, string mask)
        {
            Assert.Null(Of(address, mask));
        }

        [Fact]
        public void NullArguments_AreRefused()
        {
            Assert.Equal("address", Assert.Throws<ArgumentNullException>(() => YeelightDiscovery.BroadcastAddress(null!, IPAddress.Any)).ParamName);
            Assert.Equal("mask", Assert.Throws<ArgumentNullException>(() => YeelightDiscovery.BroadcastAddress(IPAddress.Loopback, null!)).ParamName);
        }

        [Fact]
        public void ForEveryPrefixLength_TheBroadcastAddressIsTheLastAddressOfTheNetwork()
        {
            var random = new Random(1982);
            for (var round = 0; round < 2000; round++)
            {
                var bytes = new byte[4];
                random.NextBytes(bytes);
                var prefix = random.Next(0, 31);
                var maskBits = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
                var address = new IPAddress(bytes);
                var mask = FromBits(maskBits);
                var networkBase = FromBits(BinaryPrimitives.ReadUInt32BigEndian(bytes) & maskBits);

                var broadcast = YeelightDiscovery.BroadcastAddress(address, mask);

                Assert.NotNull(broadcast);
                var network = new IPNetwork(networkBase, prefix);
                var message = $"{address} with {mask} is in {network}, which does not end at {broadcast}.";
                Assert.Equal(AddressFamily.InterNetwork, broadcast.AddressFamily);
                Assert.True(network.Contains(broadcast), message);

                // It is the last address of the network: the next one is of another network.
                var bits = BinaryPrimitives.ReadUInt32BigEndian(broadcast.GetAddressBytes());
                if (bits != uint.MaxValue)
                {
                    Assert.False(network.Contains(FromBits(bits + 1)), message);
                }

                // Every address of the network has the same one.
                Assert.Equal(broadcast, YeelightDiscovery.BroadcastAddress(networkBase, mask));
                Assert.Equal(broadcast, YeelightDiscovery.BroadcastAddress(broadcast, mask));
            }
        }

        private static IPAddress FromBits(uint bits)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, bits);
            return new IPAddress(bytes);
        }
    }
}
