namespace Yeelight.Tests.Unit
{
    using System;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// Where a connection to a light points, and when it may be pointed elsewhere: a connection that works says where the light is, and an
    /// announcement from another address is not to be believed over it (anyone on the network can announce the id of a light).
    /// </summary>
    public class YeelightConnectionEndpointTests
    {
        /// <summary>A connection that is made, and whose epoch is that of the first connection: a reader that sees it connected can still see the epoch of before.</summary>
        private static async Task<YeelightConnection> ConnectedAsync(int port)
        {
            var connection = await Connections.ConnectedAsync(port);
            await Wait.UntilAsync(() => connection.Epoch == 1, "the epoch of the first connection");
            return connection;
        }
        [Fact]
        public async Task Epoch_IsZeroUntilAConnectionIsMade_AndGrowsByOneForEveryConnectionThatIsMade()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = Connections.To(bulb.ControlPort);
            Assert.Equal(0, connection.Epoch);

            // Both ends are waited for: the light registers a connection a moment after the client sees it, and a drop before that closes nothing.
            connection.Start();
            await Wait.UntilAsync(() => connection.IsConnected && connection.Epoch == 1 && bulb.ClientCount == 1, "the first connection, at both ends");

            bulb.DropConnections();
            await Wait.UntilAsync(() => connection.IsConnected && connection.Epoch == 2 && bulb.ClientCount == 1, "the second connection, at both ends");
            bulb.DropConnections();
            await Wait.UntilAsync(() => connection.IsConnected && connection.Epoch == 3 && bulb.ClientCount == 1, "the third connection, at both ends");

            Assert.Equal(3, bulb.ConnectionCount);
        }

        [Fact]
        public async Task Epoch_StaysWhileTheConnectionLives()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await ConnectedAsync(bulb.ControlPort);

            for (var index = 0; index < 5; index++)
            {
                await connection.SendAsync("get_prop", ["power"]);
            }

            Assert.Equal(1, connection.Epoch);
        }

        [Fact]
        public async Task TryUpdateEndpoint_WhileConnected_LeavesTheConnectionAlone_AndSaysSo()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await ConnectedAsync(bulb.ControlPort);
            var endpoint = connection.Endpoint;

            var moved = connection.TryUpdateEndpoint("127.0.0.1", FreePort.Tcp());

            Assert.False(moved);
            Assert.Equal(endpoint, connection.Endpoint);
            Assert.True(connection.IsConnected);
            Assert.Equal(1, connection.Epoch);
            var answer = await connection.SendAsync("get_prop", ["power"]);
            Assert.True(answer.Success);
            Assert.Equal(1, bulb.ConnectionCount);
        }

        [Fact]
        public async Task TryUpdateEndpoint_ToTheEndpointItHas_IsTrue_EvenWhileConnected_AndChangesNothing()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await ConnectedAsync(bulb.ControlPort);

            var same = connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort);

            Assert.True(same);
            var answer = await connection.SendAsync("get_prop", ["power"]);
            Assert.True(answer.Success);
            Assert.True(connection.IsConnected);
            Assert.Equal(1, connection.Epoch);
            Assert.Equal(1, bulb.ConnectionCount);
        }

        [Fact]
        public async Task TryUpdateEndpoint_WhileNotConnected_MovesTheConnection_AndItIsMadeToTheNewEndpoint()
        {
            await using var bulb = new SimulatedBulb();
            using var dead = new DeadPort();
            await using var connection = Connections.To(dead.Port);
            connection.Start();
            await Wait.NotHappensAsync(() => connection.IsConnected, "a connection to a port where nothing listens", TimeSpan.FromMilliseconds(100));

            var moved = connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort);

            Assert.True(moved);
            Assert.Equal($"127.0.0.1:{bulb.ControlPort}", connection.Endpoint);
            await Wait.UntilAsync(() => connection.IsConnected && connection.Epoch == 1, "the connection to the new endpoint");
            var answer = await connection.SendAsync("get_prop", ["power"]);
            Assert.True(answer.Success);
            Assert.Equal(1, bulb.ConnectionCount);
        }

        [Fact]
        public async Task TryUpdateEndpoint_OfAConnectionThatWasNeverStarted_MovesItWithoutConnecting()
        {
            await using var bulb = new SimulatedBulb();
            using var dead = new DeadPort();
            await using var connection = Connections.To(dead.Port);

            var moved = connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort);

            Assert.True(moved);
            Assert.Equal($"127.0.0.1:{bulb.ControlPort}", connection.Endpoint);
            Assert.False(connection.IsConnected);
            Assert.Equal(0, connection.Epoch);
            await Wait.NotHappensAsync(() => bulb.ConnectionCount > 0, "a connection that was never asked for", TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task TryUpdateEndpoint_OfAConnectionThatIsDisposed_IsFalse_AndMovesNothing()
        {
            await using var bulb = new SimulatedBulb();
            var connection = Connections.To(bulb.ControlPort);
            await connection.DisposeAsync();
            var endpoint = connection.Endpoint;

            Assert.False(connection.TryUpdateEndpoint("127.0.0.1", FreePort.Tcp()));
            Assert.False(connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort));

            Assert.Equal(endpoint, connection.Endpoint);
        }

        [Fact]
        public async Task TryUpdateEndpoint_ANullOrEmptyHost_IsRefused()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = Connections.To(bulb.ControlPort);

            Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => connection.TryUpdateEndpoint(null!, 1)).ParamName);
            Assert.Equal("host", Assert.Throws<ArgumentException>(() => connection.TryUpdateEndpoint(string.Empty, 1)).ParamName);
        }

        [Fact]
        public async Task UpdateEndpoint_WhileConnected_MovesTheConnectionAnyway_AsItAlwaysDid()
        {
            await using var bulb = new SimulatedBulb();
            await using var other = new SimulatedBulb();
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort);

            connection.UpdateEndpoint("127.0.0.1", other.ControlPort);

            await Wait.UntilAsync(() => connection.Epoch == 2 && connection.IsConnected && other.ConnectionCount == 1, "the connection to be made again, to the new endpoint");
            Assert.Equal($"127.0.0.1:{other.ControlPort}", connection.Endpoint);
        }
    }
}
