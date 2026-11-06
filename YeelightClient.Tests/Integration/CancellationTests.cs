namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// Everything that waits takes a <see cref="CancellationToken"/>: cancelling it ends the wait with an <see cref="OperationCanceledException"/>
    /// (and not with a failure of the command), at once, and leaves the connection and the client usable.
    /// </summary>
    public class CancellationTests
    {
        private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

        private static readonly ColorFlow Flow = new([ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(500)), ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(100))]);

        // Every command of a light that takes a token, by name, so that one theory goes through all of them.
        private static readonly Dictionary<string, Func<YeelightDevice, CancellationToken, Task>> Commands = new()
        {
            ["SetPower"] = (device, token) => device.SetPowerAsync(true, null, PowerOnMode.Normal, token),
            ["TurnOn"] = (device, token) => device.TurnOnAsync(null, PowerOnMode.Normal, token),
            ["TurnOff"] = (device, token) => device.TurnOffAsync(null, token),
            ["Toggle"] = (device, token) => device.ToggleAsync(token),
            ["SetBrightness"] = (device, token) => device.SetBrightnessAsync(50, null, token),
            ["SetColorTemperature"] = (device, token) => device.SetColorTemperatureAsync(3000, null, token),
            ["SetRgb"] = (device, token) => device.SetRgbAsync(1, 2, 3, null, token),
            ["SetRgbColor"] = (device, token) => device.SetRgbAsync(Color.Red, null, token),
            ["SetHsv"] = (device, token) => device.SetHsvAsync(10, 20, null, token),
            ["SetDefault"] = (device, token) => device.SetDefaultAsync(token),
            ["StartColorFlow"] = (device, token) => device.StartColorFlowAsync(Flow, token),
            ["StopColorFlow"] = (device, token) => device.StopColorFlowAsync(token),
            ["SetScene"] = (device, token) => device.SetSceneAsync(YeelightScene.ColorTemperature(3000, 50), token),
            ["Adjust"] = (device, token) => device.AdjustAsync(AdjustAction.Increase, AdjustProperty.Brightness, token),
            ["AdjustBrightness"] = (device, token) => device.AdjustBrightnessAsync(10, null, token),
            ["AdjustColorTemperature"] = (device, token) => device.AdjustColorTemperatureAsync(10, null, token),
            ["AdjustColor"] = (device, token) => device.AdjustColorAsync(null, token),
            ["SendCommand"] = (device, token) => device.SendCommandAsync("toggle", null, token),
            ["SetName"] = (device, token) => device.SetNameAsync("name", token),
            ["ToggleAll"] = (device, token) => device.ToggleAllAsync(token),
            ["SetSleepTimer"] = (device, token) => device.SetSleepTimerAsync(TimeSpan.FromMinutes(5), token),
            ["GetSleepTimer"] = (device, token) => device.GetSleepTimerAsync(token),
            ["CancelSleepTimer"] = (device, token) => device.CancelSleepTimerAsync(token),
            ["GetProperties"] = (device, token) => device.GetPropertiesAsync(["power"], token),
            ["Refresh"] = (device, token) => device.RefreshAsync(token),
            ["BackgroundTurnOn"] = (device, token) => device.Background.TurnOnAsync(null, PowerOnMode.Normal, token),
            ["BackgroundSetBrightness"] = (device, token) => device.Background.SetBrightnessAsync(50, null, token),
            ["BackgroundSetRgb"] = (device, token) => device.Background.SetRgbAsync(1, 2, 3, null, token),
        };

        public static TheoryData<string> AllCommands => new(Commands.Keys);

        public static TheoryData<string> SomeCommands => new("Toggle", "SetBrightness", "GetProperties", "Refresh", "SendCommand", "SetName", "GetSleepTimer", "SetScene", "StartColorFlow", "BackgroundTurnOn");

        private static YeelightClientOptions Options(int port, TimeSpan? commandTimeout = null, int commandsPerMinute = 60)
        {
            return new YeelightClientOptions
            {
                DiscoveryPort = port,
                RefreshOnConnect = false,
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30),
                CommandsPerMinute = commandsPerMinute,
            };
        }

        [Theory]
        [MemberData(nameof(AllCommands))]
        public async Task ACommandThatIsGivenACancelledToken_ThrowsOperationCanceled_AndSendsNothing(string command)
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true }, port => Options(port, commandsPerMinute: 6000));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Commands[command](harness.Device, cancelled.Token));

            Assert.Equal(before, harness.Bulb.Requests.Count);
            Assert.True(harness.Device.IsConnected);
            await harness.Device.SetNameAsync("still works");
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Theory]
        [MemberData(nameof(SomeCommands))]
        public async Task ACommandThatIsCancelledWhileItWaitsForTheAnswer_ThrowsOperationCanceled_AndTheConnectionStaysUsable(string command)
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true }, port => Options(port, commandsPerMinute: 6000));
            harness.Bulb.ReplyOverride = _ => [];
            using var cancellation = new CancellationTokenSource();
            var watch = Stopwatch.StartNew();

            var call = Commands[command](harness.Device, cancellation.Token);
            await Wait.UntilAsync(() => harness.Bulb.Requests.Count == 1, "the command to arrive at the light");
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WithTimeoutAsync("the command to end when its token was cancelled", Prompt));
            Assert.True(watch.Elapsed < Prompt);
            harness.Bulb.ReplyOverride = null;
            Assert.True(harness.Device.IsConnected);
            await harness.Device.SetNameAsync("still works");
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task TheAnswerToACommandThatWasCancelled_ArrivesLater_AndDoesNotHarmTheNextCommand()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port));
            harness.Bulb.Delay = request => request.Method == "toggle" ? TimeSpan.FromMilliseconds(500) : TimeSpan.Zero;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.ToggleAsync(cancellation.Token));
            await Wait.UntilAsync(() => harness.Bulb.Get("power") == "off", "the light to carry out the command that was cancelled");
            await Task.Delay(700);

            var next = await harness.Device.SendCommandAsync("get_prop", ["power"]);
            Assert.True(next.Success);
            Assert.Equal("off", next.GetString(0));
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task ACommandThatIsCancelledWhileItWaitsForItsTurnUnderTheLimit_ThrowsOperationCanceled_AndSendsNothing()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port));
            for (var index = 0; index < 10; index++)
            {
                await harness.Device.ToggleAsync();
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.ToggleAsync(cancellation.Token));

            Assert.Equal(10, harness.Bulb.RequestsOf("toggle").Count);
            Assert.True(harness.Device.IsConnected);
        }

        [Fact]
        public async Task ConnectAsync_ThatIsCancelledWhileItWaits_ThrowsOperationCanceled_Promptly()
        {
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, ConnectTimeout = TimeSpan.FromSeconds(30) });
            var device = client.Apply(Announcements.Of("0x1", Announcements.LocationOf(FreePort.Tcp()))) ?? throw new InvalidOperationException("The light was not taken over.");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var watch = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => device.ConnectAsync(cancellation.Token).WithTimeoutAsync("ConnectAsync to end when its token was cancelled", Prompt));

            Assert.True(watch.Elapsed < Prompt);
        }

        [Fact]
        public async Task ConnectAsync_WithACancelledToken_ThrowsOperationCanceled_ForALightThatIsNotConnected()
        {
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, ConnectTimeout = TimeSpan.FromSeconds(30) });
            var device = client.Apply(Announcements.Of("0x1", Announcements.LocationOf(FreePort.Tcp()))) ?? throw new InvalidOperationException("The light was not taken over.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => device.ConnectAsync(cancelled.Token).WithTimeoutAsync("ConnectAsync to end", Prompt));
        }

        [Fact]
        public async Task ProbeAsync_ThatIsCancelledWhileItWaits_ThrowsOperationCanceled_Promptly()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var watch = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProbeAsync(IPAddress.Loopback, cancellation.Token).WithTimeoutAsync("ProbeAsync to end when its token was cancelled", Prompt));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2.5), $"ProbeAsync took {watch.Elapsed.TotalSeconds:0.0} seconds to end when it was cancelled.");
            Assert.True(bulb.SearchCount >= 1);
            Assert.Empty(client.Devices);
        }

        [Fact]
        public async Task ProbeAsync_WithACancelledToken_ThrowsOperationCanceled_AtOnce()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProbeAsync(IPAddress.Loopback, cancelled.Token).WithTimeoutAsync("ProbeAsync to end", Prompt));

            Assert.Empty(client.Devices);
        }

        [Fact]
        public async Task DiscoverAsync_ThatIsCancelledWhileItWaits_ThrowsOperationCanceled_Promptly_AndKeepsWhatItFound()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, RefreshOnConnect = false }, searchTarget: bulb);
            using var cancellation = new CancellationTokenSource();

            var call = client.DiscoverAsync(TimeSpan.FromSeconds(30), cancellation.Token);
            await Wait.UntilAsync(() => client.Devices.Count == 1, "the light to be found");
            var watch = Stopwatch.StartNew();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WithTimeoutAsync("DiscoverAsync to end when its token was cancelled", Prompt));

            Assert.True(watch.Elapsed < Prompt);
            Assert.Single(client.Devices);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_WithACancelledToken_ThrowsOperationCanceled_AtOnce()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DiscoverAsync(TimeSpan.FromSeconds(30), cancelled.Token).WithTimeoutAsync("DiscoverAsync to end", Prompt));
        }

        [Fact]
        public async Task WaitForDeviceAsync_ThatIsCancelledWhileItWaits_ThrowsOperationCanceled_Promptly()
        {
            await using var client = TestClients.Create();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var watch = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromSeconds(30), cancellation.Token).WithTimeoutAsync("WaitForDeviceAsync to end when its token was cancelled", Prompt));

            Assert.True(watch.Elapsed < Prompt);
        }

        [Fact]
        public async Task WaitForDeviceAsync_WithACancelledToken_ThrowsOperationCanceled_EvenForALightThatIsNotThere()
        {
            await using var client = TestClients.Create();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromSeconds(30), cancelled.Token).WithTimeoutAsync("WaitForDeviceAsync to end", Prompt));
        }

        [Fact]
        public async Task StartMusicModeAsync_ThatIsCancelledWhileItWaitsForTheLight_ThrowsOperationCanceled_Promptly()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ConnectsForMusic = false;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.StartMusicModeAsync(cancellationToken: cancellation.Token).WithTimeoutAsync("StartMusicModeAsync to end when its token was cancelled", Prompt));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"StartMusicModeAsync took {watch.Elapsed.TotalSeconds:0.0} seconds to end when it was cancelled (it waits five seconds for the light).");
            Assert.True(harness.Device.IsConnected);
            await harness.Device.SetNameAsync("still works");
        }

        [Fact]
        public async Task StartMusicModeAsync_WithACancelledToken_ThrowsOperationCanceled_AndLeavesNoListenerBehind()
        {
            await using var harness = await Harness.StartAsync();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.StartMusicModeAsync(cancellationToken: cancelled.Token).WithTimeoutAsync("StartMusicModeAsync to end", Prompt));

            Assert.False(harness.Bulb.InMusicMode);
            Assert.True(harness.Device.IsConnected);
        }
    }
}
