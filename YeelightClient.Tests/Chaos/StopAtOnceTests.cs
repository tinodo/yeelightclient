namespace Yeelight.Tests.Chaos
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What the library leaves behind when it is stopped before it has properly started: a loop that was queued and begins when the thing
    /// that it works on is gone has to end quietly, and not with an exception that nobody ever sees (a task that fails and is never awaited is
    /// reported when it is finalized, and an application can have that crash it). On a busy machine the loop begins late, so the tests run
    /// many starts and stops side by side, which keeps the thread pool busy. They run alone (like the soak tests), because the exceptions of
    /// tasks that nobody looked at are collected for the whole process.
    /// </summary>
    [Collection(SoakCollection.Name)]
    public class StopAtOnceTests
    {
        private const int Workers = 16;
        private const int Iterations = 60;

        [Fact]
        public async Task ADiscoveryThatIsStoppedAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();
            var options = LoopbackDiscovery.Search() with { Listen = true, ListenPort = 0 };

            await RunSideBySideAsync(async number =>
            {
                await using var discovery = LoopbackDiscovery.Create(options);
                discovery.Start();
                await PauseAsync(number);
            });

            unobserved.AssertNone();
        }

        [Fact]
        public async Task ADiscoveryThatIsDisposedAtOnceWithoutWaiting_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();
            var options = LoopbackDiscovery.Search() with { Listen = true, ListenPort = 0 };

            await RunSideBySideAsync(async number =>
            {
                using var discovery = LoopbackDiscovery.Create(options);
                discovery.Start();
                await PauseAsync(number);
            });

            unobserved.AssertNone();
        }

        [Fact]
        public async Task AClientThatSearchesAndIsDisposedAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();

            await RunSideBySideAsync(async number =>
            {
                await using var client = TestClients.Create();
                client.StartListening();
                client.StartDiscovery();
                await PauseAsync(number);
            });

            unobserved.AssertNone();
        }

        [Fact]
        public async Task ASearchThatEndsAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();

            await RunSideBySideAsync(async number =>
            {
                await using var client = TestClients.Create();
                var found = await client.DiscoverAsync(TimeSpan.FromMilliseconds(1 + (number % 3)));
                Assert.Empty(found);
            });

            unobserved.AssertNone();
        }

        [Fact]
        public async Task AConnectionThatIsStartedAndClosedAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();
            await using var bulb = new SimulatedBulb();

            await RunSideBySideAsync(async number =>
            {
                await using var client = TestClients.Create();
                var device = client.Apply(Announcements.From(bulb, Announcements.LocationOf(bulb.ControlPort)));
                Assert.NotNull(device);
                await PauseAsync(number);
            });

            unobserved.AssertNone();
        }

        [Fact]
        public async Task AMusicSessionThatIsEndedAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();
            var started = 0;

            await RunSideBySideAsync(
                async number =>
                {
                    await using var bulb = new SimulatedBulb();
                    await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false });
                    var device = client.Apply(Announcements.From(bulb, Announcements.LocationOf(bulb.ControlPort))) ?? throw new InvalidOperationException("The client did not take the light.");

                    // Cancelled at every point of the start, or not at all; and ended by the end of the session, or of the client.
                    using var cancel = new CancellationTokenSource();
                    if (number % 3 != 0)
                    {
                        cancel.CancelAfter(TimeSpan.FromMilliseconds(number % 40));
                    }

                    try
                    {
                        await using var session = await device.StartMusicModeAsync(cancellationToken: cancel.Token);
                        Interlocked.Increment(ref started);
                        await PauseAsync(number);
                        if (number % 2 == 0)
                        {
                            await client.DisposeAsync();
                        }
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or YeelightException)
                    {
                        // The start did not end well: that is what the cancel is for.
                    }
                },
                workers: 6,
                iterations: 15);

            Assert.True(Volatile.Read(ref started) >= 10, $"Only {started} of 90 music sessions were made: the test does not look at what it is for.");
            unobserved.AssertNone();
        }

        [Fact]
        public async Task AProbeThatIsCancelledAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();

            await RunSideBySideAsync(
                async number =>
                {
                    await using var bulb = new SimulatedBulb { AnswersSearches = false };
                    await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort });
                    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(number % 6));

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProbeAsync(IPAddress.Loopback, cancel.Token));
                },
                workers: 8,
                iterations: 20);

            unobserved.AssertNone();
        }

        [Fact]
        public async Task ADeviceThatIsConnectedAndDisconnectedAtOnce_LeavesNoTaskThatFailedUnseen()
        {
            using var unobserved = new UnobservedExceptions();
            var connected = 0;

            await RunSideBySideAsync(
                async number =>
                {
                    await using var bulb = new SimulatedBulb();
                    await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, RefreshOnConnect = number % 2 == 0, ConnectTimeout = TimeSpan.FromSeconds(2) });
                    var device = client.Apply(Announcements.From(bulb, Announcements.LocationOf(bulb.ControlPort))) ?? throw new InvalidOperationException("The client did not take the light.");
                    for (var round = 0; round < 3; round++)
                    {
                        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(5 + (number % 10)));
                        try
                        {
                            await device.ConnectAsync(cancel.Token);
                            Interlocked.Increment(ref connected);
                            await PauseAsync(number + round);
                        }
                        catch (Exception exception) when (exception is OperationCanceledException or YeelightException)
                        {
                            // The wait for the connection was cancelled: the connection is closed all the same.
                        }

                        await device.DisconnectAsync();
                    }

                    await client.RemoveDeviceAsync(device);
                },
                workers: 8,
                iterations: 12);

            Assert.True(Volatile.Read(ref connected) >= 20, $"Only {connected} of 288 connections were made: the test does not look at what it is for.");
            unobserved.AssertNone();
        }

        // Several things that make the thread pool busy, and so make a queued loop begin after what it works on was disposed.
        private static async Task RunSideBySideAsync(Func<int, Task> work, int workers = Workers, int iterations = Iterations)
        {
            var running = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
            {
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    await work((worker * iterations) + iteration).ConfigureAwait(false);
                }
            }));
            await Task.WhenAll(running).WithTimeoutAsync("the starts and stops to be over", TimeSpan.FromSeconds(120));
        }

        // From no time at all to a few milliseconds: the stop comes at every point of the start.
        private static async Task PauseAsync(int number)
        {
            switch (number % 5)
            {
                case 0:
                    break;
                case 1:
                    await Task.Yield();
                    break;
                case 2:
                    Thread.SpinWait(500 + (number % 7 * 300));
                    break;
                case 3:
                    await Task.Delay(1).ConfigureAwait(false);
                    break;
                default:
                    await Task.Delay(3).ConfigureAwait(false);
                    break;
            }
        }
    }
}
