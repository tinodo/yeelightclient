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
    /// A client of two lights (the same simulated light, twice) runs for a while with the network doing what a network does (the connection
    /// is cut, stalled and refused, at random, from a seed) while one task sends commands and reads properties, and another sends a stream of
    /// commands as fast as the client lets it. The runs for a few seconds, and for as long as <c>YEELIGHT_SOAK_SECONDS</c> says.
    /// </summary>
    /// <remarks>
    /// What has to hold: every call ends in time, with what the light answered or with a failure that says the way to it failed (and never
    /// with another exception); an answer is the answer of its own command; the stream never sends the light more than the client is
    /// allowed to send; <c>IsConnected</c> is reported; when the chaos stops the client is connected again and everything works; no task fails
    /// without anyone looking; and what the connections used (threads, handles, memory) is given back.
    /// </remarks>
    [Collection(SoakCollection.Name)]
    [Trait("Category", "Soak")]
    public class SoakTests(ITestOutputHelper output)
    {
        private const int StreamPerMinute = 300;

        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(1);

        // The longest that a call may take: the wait for a connection (the client waits two seconds for one), the wait for the answer, and a busy machine.
        private static readonly TimeSpan Longest = TimeSpan.FromSeconds(2) + CommandTimeout + TimeSpan.FromSeconds(6);

        [Fact]
        public async Task WhileTheNetworkMisbehaves_EveryCallEndsInTime_AnswersAreRight_AndTheClientIsBackWhenItStops()
        {
            using var unobserved = new UnobservedExceptions();

            // A first run, short, makes whatever is made once (the thread pool grows, the types are loaded) before the numbers are taken.
            await this.RunAsync(TimeSpan.FromSeconds(1.5), warmUp: true);
            var before = ResourceSnapshot.Take();

            var result = await this.RunAsync(SoakSettings.Duration, warmUp: false);

            var after = ResourceSnapshot.Take();
            output.WriteLine($"Soak: {SoakSettings.Description}.");
            output.WriteLine(result.Describe());
            output.WriteLine($"Before: {before}. After: {after}.");
            Assert.True(result.Problems.Count == 0, "Problems:\n" + string.Join("\n", result.Problems.Take(10)) + $"\n(Soak: {SoakSettings.Description}.)");
            Assert.True(result.Calls.Total > 0 && result.Calls["success"] > 0, $"No call succeeded in the whole run: {result.Calls}.");
            Assert.True(result.Cuts > 0 || SoakSettings.Duration < TimeSpan.FromSeconds(3), "The chaos did not cut the connection once.");
            Assert.True(result.MaxCallTime < Longest, $"A call took {result.MaxCallTime.TotalSeconds:0.0} seconds, and none may take {Longest.TotalSeconds:0.#}.");
            unobserved.AssertNone();
            AssertGivenBack(before, after);
        }

        // Threads, handles and memory are given back, with room for what a busy machine does on its own.
        internal static void AssertGivenBack(ResourceSnapshot before, ResourceSnapshot after)
        {
            Assert.True(after.Threads <= before.Threads + 40, $"The process had {before.Threads} threads before and has {after.Threads} after.");
            if (before.Handles > 0)
            {
                Assert.True(after.Handles <= before.Handles + 200, $"The process had {before.Handles} handles before and has {after.Handles} after.");
            }

            Assert.True(after.ManagedBytes <= before.ManagedBytes + (96L * 1024 * 1024), $"The process held {before.ManagedBytes / 1048576d:0.0} MB before and holds {after.ManagedBytes / 1048576d:0.0} MB after.");
        }

        private async Task<SoakResult> RunAsync(TimeSpan duration, bool warmUp)
        {
            var problems = new ConcurrentQueue<string>();
            var calls = new Tally();
            var longest = new LongestTime();

            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { CommandsPerMinute = 6_000_000 });
            await using var proxy = new ChaosProxy("127.0.0.1", bulb.ControlPort);
            proxy.Start();
            var location = Announcements.LocationOf(proxy.Port);
            var quick = new YeelightClientOptions { CommandTimeout = CommandTimeout, ConnectTimeout = TimeSpan.FromSeconds(1), KeepAliveInterval = TimeSpan.FromSeconds(2), CommandsPerMinute = 6_000_000 };
            var paced = new YeelightClientOptions { CommandTimeout = CommandTimeout, ConnectTimeout = TimeSpan.FromSeconds(1), CommandsPerMinute = StreamPerMinute };
            await using var client = TestClients.Create(quick);
            await using var streamClient = TestClients.Create(paced);
            var connectedEvents = new EventRecorder<bool>();
            client.DeviceStateChanged += (_, e) =>
            {
                if (e.Light == e.Device && e.PropertyName == nameof(YeelightDevice.IsConnected))
                {
                    connectedEvents.Add(e.Device.IsConnected);
                }
            };
            var device = client.Apply(Announcements.From(bulb, location)) ?? throw new InvalidOperationException("The client did not take the light.");
            var stream = streamClient.Apply(Announcements.From(bulb, location)) ?? throw new InvalidOperationException("The client did not take the light.");
            await Wait.UntilAsync(() => device.IsConnected && stream.IsConnected, "both lights to be connected before the chaos", TimeSpan.FromSeconds(15));

            using var stop = new CancellationTokenSource();
            var chaosOver = false;
            var afterChaos = new HitCounter();
            var streamWatch = new Stopwatch();

            var integrity = Task.Run(async () =>
            {
                var number = 0;
                while (!stop.IsCancellationRequested)
                {
                    var value = (number++ % 100) + 1;
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        if (number % 3 == 0)
                        {
                            var values = await device.GetPropertiesAsync(["power", "ct"]).WithTimeoutAsync("a read of properties", TimeSpan.FromSeconds(20));

                            // The answer of a command is the answer of that command: this is what is in the light, whatever the network did to the others.
                            if (values["power"] != "on" || values["ct"] != "4000")
                            {
                                problems.Enqueue($"get_prop answered {values["power"]} and {values["ct"]}, and the light is on at 4000 K.");
                            }
                        }
                        else
                        {
                            await device.SetBrightnessAsync(value).WithTimeoutAsync("a command", TimeSpan.FromSeconds(20));
                        }

                        calls.Add("success");
                        if (Volatile.Read(ref chaosOver))
                        {
                            afterChaos.Increment();
                        }
                    }
                    catch (YeelightException exception) when (exception.Error is { IsTransportFailure: true })
                    {
                        calls.Add(exception.Kind.ToString());
                        if (exception.Kind == YeelightFailureKind.RateLimited)
                        {
                            problems.Enqueue("The client refused a command as too many, and it sends a few a second.");
                        }
                    }
                    catch (Exception exception)
                    {
                        problems.Enqueue($"A call failed with {exception.GetType().Name}: {exception.Message}");
                    }

                    longest.Add(watch.Elapsed);
                    await Task.Delay(5).ConfigureAwait(false);
                }
            });

            var streaming = Task.Run(async () =>
            {
                var number = 0;
                streamWatch.Start();
                while (!stop.IsCancellationRequested)
                {
                    var name = "stream-" + (number++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        await stream.SetNameAsync(name).WithTimeoutAsync("a command of the stream", TimeSpan.FromSeconds(20));
                        calls.Add("stream success");
                    }
                    catch (YeelightException exception) when (exception.Error is { IsTransportFailure: true })
                    {
                        calls.Add("stream " + exception.Kind);
                    }
                    catch (Exception exception)
                    {
                        problems.Enqueue($"A call of the stream failed with {exception.GetType().Name}: {exception.Message}");
                    }

                    longest.Add(watch.Elapsed);
                }

                streamWatch.Stop();
            });

            await using var chaos = new ChaosDriver(proxy, SoakSettings.Seed + (warmUp ? 1 : 0), ChaosStyle.Gentle(!warmUp && !SoakSettings.IsShort));
            chaos.Start();
            await Task.Delay(duration);
            await chaos.StopAsync();
            Volatile.Write(ref chaosOver, true);

            // The chaos is over: the clients connect again (a client that has failed a few times waits up to half a minute to try), and everything works.
            var recovery = Stopwatch.StartNew();
            var patience = TimeSpan.FromSeconds(duration > TimeSpan.FromSeconds(30) ? 60 : 25);
            while ((!device.IsConnected || !stream.IsConnected || afterChaos.Value < 3) && recovery.Elapsed < patience)
            {
                await Task.Delay(50);
            }

            if (!device.IsConnected || !stream.IsConnected)
            {
                problems.Enqueue($"The clients were not connected {recovery.Elapsed.TotalSeconds:0} seconds after the chaos stopped (device {device.IsConnected}, stream {stream.IsConnected}).");
            }

            if (afterChaos.Value < 3)
            {
                problems.Enqueue($"Only {afterChaos.Value} calls succeeded in the {recovery.Elapsed.TotalSeconds:0} seconds after the chaos stopped.");
            }

            stop.Cancel();
            try
            {
                await Task.WhenAll(integrity, streaming).WithTimeoutAsync("the tasks that send commands to end", TimeSpan.FromSeconds(30));
            }
            catch (Exception exception)
            {
                problems.Enqueue($"The tasks that send commands did not end well: {exception.Message}");
            }

            // The stream has not sent the light more than the client may send: a burst of ten, and then the pace that was asked for.
            var allowed = 10 + (StreamPerMinute / 60d * streamWatch.Elapsed.TotalSeconds) + 2;
            var received = bulb.RequestsOf("set_name").Count;
            if (received > allowed)
            {
                problems.Enqueue($"The stream sent the light {received} commands in {streamWatch.Elapsed.TotalSeconds:0.0} seconds, and the client may send {allowed:0}.");
            }

            // What is left when the network is quiet again: the light has what the last command said, and the client knows.
            try
            {
                await device.SetBrightnessAsync(77).WithTimeoutAsync("the command after the chaos", TimeSpan.FromSeconds(10));
                var read = await device.GetPropertiesAsync(["bright"]).WithTimeoutAsync("the read after the chaos", TimeSpan.FromSeconds(10));
                if (read["bright"] != "77" || bulb.Get("bright") != "77")
                {
                    problems.Enqueue($"After a command of 77 the light has {bulb.Get("bright")} and said {read["bright"]}.");
                }

                await Wait.UntilAsync(() => device.Brightness == 77, "the client to know the brightness that it set", TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                problems.Enqueue($"The client does not work after the chaos: {exception.GetType().Name}: {exception.Message}");
            }

            // IsConnected was reported, and the last that was reported is the truth.
            await Task.Delay(300);
            if (chaos.Cuts > 0 && connectedEvents.Count == 0)
            {
                problems.Enqueue("The connection was cut and IsConnected was never reported.");
            }

            if (connectedEvents.Items.Count > 0 && connectedEvents.Items[^1] != device.IsConnected)
            {
                problems.Enqueue($"The last IsConnected that was reported is {connectedEvents.Items[^1]}, and the light is {(device.IsConnected ? "connected" : "not connected")}.");
            }

            var cuts = chaos.Cuts;
            var stalls = chaos.Stalls;
            var refusals = chaos.Refusals;
            var accepted = proxy.Accepted;

            // Everything is closed: what the client and the proxy ended, the light has seen end.
            await client.DisposeAsync();
            await streamClient.DisposeAsync();
            try
            {
                await Wait.UntilAsync(() => bulb.ClientCount == 0 && proxy.Open == 0, "every connection to be closed", TimeSpan.FromSeconds(15));
            }
            catch (Xunit.Sdk.XunitException)
            {
                problems.Enqueue($"After the clients were disposed the light still has {bulb.ClientCount} connections open, and the proxy {proxy.Open}.");
            }

            return new SoakResult(calls, longest.Value, problems.ToList(), cuts, stalls, refusals, accepted, bulb.Requests.Count, connectedEvents.Count);
        }

        private sealed record SoakResult(Tally Calls, TimeSpan MaxCallTime, List<string> Problems, int Cuts, int Stalls, int Refusals, int Connections, int Requests, int ConnectedEvents)
        {
            public string Describe() => $"{this.Calls.Total} calls ({this.Calls}); the longest took {this.MaxCallTime.TotalSeconds:0.00} s; the chaos: {this.Cuts} cuts, {this.Stalls} stalls, {this.Refusals} refusals; {this.Connections} connections were made, {this.Requests} commands reached the light, IsConnected was reported {this.ConnectedEvents} times.";
        }
    }
}
