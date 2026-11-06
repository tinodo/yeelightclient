namespace Yeelight.Tests.Chaos
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit.Abstractions;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A connection that connects again after a few milliseconds, to a light behind a proxy that cuts, stalls and refuses all the time: it
    /// makes (and ends) hundreds of connections in the few seconds of a run, thousands in a long one, while three tasks send commands.
    /// What a connection that is lost must give back (the socket, the thread, the memory) is checked by what the process holds before and
    /// after, and by what the light still has open when the connection is disposed. Runs for <c>YEELIGHT_SOAK_SECONDS</c> (five by default).
    /// </summary>
    [Collection(SoakCollection.Name)]
    [Trait("Category", "Soak")]
    public class ConnectionChurnTests(ITestOutputHelper output)
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(800);

        private static readonly TimeSpan ConnectionWait = TimeSpan.FromSeconds(1);

        // The longest that a call may take: the wait for a connection, the wait for the answer, and a busy machine.
        private static readonly TimeSpan Longest = ConnectionWait + CommandTimeout + TimeSpan.FromSeconds(6);

        [Fact]
        public async Task ConnectionsThatAreCutRefusedAndStalledAllTheTime_AreEachClosed_AndNothingIsLeftBehind()
        {
            using var unobserved = new UnobservedExceptions();
            await this.RunAsync(TimeSpan.FromSeconds(1));
            var before = ResourceSnapshot.Take();

            var result = await this.RunAsync(SoakSettings.Duration);

            var after = ResourceSnapshot.Take();
            output.WriteLine($"Churn: {SoakSettings.Description}.");
            output.WriteLine(result.Describe());
            output.WriteLine($"Before: {before}. After: {after}.");
            Assert.True(result.Problems.Count == 0, "Problems:\n" + string.Join("\n", result.Problems.Take(10)) + $"\n(Churn: {SoakSettings.Description}.)");
            Assert.True(result.Calls["success"] > 0, $"No command succeeded in the whole run: {result.Calls}.");
            Assert.True(result.Connections >= 10, $"The proxy accepted {result.Connections} connections only: the chaos is not what it should be.");
            Assert.True(result.MaxCallTime < Longest, $"A call took {result.MaxCallTime.TotalSeconds:0.0} seconds, and none may take {Longest.TotalSeconds:0.#}.");
            unobserved.AssertNone();
            SoakTests.AssertGivenBack(before, after);
        }

        private async Task<ChurnResult> RunAsync(TimeSpan duration)
        {
            var problems = new ConcurrentQueue<string>();
            var calls = new Tally();
            var longest = new LongestTime();

            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { CommandsPerMinute = 6_000_000, MaxConnections = 16 });
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            var options = Connections.Fast(
                commandTimeout: CommandTimeout,
                connectTimeout: TimeSpan.FromSeconds(1),
                reconnectMin: TimeSpan.FromMilliseconds(1),
                reconnectMax: TimeSpan.FromMilliseconds(20),
                connectionWait: ConnectionWait);
            await using var connection = Connections.To(proxy.Port, options);
            var transitions = new ConcurrentQueue<bool>();
            connection.ConnectedChanged += transitions.Enqueue;
            connection.Start();
            await Wait.UntilAsync(() => connection.IsConnected, "the connection before the chaos");

            using var stop = new CancellationTokenSource();
            var chaosOver = false;
            var afterChaos = new HitCounter();

            Task Worker(int number) => Task.Run(async () =>
            {
                var turn = number;
                while (!stop.IsCancellationRequested)
                {
                    var read = (turn++ % 3) != 0;
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var result = read
                            ? await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync("a read", TimeSpan.FromSeconds(20))
                            : await connection.SendAsync("set_bright", [(turn % 100) + 1, "sudden", 0]).WithTimeoutAsync("a command", TimeSpan.FromSeconds(20));
                        if (result.Success)
                        {
                            // The answer is the answer of its own command: a read says "on", a command says "ok".
                            var expected = read ? "on" : "ok";
                            if (result.GetString(0) != expected)
                            {
                                problems.Enqueue($"The answer of {(read ? "get_prop" : "set_bright")} was {result.GetString(0)}, and it is {expected}.");
                            }

                            calls.Add("success");
                            if (Volatile.Read(ref chaosOver))
                            {
                                afterChaos.Increment();
                            }
                        }
                        else if (result.Error is { Kind: YeelightFailureKind.NotConnected or YeelightFailureKind.ConnectionLost or YeelightFailureKind.Timeout })
                        {
                            calls.Add(result.Error.Kind.ToString());
                        }
                        else
                        {
                            problems.Enqueue($"A command ended with {result.Error}.");
                        }
                    }
                    catch (Exception exception)
                    {
                        problems.Enqueue($"A call failed with {exception.GetType().Name}: {exception.Message}");
                    }

                    longest.Add(watch.Elapsed);
                    await Task.Delay(1).ConfigureAwait(false);
                }
            });

            var workers = new[] { Worker(0), Worker(1), Worker(2) };
            await using var chaos = new ChaosDriver(proxy, SoakSettings.Seed, ChaosStyle.Fierce());
            chaos.Start();
            await Task.Delay(duration);
            await chaos.StopAsync();
            Volatile.Write(ref chaosOver, true);

            var recovery = Stopwatch.StartNew();
            while ((!connection.IsConnected || afterChaos.Value < 5) && recovery.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(20);
            }

            if (!connection.IsConnected || afterChaos.Value < 5)
            {
                problems.Enqueue($"{afterChaos.Value} commands succeeded in the {recovery.Elapsed.TotalSeconds:0} seconds after the chaos stopped, and the connection is {(connection.IsConnected ? "up" : "down")}.");
            }

            stop.Cancel();
            try
            {
                await Task.WhenAll(workers).WithTimeoutAsync("the tasks that send commands to end", TimeSpan.FromSeconds(30));
            }
            catch (Exception exception)
            {
                problems.Enqueue($"The tasks that send commands did not end well: {exception.Message}");
            }

            // The connection is reported when it is made and when it is lost, one after the other, starting with the first.
            var changes = transitions.ToArray();
            if (changes.Length == 0 || !changes[0])
            {
                problems.Enqueue("The first thing that was reported of the connection is not that it was made.");
            }

            for (var index = 1; index < changes.Length; index++)
            {
                if (changes[index] == changes[index - 1])
                {
                    problems.Enqueue($"The connection was reported as {(changes[index] ? "made" : "lost")} twice in a row (reports {index} and {index + 1} of {changes.Length}).");
                    break;
                }
            }

            // No command had a number that another had: the light would take the answer of one for the answer of another.
            var requests = bulb.Requests;
            var repeated = requests.GroupBy(request => request.Id).Count(group => group.Count() > 1);
            if (repeated > 0)
            {
                problems.Enqueue($"{repeated} numbers of commands were used more than once on the way to the light.");
            }

            var cuts = chaos.Cuts;
            var stalls = chaos.Stalls;
            var refusals = chaos.Refusals;
            var accepted = proxy.Accepted;
            var made = changes.Count(change => change);

            await connection.DisposeAsync();
            try
            {
                await Wait.UntilAsync(() => bulb.ClientCount == 0 && proxy.Open == 0, "every connection to be closed", TimeSpan.FromSeconds(15));
            }
            catch (Xunit.Sdk.XunitException)
            {
                problems.Enqueue($"After the connection was disposed the light still has {bulb.ClientCount} connections open, and the proxy {proxy.Open}.");
            }

            return new ChurnResult(calls, longest.Value, problems.ToList(), cuts, stalls, refusals, accepted, made, requests.Count);
        }

        private sealed record ChurnResult(Tally Calls, TimeSpan MaxCallTime, List<string> Problems, int Cuts, int Stalls, int Refusals, int Connections, int ConnectionsMade, int Requests)
        {
            public string Describe() => $"{this.Calls.Total} calls ({this.Calls}); the longest took {this.MaxCallTime.TotalSeconds:0.00} s; the chaos: {this.Cuts} cuts, {this.Stalls} stalls, {this.Refusals} refusals; the proxy accepted {this.Connections} connections, {this.ConnectionsMade} were made and reported by the client, {this.Requests} commands reached the light.";
        }
    }
}
