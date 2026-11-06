namespace Yeelight.Tests.Regression
{
    using System;
    using System.Diagnostics;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What a dispose leaves undone, found by the stress tests: callers that wait for a connection or for their turn were kept waiting for
    /// the time that they were willing to, and a loop of the discovery that began after its sockets were gone failed in a task that nobody
    /// looked at.
    /// </summary>
    public class DisposeRegressionTests
    {
        // Much more than any test waits: a command that is still waiting after the dispose waits for this.
        private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(60);

        [Fact]
        public async Task ACommandThatWaitsForAConnection_EndsAtOnce_WhenTheConnectionIsDisposed()
        {
            using var dead = new DeadPort();
            var options = Connections.Fast(connectionWait: LongWait, connectTimeout: TimeSpan.FromSeconds(1));
            await using var connection = Connections.To(dead.Port, options);
            var sending = connection.SendAsync("get_prop", ["power"]);
            await Task.Delay(150);
            Assert.False(sending.IsCompleted, "The command did not wait for a connection that is not there.");

            await connection.DisposeAsync();

            var result = await sending.WithTimeoutAsync("the command that waited for a connection to end", TimeSpan.FromSeconds(5));
            Assert.Equal(YeelightFailureKind.NotConnected, result.Error?.Kind);
        }

        [Fact]
        public async Task ACommandThatWaitsForItsTurn_EndsAtOnce_WhenTheConnectionIsDisposed()
        {
            await using var bulb = new SimulatedBulb();

            // One command at once, then one every twelve seconds: the second one waits for its turn.
            var options = Connections.Fast(perMinute: 6, burst: 1, rateLimitWait: LongWait, connectionWait: TimeSpan.FromSeconds(5));
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort, options);
            var first = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync("the first command");
            Assert.True(first.Success, $"The first command did not succeed: {first.Error}.");
            var second = connection.SendAsync("get_prop", ["power"]);
            await Task.Delay(150);
            Assert.False(second.IsCompleted, "The second command did not wait for its turn.");

            await connection.DisposeAsync();

            var result = await second.WithTimeoutAsync("the command that waited for its turn to end", TimeSpan.FromSeconds(5));
            Assert.Equal(YeelightFailureKind.NotConnected, result.Error?.Kind);
        }

        [Fact]
        public async Task AWaitForTheConnection_EndsAtOnce_WhenTheConnectionIsDisposed()
        {
            using var dead = new DeadPort();
            await using var connection = Connections.To(dead.Port, Connections.Fast(connectTimeout: TimeSpan.FromSeconds(1)));
            var waiting = connection.WaitConnectedAsync(LongWait);
            await Task.Delay(150);
            Assert.False(waiting.IsCompleted, $"The wait ended with no connection and no dispose (its status is {waiting.Status}).");

            await connection.DisposeAsync();

            Assert.False(await waiting.WithTimeoutAsync("the wait for the connection to end", TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task ACancelledCommand_IsStillCancelled_AndNotReportedAsAClosedConnection()
        {
            using var dead = new DeadPort();
            await using var connection = Connections.To(dead.Port, Connections.Fast(connectionWait: LongWait, connectTimeout: TimeSpan.FromSeconds(1)));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.SendAsync("get_prop", ["power"], cancel.Token));
        }

        [Fact]
        public async Task ACommandOfALightThatCannotBeReached_EndsAtOnce_WhenTheClientIsDisposed()
        {
            using var dead = new DeadPort();
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { ConnectTimeout = TimeSpan.FromSeconds(1) });
            var device = client.Apply(Announcements.From(bulb, Announcements.LocationOf(dead.Port))) ?? throw new InvalidOperationException("The client did not take the light.");
            var sending = device.SetBrightnessAsync(50);
            await Task.Delay(200);
            Assert.False(sending.IsCompleted, "The command did not wait for a connection that is not there.");

            var watch = Stopwatch.StartNew();
            await client.DisposeAsync();

            // The client waits two seconds for a connection: a command that is let wait for the rest of them is not ended by the dispose.
            var failure = await Assert.ThrowsAsync<YeelightException>(() => sending.WithTimeoutAsync("the command to end", TimeSpan.FromSeconds(5)));
            Assert.Equal(YeelightFailureKind.NotConnected, failure.Kind);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.2), $"The command ended {watch.Elapsed.TotalSeconds:0.0} seconds after the dispose.");
        }

        [Fact]
        public void AGenerationOfSockets_StillHasATokenAfterItWasDisposed_ForALoopThatBeginsLate()
        {
            var type = typeof(YeelightDiscovery).GetNestedType("Generation", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("YeelightDiscovery has no type 'Generation' any more: update this test.");
            using var parent = new CancellationTokenSource();
            var generation = (IDisposable)Activator.CreateInstance(type, parent.Token)!;
            var token = type.GetProperty("Token")!;

            generation.Dispose();

            Assert.True(((CancellationToken)token.GetValue(generation)!).IsCancellationRequested);
            generation.Dispose();
        }
    }
}
