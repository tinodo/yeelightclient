namespace Yeelight.Tests.Chaos
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The proxy that makes a connection go wrong (it cuts, stalls and refuses), with a simulated light behind it: what it does has to be what
    /// a network does, or the tests that use it prove nothing. A command that goes through it is answered, a cut is a reset that both ends
    /// see, a stall holds everything back and lets it all through afterwards, and a refusal resets new connections until it is lifted.
    /// </summary>
    [Trait("Category", "Chaos")]
    public class ChaosProxyTests
    {
        // What a light takes as a command to change the brightness: the brightness, and how (at once), as the specification has it.
        private static object?[] Brightness(int value) => [value, "sudden", 0];

        [Fact]
        public async Task AProxy_PassesWhatIsSent_BothWays()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port);
            var reported = new EventRecorder<string>();
            connection.PropsReceived += properties => reported.Add(string.Join(",", properties.Select(pair => pair.Key + "=" + pair.Value)));
            await Wait.UntilAsync(() => bulb.ClientCount >= 1, "the light to have accepted the connection");

            var result = await connection.SendAsync("set_bright", Brightness(42)).WithTimeoutAsync("the answer");
            await bulb.ChangeAsync(("bright", "7"));

            Assert.True(result.Success);
            Assert.Equal("7", bulb.Get("bright"));
            await reported.WaitUntilAsync(items => items.Any(item => item.Contains("bright=7", StringComparison.Ordinal)), "the report of the light");
            Assert.Equal(1, proxy.Accepted);
        }

        [Fact]
        public async Task ACut_ResetsTheConnectionAtBothEnds_AndTheClientConnectsAgain()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port);
            await Wait.UntilAsync(() => bulb.ClientCount == 1 && proxy.Open == 1, "the connection to be passed on to the light");

            var cut = proxy.Cut();

            Assert.True(cut >= 1);
            Assert.Equal(cut, proxy.ConnectionsCut);
            await Wait.UntilAsync(() => bulb.ConnectionCount >= 2 && connection.IsConnected, "the client to connect again");
            var result = await connection.SendAsync("set_bright", Brightness(11)).WithTimeoutAsync("the answer after the cut");
            Assert.True(result.Success);
            Assert.Equal("11", bulb.Get("bright"));
            await Wait.UntilAsync(() => bulb.ClientCount == 1, "the light to have forgotten the connection that was cut");
        }

        [Fact]
        public async Task ACut_WhileACommandWaits_EndsTheCommandAtOnce_AsALostConnection()
        {
            await using var bulb = new SimulatedBulb { Delay = _ => TimeSpan.FromSeconds(30) };
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port, Connections.Fast(commandTimeout: TimeSpan.FromSeconds(20)));
            var waiting = connection.SendAsync("set_bright", Brightness(5));
            await Wait.UntilAsync(() => bulb.Requests.Count == 1, "the command to arrive at the light");

            var watch = Stopwatch.StartNew();
            proxy.Cut();
            var result = await waiting.WithTimeoutAsync("the command to end");

            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.ConnectionLost, result.Error!.Kind);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"The command ended {watch.Elapsed.TotalSeconds:0.0} seconds after the cut.");
        }

        [Fact]
        public async Task AStall_HoldsBackTheAnswer_AndLetsItThroughWhenItIsOver()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port, Connections.Fast(commandTimeout: TimeSpan.FromSeconds(10)));
            var watch = Stopwatch.StartNew();

            proxy.Stall(TimeSpan.FromMilliseconds(600));
            var waiting = connection.SendAsync("set_bright", Brightness(33));
            await Task.Delay(250);
            Assert.False(waiting.IsCompleted, "The answer came while the proxy held everything back.");
            var result = await waiting.WithTimeoutAsync("the answer after the stall");

            Assert.True(result.Success);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(450), $"The answer came after {watch.ElapsedMilliseconds} ms, and the stall was 600 ms.");
            Assert.Equal("33", bulb.Get("bright"));
        }

        [Fact]
        public async Task EndStall_LetsEverythingThroughAtOnce()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port, Connections.Fast(commandTimeout: TimeSpan.FromSeconds(20)));

            proxy.Stall(TimeSpan.FromSeconds(30));
            var waiting = connection.SendAsync("set_bright", Brightness(34));
            await Task.Delay(200);
            var watch = Stopwatch.StartNew();
            proxy.EndStall();
            var result = await waiting.WithTimeoutAsync("the answer after the end of the stall");

            Assert.True(result.Success);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"The answer took {watch.Elapsed.TotalSeconds:0.0} seconds after the stall was ended.");
        }

        [Fact]
        public async Task AStallThatIsLongerThanTheTimeout_TimesTheCommandOut_AndTheConnectionStaysUsable()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port, Connections.Fast(commandTimeout: TimeSpan.FromMilliseconds(300)));

            proxy.Stall(TimeSpan.FromMilliseconds(900));
            var late = await connection.SendAsync("set_bright", Brightness(35)).WithTimeoutAsync("the command to time out");
            await Wait.UntilAsync(() => bulb.Get("bright") == "35", "the command that was held back to arrive at the light");
            var next = await connection.SendAsync("set_bright", Brightness(36)).WithTimeoutAsync("the command after the stall");

            Assert.False(late.Success);
            Assert.Equal(YeelightFailureKind.Timeout, late.Error!.Kind);
            Assert.True(next.Success, "The connection is not usable after the answer of a command that timed out came late.");
            Assert.Equal("36", bulb.Get("bright"));
            Assert.Equal(1, proxy.Accepted);
        }

        [Fact]
        public async Task ARefusal_ResetsNewConnectionsAtOnce_AndTheClientKeepsTrying_UntilItIsLifted()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port);
            await Wait.UntilAsync(() => proxy.Open == 1, "the connection to be passed on to the light");

            proxy.Refuse(true);
            proxy.Cut();
            await Wait.UntilAsync(() => !connection.IsConnected, "the connection to be lost");
            var accepted = proxy.Accepted;
            await Wait.UntilAsync(() => proxy.Accepted >= accepted + 5, "the client to try again and again");

            // The proxy accepts and resets (the way a light that is starting does): the client can be connected for a moment, but no connection gets to the light.
            Assert.Equal(1, bulb.ConnectionCount);
            proxy.Refuse(false);
            await Wait.UntilAsync(() => bulb.ConnectionCount >= 2 && connection.IsConnected, "the connection to the light when the refusal was lifted");
            var result = await connection.SendAsync("set_bright", Brightness(37)).WithTimeoutAsync("the answer");
            Assert.True(result.Success);
        }

        [Fact]
        public async Task TheEndOfTheLight_IsPassedOn_AsTheEndOfTheConnection()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port);
            await Wait.UntilAsync(() => bulb.ClientCount == 1, "the light to have accepted the connection");
            var changes = new EventRecorder<bool>();
            connection.ConnectedChanged += changes.Add;

            bulb.DropConnections();

            await changes.WaitUntilAsync(items => items.Count >= 2 && !items[0] && items[1], "the loss of the connection, and the new one");
            await Wait.UntilAsync(() => proxy.Accepted >= 2, "the proxy to have counted the second connection");
        }

        [Fact]
        public async Task DisposingTheProxy_ClosesEveryConnection_AndRefusesTheOnesThatFollow()
        {
            await using var bulb = new SimulatedBulb();
            var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var connection = await Connections.ConnectedAsync(proxy.Port);
            await Wait.UntilAsync(() => bulb.ClientCount == 1, "the light to have accepted the connection");

            await proxy.DisposeAsync();

            await Wait.UntilAsync(() => !connection.IsConnected, "the connection to be closed");
            await Wait.UntilAsync(() => bulb.ClientCount == 0 && proxy.Open == 0, "nothing to be left open");
            await Wait.NotHappensAsync(() => connection.IsConnected, "a connection to a proxy that is gone");
        }

        [Fact]
        public async Task ManyConnectionsAtOnce_AreEachPassedOn()
        {
            await using var bulb = new SimulatedBulb();
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            var connections = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Connections.ConnectedAsync(proxy.Port)));
            try
            {
                var results = await Task.WhenAll(connections.Select((connection, index) => connection.SendAsync("set_bright", Brightness(10 + index)))).WithTimeoutAsync("the answers");

                Assert.All(results, result => Assert.True(result.Success));
                Assert.Equal(3, proxy.Open);
                await Wait.UntilAsync(() => bulb.ClientCount == 3, "the light to have three connections");
            }
            finally
            {
                foreach (var connection in connections)
                {
                    await connection.DisposeAsync();
                }
            }

            await Wait.UntilAsync(() => proxy.Open == 0 && bulb.ClientCount == 0, "every connection to be closed");
        }
    }
}
