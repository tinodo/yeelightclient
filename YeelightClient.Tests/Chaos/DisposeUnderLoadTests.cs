namespace Yeelight.Tests.Chaos
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A connection, and a client, that are disposed at every point of the work and of the trouble: commands are on the way, the network is cut,
    /// stalled and refused all the time, and the dispose comes after a time that is different in every round. What a dispose has to do is
    /// the same wherever it falls: every call ends in time (with the failure that says the connection is closed, or was lost), nothing that
    /// was opened stays open, and no task fails with nobody to see it. They run alone (like the soak tests), because the exceptions of tasks
    /// that nobody looked at are collected for the whole process.
    /// </summary>
    [Collection(SoakCollection.Name)]
    public class DisposeUnderLoadTests
    {
        private const int Rounds = 40;

        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(800);

        [Fact]
        public async Task AConnectionThatIsDisposedAtAnyPointOfTheChaos_EndsEveryCall_AndLeavesNothingBehind()
        {
            using var unobserved = new UnobservedExceptions();
            var problems = new ConcurrentQueue<string>();
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { CommandsPerMinute = 6_000_000, MaxConnections = 64 });
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var chaos = new ChaosDriver(proxy, SoakSettings.Seed, ChaosStyle.Fierce());
            chaos.Start();
            var random = new Random(SoakSettings.Seed);

            for (var round = 0; round < Rounds; round++)
            {
                var options = Connections.Fast(
                    commandTimeout: CommandTimeout,
                    keepAlive: round % 2 == 0 ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero,
                    connectTimeout: TimeSpan.FromSeconds(1),
                    reconnectMin: TimeSpan.FromMilliseconds(1),
                    reconnectMax: TimeSpan.FromMilliseconds(20),
                    connectionWait: TimeSpan.FromSeconds(1));
                var connection = Connections.To(proxy.Port, options);
                using var stop = new CancellationTokenSource();
                var callers = Enumerable.Range(0, 3).Select(number => Task.Run(async () =>
                {
                    var turn = number;
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await connection.SendAsync(turn++ % 3 == 0 ? "get_prop" : "set_bright", turn % 3 == 1 ? ["power"] : [(turn % 100) + 1, "sudden", 0])
                                .WithTimeoutAsync("a command", TimeSpan.FromSeconds(20));
                            if (!result.Success && result.Error is not { Kind: YeelightFailureKind.NotConnected or YeelightFailureKind.ConnectionLost or YeelightFailureKind.Timeout })
                            {
                                problems.Enqueue($"A command ended with {result.Error}.");
                            }
                        }
                        catch (Exception exception)
                        {
                            problems.Enqueue($"A call failed with {exception.GetType().Name}: {exception.Message}");
                        }

                        await Task.Delay(1).ConfigureAwait(false);
                    }
                })).ToArray();

                await Task.Delay(random.Next(0, 250));
                await connection.DisposeAsync().AsTask().WithTimeoutAsync("the connection to be disposed", TimeSpan.FromSeconds(15));

                // A connection that is disposed answers every command at once, and does not connect again.
                var afterwards = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync("a command after the dispose", TimeSpan.FromSeconds(5));
                if (afterwards.Error is not { Kind: YeelightFailureKind.NotConnected })
                {
                    problems.Enqueue($"A command after the dispose ended with {afterwards.Error?.ToString() ?? "success"}.");
                }

                stop.Cancel();
                await Task.WhenAll(callers).WithTimeoutAsync("the callers to end", TimeSpan.FromSeconds(30));
                Assert.True(!connection.IsConnected, "A connection that was disposed says that it is connected.");
            }

            await chaos.StopAsync();
            await AssertNothingLeftOpenAsync(bulb, proxy, problems);
            AssertNoProblems(problems);
            unobserved.AssertNone();
        }

        [Fact]
        public async Task AClientThatIsDisposedAtAnyPointOfTheChaos_EndsEveryCall_AndLeavesNothingBehind()
        {
            using var unobserved = new UnobservedExceptions();
            var problems = new ConcurrentQueue<string>();
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { CommandsPerMinute = 6_000_000, MaxConnections = 64 });
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            await using var chaos = new ChaosDriver(proxy, SoakSettings.Seed + 1, ChaosStyle.Fierce());
            chaos.Start();
            var random = new Random(SoakSettings.Seed + 1);
            var location = Announcements.LocationOf(proxy.Port);

            for (var round = 0; round < Rounds; round++)
            {
                var options = new YeelightClientOptions
                {
                    CommandTimeout = CommandTimeout,
                    ConnectTimeout = TimeSpan.FromSeconds(1),
                    KeepAliveInterval = round % 2 == 0 ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(30),
                    CommandsPerMinute = 6_000_000,
                };
                var client = TestClients.Create(options);
                var changes = 0;
                client.DeviceStateChanged += (_, _) => Interlocked.Increment(ref changes);
                var device = client.Apply(Announcements.From(bulb, location)) ?? throw new InvalidOperationException("The client did not take the light.");
                using var stop = new CancellationTokenSource();
                var callers = Enumerable.Range(0, 3).Select(number => Task.Run(async () =>
                {
                    var turn = number;
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            if (turn++ % 3 == 0)
                            {
                                await device.GetPropertiesAsync(["power"]).WithTimeoutAsync("a read", TimeSpan.FromSeconds(20));
                            }
                            else
                            {
                                await device.SetBrightnessAsync((turn % 100) + 1).WithTimeoutAsync("a command", TimeSpan.FromSeconds(20));
                            }
                        }
                        catch (YeelightException exception) when (exception.Error is { IsTransportFailure: true })
                        {
                            // The network, or the end of the client: what a call is told in either case.
                        }
                        catch (Exception exception)
                        {
                            problems.Enqueue($"A call failed with {exception.GetType().Name}: {exception.Message}");
                        }

                        await Task.Delay(1).ConfigureAwait(false);
                    }
                })).ToArray();

                await Task.Delay(random.Next(0, 250));
                await client.DisposeAsync().AsTask().WithTimeoutAsync("the client to be disposed", TimeSpan.FromSeconds(15));

                // A handler that was running when the client was disposed is let finish; after that nothing is raised.
                await Task.Delay(50);
                var raised = Volatile.Read(ref changes);
                await Task.Delay(50);
                if (Volatile.Read(ref changes) != raised)
                {
                    problems.Enqueue("A client that was disposed went on raising events.");
                }

                stop.Cancel();
                await Task.WhenAll(callers).WithTimeoutAsync("the callers to end", TimeSpan.FromSeconds(30));
            }

            await chaos.StopAsync();
            await AssertNothingLeftOpenAsync(bulb, proxy, problems);
            AssertNoProblems(problems);
            unobserved.AssertNone();
        }

        private static async Task AssertNothingLeftOpenAsync(SimulatedBulb bulb, ChaosProxy proxy, ConcurrentQueue<string> problems)
        {
            try
            {
                await Wait.UntilAsync(() => bulb.ClientCount == 0 && proxy.Open == 0, "every connection to be closed", TimeSpan.FromSeconds(15));
            }
            catch (Xunit.Sdk.XunitException)
            {
                problems.Enqueue($"After every connection was disposed the light still has {bulb.ClientCount} connections open, and the proxy {proxy.Open}.");
            }
        }

        private static void AssertNoProblems(ConcurrentQueue<string> problems)
        {
            Assert.True(problems.IsEmpty, "Problems:\n" + string.Join("\n", problems.Take(10)));
        }
    }
}
