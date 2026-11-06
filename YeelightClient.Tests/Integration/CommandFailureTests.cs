namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// Every way in which a command can fail, and what the caller is told: a light that refuses (a failure of the light), a light that does not
    /// answer, a connection that is lost while a command waits, a light that is not there, and the limits on the number of commands (of the
    /// client, and of the light). The client and the connection are fine after every one of them.
    /// </summary>
    public class CommandFailureTests
    {
        private static YeelightClientOptions Options(int port, TimeSpan? commandTimeout = null, int commandsPerMinute = 60, TimeSpan? connectTimeout = null)
        {
            return new YeelightClientOptions
            {
                DiscoveryPort = port,
                RefreshOnConnect = false,
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(3),
                ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3),
                CommandsPerMinute = commandsPerMinute,
            };
        }

        [Fact]
        public async Task ACommandThatIsNotAnswered_FailsWithATimeout_AndTheConnectionStaysUsable()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, TimeSpan.FromMilliseconds(400)));
            harness.Bulb.ReplyOverride = request => request.Method == "toggle" ? [] : null;
            var watch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.ToggleAsync());

            watch.Stop();
            Assert.Equal(YeelightFailureKind.Timeout, exception.Kind);
            Assert.True(exception.Error!.IsTransportFailure);
            Assert.Equal(YeelightError.TimeoutCode, exception.Error.Code);
            Assert.Contains("did not answer 'toggle'", exception.Message, StringComparison.Ordinal);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(350), $"The command failed after {watch.ElapsedMilliseconds} ms, before its time was up.");
            await harness.Device.SetNameAsync("still works");
            Assert.True(harness.Device.IsConnected);
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task AnAnswerThatArrivesAfterTheTimeout_IsIgnored_AndDoesNotHarmTheNextCommand()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, TimeSpan.FromMilliseconds(300)));
            harness.Bulb.Delay = request => request.Method == "toggle" ? TimeSpan.FromMilliseconds(900) : TimeSpan.Zero;

            var late = await harness.Device.SendCommandAsync("toggle");
            Assert.False(late.Success);
            Assert.Equal(YeelightFailureKind.Timeout, late.Error!.Kind);
            Assert.Equal(0, late.Id);

            // The light answers after all, and the next command has its own number: nothing waits for the old answer.
            await Wait.UntilAsync(() => harness.Bulb.Get("power") == "off", "the late command to have been carried out");
            await Task.Delay(1000);
            var next = await harness.Device.SendCommandAsync("get_prop", ["power"]);

            Assert.True(next.Success);
            Assert.Equal("off", next.GetString(0));
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task ACommandThatIsAnsweredOutOfOrder_AndManyAtOnce_EachGetItsOwnAnswer()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, commandsPerMinute: 6000));
            var random = new Random(17);
            harness.Bulb.Delay = _ =>
            {
                lock (random)
                {
                    return TimeSpan.FromMilliseconds(random.Next(0, 60));
                }
            };
            harness.Bulb.ReplyOverride = request => request.Method == "get_prop" && request.Parameters.Length == 1
                ? [$"{{\"id\":{request.Id},\"result\":[\"echo {request.Parameters[0].GetString()}\"]}}"]
                : null;

            var calls = Enumerable.Range(0, 40).Select(number => (Number: number, Call: harness.Device.SendCommandAsync("get_prop", [$"n{number}"]))).ToArray();
            var results = await Task.WhenAll(calls.Select(call => call.Call));

            for (var index = 0; index < calls.Length; index++)
            {
                Assert.True(results[index].Success, $"Command {calls[index].Number}");
                Assert.Equal($"echo n{calls[index].Number}", results[index].GetString(0));
            }
        }

        [Fact]
        public async Task AStreamOfCommandsThatIsFasterThanTheLimit_IsPaced_AndTheLightNeverGetsMoreThanTheLimitAllows()
        {
            // 10 commands go through at once (the burst), after that one every 100 ms.
            await using var harness = await Harness.StartAsync(options: port => Options(port, commandsPerMinute: 600));
            var arrivals = new ConcurrentQueue<long>();
            harness.Bulb.Delay = _ =>
            {
                arrivals.Enqueue(Stopwatch.GetTimestamp());
                return TimeSpan.Zero;
            };

            await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => harness.Device.ToggleAsync()));

            var times = arrivals.OrderBy(time => time).ToArray();
            Assert.Equal(24, times.Length);
            for (var index = 10; index < times.Length; index++)
            {
                var allowedAfter = TimeSpan.FromMilliseconds((index - 9) * 100 - 40);
                var actual = Stopwatch.GetElapsedTime(times[0], times[index]);
                Assert.True(actual >= allowedAfter, $"Command {index + 1} arrived {actual.TotalMilliseconds:0} ms after the first, and no sooner than {allowedAfter.TotalMilliseconds:0} ms is allowed.");
            }
        }

        [Fact]
        public async Task ACommandThatWouldHaveToWaitTooLong_IsRefusedAtOnce_AsRateLimited_AndIsNotSent()
        {
            // The burst of 10, then one every 1.2 seconds (the pace is the 60 a minute less the burst): the 11th waits 1.2 seconds, the 12th would wait
            // 2.4, which is more than the limit of two, and so would the 13th.
            await using var harness = await Harness.StartAsync(options: port => Options(port));
            var watch = Stopwatch.StartNew();

            var results = await Task.WhenAll(Enumerable.Range(0, 13).Select(async _ =>
            {
                var result = await harness.Device.SendCommandAsync("toggle");
                return (Result: result, Took: watch.Elapsed);
            }));

            var refused = results.Where(entry => !entry.Result.Success).ToArray();
            Assert.Equal(2, refused.Length);
            foreach (var refusal in refused)
            {
                Assert.Equal(YeelightFailureKind.RateLimited, refusal.Result.Error!.Kind);
                Assert.True(refusal.Result.Error.IsTransportFailure);
                Assert.Contains("60 a minute", refusal.Result.Error.Message, StringComparison.Ordinal);
                Assert.True(refusal.Took < TimeSpan.FromMilliseconds(1000), $"The refusal took {refusal.Took.TotalMilliseconds:0} ms: it should be at once.");
            }

            Assert.Equal(11, results.Count(entry => entry.Result.Success));
            Assert.Equal(11, harness.Bulb.RequestsOf("toggle").Count);
            Assert.True(results.Max(entry => entry.Took) >= TimeSpan.FromMilliseconds(1100), "The command that had to wait did not wait for its turn.");
        }

        [Fact]
        public async Task TheTypedCommands_ThrowARateLimitedException_WhenTheyAreRefused()
        {
            // 20 a minute: a burst of 10, then one every 6 seconds, which is more than a command is willing to wait.
            await using var harness = await Harness.StartAsync(options: port => Options(port, commandsPerMinute: 20));
            for (var index = 0; index < 10; index++)
            {
                await harness.Device.ToggleAsync();
            }

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.ToggleAsync());

            Assert.Equal(YeelightFailureKind.RateLimited, exception.Kind);
            Assert.Equal(10, harness.Bulb.RequestsOf("toggle").Count);
            Assert.True(harness.Device.IsConnected);
        }

        [Fact]
        public async Task TheQuotaOfTheLight_IsAnAnswerOfTheLight_AndNotAFailureOfTheWayToIt()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { CommandsPerMinute = 3 }, port => Options(port));
            for (var index = 0; index < 3; index++)
            {
                await harness.Device.ToggleAsync();
            }

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.ToggleAsync());
            var result = await harness.Device.SendCommandAsync("toggle");

            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
            Assert.False(exception.Error!.IsTransportFailure);
            Assert.Equal("client quota exceeded", exception.Error.Message);
            Assert.Contains("client quota exceeded", exception.Message, StringComparison.Ordinal);
            Assert.Equal("toggle", exception.Method);
            Assert.False(result.Success);
            Assert.Equal("client quota exceeded", result.Error!.Message);
            Assert.False(result.Error.IsTransportFailure);
            Assert.True(harness.Device.IsConnected);
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task AConnectionThatIsLostWhileACommandWaits_FailsTheCommandAtOnce_NotAfterItsTimeout()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, TimeSpan.FromSeconds(30)));
            harness.Bulb.Delay = request => request.Method == "toggle" ? TimeSpan.FromSeconds(20) : TimeSpan.Zero;
            var watch = Stopwatch.StartNew();
            var call = harness.Device.SendCommandAsync("toggle");
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("toggle").Count == 1, "the command to arrive at the light");

            harness.Bulb.DropConnections();
            var result = await call.WithTimeoutAsync("the command to end when the connection was lost", TimeSpan.FromSeconds(8));

            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.ConnectionLost, result.Error!.Kind);
            Assert.True(result.Error.IsTransportFailure);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"The command took {watch.Elapsed.TotalSeconds:0.0} seconds to end after the connection was lost.");
            await Wait.UntilAsync(() => harness.Device.IsConnected && harness.Bulb.ClientCount == 1, "the connection to be made again");
            harness.Bulb.Delay = _ => TimeSpan.Zero;
            await harness.Device.SetNameAsync("back");
        }

        [Fact]
        public async Task ACommandThatIsGivenAfterTheConnectionWasLost_WaitsForTheNewOne_AndSucceeds()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port));
            await Wait.UntilAsync(() => harness.Bulb.ClientCount >= 1, "the light to have registered the connection");
            harness.Bulb.DropConnections();
            await Wait.UntilAsync(() => !harness.Device.IsConnected, "the loss of the connection to be noticed");

            await harness.Device.ToggleAsync();

            Assert.True(harness.Device.IsConnected);
            Assert.Equal(2, harness.Bulb.ConnectionCount);
            Assert.Single(harness.Bulb.RequestsOf("toggle"));
        }

        [Fact]
        public async Task ALightThatIsGone_IsNotConnected_AfterTheWaitForAConnection()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port));
            await harness.Bulb.DisposeAsync();
            await Wait.UntilAsync(() => !harness.Device.IsConnected, "the loss of the connection to be noticed");
            var watch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.ToggleAsync());
            var result = await harness.Device.SendCommandAsync("toggle");

            Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind);
            Assert.True(exception.Error!.IsTransportFailure);
            Assert.Contains($"127.0.0.1:{harness.Bulb.ControlPort}", exception.Message, StringComparison.Ordinal);
            Assert.Equal(YeelightFailureKind.NotConnected, result.Error!.Kind);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"The two commands took {watch.Elapsed.TotalSeconds:0.0} seconds to be refused.");
        }

        [Fact]
        public async Task ACommandThatIsRefused_AndACommandThatDoesNotArrive_AreToldApartByIsTransportFailure()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, TimeSpan.FromMilliseconds(300)));
            harness.Bulb.ReplyOverride = request => request.Method == "toggle" ? [] : null;

            var refused = await harness.Device.SendCommandAsync("no_such_method");
            var silent = await harness.Device.SendCommandAsync("toggle");

            Assert.Equal(YeelightFailureKind.DeviceRejected, refused.Error!.Kind);
            Assert.False(refused.Error.IsTransportFailure);
            Assert.Equal(YeelightFailureKind.Timeout, silent.Error!.Kind);
            Assert.True(silent.Error.IsTransportFailure);
        }

        [Fact]
        public async Task CommandsThatRaceAConnectionLoss_AllEnd_WithAnAnswerOrWithAFailureThatSaysWhy()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, TimeSpan.FromSeconds(2), commandsPerMinute: 6000));
            harness.Bulb.Delay = _ => TimeSpan.FromMilliseconds(30);
            var calls = new List<Task<YeelightResult>>();

            for (var round = 0; round < 6; round++)
            {
                for (var index = 0; index < 5; index++)
                {
                    calls.Add(harness.Device.SendCommandAsync("get_prop", ["power"]));
                }

                await Task.Delay(20);
                harness.Bulb.DropConnections();
            }

            var results = await Task.WhenAll(calls).WithTimeoutAsync("every command to end", TimeSpan.FromSeconds(20));

            Assert.All(results, result => Assert.True(result.Success || result.Error is { IsTransportFailure: true }, "A command ended in a way that is neither an answer nor a failure of the way to the light."));
            Assert.Contains(results, result => result.Success || result.Error!.Kind is YeelightFailureKind.ConnectionLost or YeelightFailureKind.NotConnected);
            await Wait.UntilAsync(() => harness.Device.IsConnected, "the connection to come back", TimeSpan.FromSeconds(30));
            Assert.True((await harness.Device.SendCommandAsync("get_prop", ["power"])).Success);
        }
    }
}
