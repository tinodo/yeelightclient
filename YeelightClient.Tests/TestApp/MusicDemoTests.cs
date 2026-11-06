namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The demo of music mode: a rainbow, about twenty commands a second, over the session of a light.</summary>
    public sealed class MusicDemoTests
    {
        [Fact]
        public async Task TheDemo_SendsMoreCommandsThanAControlConnectionTakes_OverTheSession()
        {
            await using var harness = await Harness.StartAsync();

            // The address is given: without one the client uses the address of its control connection, which is an IPv4-mapped IPv6
            // address (::ffff:127.0.0.1) that Windows refuses to listen on, see the report of the test app.
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);

            var result = await MusicDemo.RunAsync(session, useColor: true, TimeSpan.FromSeconds(1.5));

            await Wait.UntilAsync(() => harness.Bulb.Requests.Count(request => request.OverMusic) >= result.CommandsSent, "the commands at the light");
            var received = harness.Bulb.Requests.Where(request => request.OverMusic).ToArray();
            Assert.InRange(result.CommandsSent, 20, 40);
            Assert.False(result.Cancelled);
            Assert.False(result.ConnectionLost);
            Assert.True(result.CommandsPerSecond * 60 > YeelightLimits.CommandsPerMinute, "more than a control connection takes in a minute");
            Assert.Equal(result.CommandsSent, received.Length);
            Assert.Contains(received, request => request.Method == "set_hsv");
            Assert.Contains(received, request => request.Method == "set_rgb");
            Assert.All(received, request => Assert.True(request.Method is "set_hsv" or "set_rgb"));
            Assert.Equal(1.5, result.Elapsed.TotalSeconds, 0.3);
            Assert.Contains("no quota", result.Describe(), StringComparison.Ordinal);
            Assert.Contains($"Sent {result.CommandsSent} commands", result.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheHuesOfTheRainbow_GoRoundAndStayInRange()
        {
            await using var harness = await Harness.StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);

            var result = await MusicDemo.RunAsync(session, useColor: true, TimeSpan.FromSeconds(1));

            await Wait.UntilAsync(() => harness.Bulb.Requests.Count(request => request.OverMusic) >= result.CommandsSent, "the commands at the light");
            var hues = harness.Bulb.Requests.Where(request => request.OverMusic && request.Method == "set_hsv").Select(request => request.Parameters[0].GetInt32()).ToArray();
            Assert.All(hues, hue => Assert.InRange(hue, 0, 359));
            Assert.True(hues.Distinct().Count() > 5, "the colour moves");
            Assert.Equal(hues.OrderBy(hue => hue), hues);
        }

        [Fact]
        public async Task ALightWithoutColours_Breathes()
        {
            await using var harness = await Harness.StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);

            var result = await MusicDemo.RunAsync(session, useColor: false, TimeSpan.FromSeconds(1));

            await Wait.UntilAsync(() => harness.Bulb.Requests.Count(request => request.OverMusic) >= result.CommandsSent, "the commands at the light");
            var received = harness.Bulb.Requests.Where(request => request.OverMusic).ToArray();
            Assert.All(received, request => Assert.Equal("set_bright", request.Method));
            Assert.All(received, request => Assert.InRange(request.Parameters[0].GetInt32(), 1, 100));
            Assert.True(received.Select(request => request.Parameters[0].GetInt32()).Distinct().Count() > 3, "the brightness moves");
        }

        [Fact]
        public async Task TheDemo_CanBeStopped()
        {
            await using var harness = await Harness.StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            var result = await MusicDemo.RunAsync(session, useColor: true, TimeSpan.FromSeconds(30), cancellationToken: stop.Token);

            Assert.True(result.Cancelled);
            Assert.False(result.ConnectionLost);
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(5));
            Assert.InRange(result.CommandsSent, 1, 12);
            Assert.StartsWith("Stopped. ", result.Describe(), StringComparison.Ordinal);
            Assert.True(session.IsConnected, "the session stays open");
        }

        [Fact]
        public async Task WhenTheSessionEnds_TheDemoEndsToo_WithoutAnException()
        {
            await using var harness = await Harness.StartAsync();
            var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);

            var running = MusicDemo.RunAsync(session, useColor: true, TimeSpan.FromSeconds(30));
            await Task.Delay(300);
            await session.DisposeAsync();
            var result = await running.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result.ConnectionLost);
            Assert.False(result.Cancelled);
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(10));
            Assert.StartsWith("The session ended. ", result.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheDemo_ReportsItsProgress()
        {
            await using var harness = await Harness.StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);
            var counts = new System.Collections.Concurrent.ConcurrentQueue<int>();

            var result = await MusicDemo.RunAsync(session, useColor: true, TimeSpan.FromMilliseconds(500), progress: new SynchronousProgress(counts.Enqueue));

            Assert.Equal(Enumerable.Range(1, result.CommandsSent), counts);
        }

        [Fact]
        public async Task ARateOfZero_IsRefused()
        {
            await using var harness = await Harness.StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => MusicDemo.RunAsync(session, true, TimeSpan.FromSeconds(1), rate: 0));
        }

        private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
        {
            public void Report(int value) => report(value);
        }
    }
}
