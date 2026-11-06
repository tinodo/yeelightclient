namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Net;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// The whole way, from the API to the wire and back, against a simulated light.
    /// </summary>
    public class DeviceEndToEndTests
    {
        private static readonly string[] AllMethods = SimulatedBulbOptions.AllMethods.Split(' ');

        // Only a get_prop of every property reports active_mode, which an announcement does not: when it is there, the refresh is taken over.
        private static Task RefreshedAsync(Harness harness) => Wait.UntilAsync(() => harness.Device.ActiveMode is not null, "the properties that were read after connecting");

        private static string Answer(SimulatedRequest request, params string[] values)
        {
            return $"{{\"id\":{request.Id},\"result\":[{string.Join(',', values.Select(value => JsonSerializer.Serialize(value)))}]}}";
        }

        [Fact]
        public async Task AProbe_FindsTheLight_AndTheClientConnectsToIt()
        {
            await using var harness = await Harness.StartAsync();

            Assert.Same(harness.Device, harness.Client.FindDevice(harness.Bulb.Id));
            Assert.Equal("color", harness.Device.Model);
            Assert.Equal("18", harness.Device.FirmwareVersion);
            Assert.True(harness.Device.IsConnected);
            Assert.True(harness.Device.IsOn);
            Assert.Equal(100, harness.Device.Brightness);
            Assert.Equal(ColorMode.ColorTemperature, harness.Device.ColorMode);
            Assert.Equal(4000, harness.Device.ColorTemperature);
            Assert.Equal("my_bulb", harness.Device.Name);
        }

        [Fact]
        public async Task Commands_ReachTheLight_AndTheStateFollowsWhatTheLightReports()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;

            await device.SetBrightnessAsync(35, TimeSpan.FromMilliseconds(500));
            await device.SetRgbAsync(Color.FromArgb(10, 20, 30));
            await device.TurnOffAsync();

            await Wait.UntilAsync(() => !device.IsOn && device.Brightness == 35 && device.Rgb == ((10 << 16) | (20 << 8) | 30), "the reports of the light");
            Assert.Equal(ColorMode.Rgb, device.ColorMode);
            Assert.Equal("off", harness.Bulb.Get("power"));
            var brightness = harness.Bulb.RequestsOf("set_bright").Single();
            Assert.Equal(35, brightness.Parameters[0].GetInt32());
            Assert.Equal("smooth", brightness.Parameters[1].GetString());
            Assert.Equal(500, brightness.Parameters[2].GetInt32());
        }

        [Fact]
        public async Task ACommandThatTheLightRefuses_Throws()
        {
            await using var harness = await Harness.StartAsync();
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.SetBrightnessAsync(50));

            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
            Assert.Equal("set_bright", exception.Method);
        }

        [Fact]
        public async Task TheIdentityOfTheDevice_IsWhatTheLightSaidWhenItWasFound()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;

            Assert.Equal("0x000000000015243f", device.Id);
            Assert.Equal(harness.Bulb.Location, device.Location);
            Assert.Equal(IPAddress.Loopback, device.Address);
            Assert.Equal(harness.Bulb.ControlPort, device.Port);
            Assert.Equal("color", device.Model);
            Assert.Equal("18", device.FirmwareVersion);
            Assert.Equal(AllMethods.Order(StringComparer.Ordinal), device.SupportedMethods.Order(StringComparer.Ordinal));
            Assert.True(device.SupportsColor);
            Assert.True(device.SupportsColorTemperature);
            Assert.True(device.SupportsMusicMode);
            Assert.False(device.SupportsBackgroundLight);
            Assert.True(device.Supports("set_power"));
            Assert.False(device.Supports("bg_set_power"));
            Assert.InRange(device.LastSeen, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Contains(device.Id, device.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task EveryPropertyOfTheState_IsWhatTheLightReportedAfterTheRefresh()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;

            await RefreshedAsync(harness);

            Assert.True(device.IsOn);
            Assert.Equal(100, device.Brightness);
            Assert.Equal(ColorMode.ColorTemperature, device.ColorMode);
            Assert.Equal(4000, device.ColorTemperature);
            Assert.Equal(16711680, device.Rgb);
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), device.Color);
            Assert.Equal(100, device.Hue);
            Assert.Equal(35, device.Saturation);
            Assert.False(device.IsFlowing);
            Assert.Equal(string.Empty, device.FlowParameters);
            Assert.Equal(TimeSpan.Zero, device.SleepTimerRemaining);
            Assert.False(device.IsMusicModeOn);
            Assert.Equal(0, device.NightLightBrightness);
            Assert.Equal(ActiveMode.Daylight, device.ActiveMode);
            Assert.Equal("my_bulb", device.Name);
        }

        [Fact]
        public async Task TheRefresh_AsksForEveryPropertyOfTheProtocol_AndTakesOverWhatItGets()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            var names = YeelightProperties.All.ToArray();
            string[] values =
            [
                "off", "7", "2700", "255", "10", "20", "1", "1", "5", "0,1,1000,2,2700,100", "0", "renamed",
                "on", "1", "0,2,500,2,3000,50", "5000", "3", "9", "65280", "100", "50", "30", "1",
            ];
            Assert.Equal(names.Length, values.Length);
            harness.Bulb.ReplyOverride = request => request.Method == "get_prop" && request.Parameters.Length == names.Length ? [Answer(request, values)] : null;

            await harness.Device.RefreshAsync();

            var request = harness.Bulb.RequestsOf("get_prop").Last();
            Assert.Equal(names, request.Parameters.Select(parameter => parameter.GetString()));
            var device = harness.Device;
            Assert.False(device.IsOn);
            Assert.Equal(7, device.Brightness);
            Assert.Equal(2700, device.ColorTemperature);
            Assert.Equal(255, device.Rgb);
            Assert.Equal(10, device.Hue);
            Assert.Equal(20, device.Saturation);
            Assert.Equal(ColorMode.Rgb, device.ColorMode);
            Assert.True(device.IsFlowing);
            Assert.Equal(TimeSpan.FromMinutes(5), device.SleepTimerRemaining);
            Assert.Equal("0,1,1000,2,2700,100", device.FlowParameters);
            Assert.False(device.IsMusicModeOn);
            Assert.Equal("renamed", device.Name);
            Assert.True(device.Background.IsOn);
            Assert.True(device.Background.IsFlowing);
            Assert.Equal("0,2,500,2,3000,50", device.Background.FlowParameters);
            Assert.Equal(5000, device.Background.ColorTemperature);
            Assert.Equal(ColorMode.Hsv, device.Background.ColorMode);
            Assert.Equal(9, device.Background.Brightness);
            Assert.Equal(65280, device.Background.Rgb);
            Assert.Equal(100, device.Background.Hue);
            Assert.Equal(50, device.Background.Saturation);
            Assert.Equal(30, device.NightLightBrightness);
            Assert.Equal(ActiveMode.Moonlight, device.ActiveMode);
        }

        [Fact]
        public async Task ABackgroundLight_HasItsOwnState_AndItsOwnCommands()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true, Model = "ceiling" });
            var device = harness.Device;
            await Wait.UntilAsync(() => device.Background.Brightness == 50, "the state of the background light");

            Assert.True(device.SupportsBackgroundLight);
            Assert.False(device.Background.IsOn);
            Assert.Equal(ColorMode.ColorTemperature, device.Background.ColorMode);
            Assert.Equal(3000, device.Background.ColorTemperature);
            Assert.Equal(255, device.Background.Rgb);
            Assert.Equal(240, device.Background.Hue);
            Assert.Equal(100, device.Background.Saturation);
            Assert.True(device.IsOn);
            Assert.Equal(100, device.Brightness);

            await device.Background.TurnOnAsync();
            await device.Background.SetBrightnessAsync(80);
            await device.Background.SetRgbAsync(1, 2, 3);

            await Wait.UntilAsync(() => device.Background.IsOn && device.Background.Brightness == 80 && device.Background.Rgb == 0x010203, "the reports about the background light");
            Assert.Equal(ColorMode.Rgb, device.Background.ColorMode);
            Assert.Single(harness.Bulb.RequestsOf("bg_set_power"));
            Assert.Empty(harness.Bulb.RequestsOf("set_bright"));
            Assert.Equal(100, device.Brightness);
            Assert.Equal(ColorMode.ColorTemperature, device.ColorMode);
        }

        [Fact]
        public async Task ALightWithoutABackgroundLight_RefusesItsCommands_WithoutSendingThem()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.Background.TurnOnAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.Background.SetBrightnessAsync(10));

            Assert.Equal(before, harness.Bulb.Requests.Count);
            Assert.False(harness.Device.Background.IsOn);
            Assert.Equal(0, harness.Device.Background.Brightness);
        }

        [Fact]
        public async Task GetProperties_GivesTheValuesByName_AndAnUnknownNameIsEmpty()
        {
            await using var harness = await Harness.StartAsync();

            var values = await harness.Device.GetPropertiesAsync(["power", "bright", "no_such_property", "name"]);

            Assert.Equal(4, values.Count);
            Assert.Equal("on", values["power"]);
            Assert.Equal("100", values["bright"]);
            Assert.Equal(string.Empty, values["no_such_property"]);
            Assert.Equal("my_bulb", values["name"]);
        }

        [Fact]
        public async Task GetProperties_TakesOverWhatTheLightSays()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            harness.Bulb.ReplyOverride = request => request.Method == "get_prop" && request.Parameters.Length == 2 ? [Answer(request, "off", "42")] : null;

            var values = await harness.Device.GetPropertiesAsync(["power", "bright"]);

            Assert.Equal("off", values["power"]);
            Assert.False(harness.Device.IsOn);
            Assert.Equal(42, harness.Device.Brightness);
        }

        [Fact]
        public async Task GetProperties_ReturnsEmptyValuesForNamesThatTheAnswerHasNoValueFor()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ReplyOverride = request => request.Method == "get_prop" && request.Parameters.Length == 3 ? [Answer(request, "on")] : null;

            var values = await harness.Device.GetPropertiesAsync(["power", "bright", "ct"]);

            Assert.Equal("on", values["power"]);
            Assert.Equal(string.Empty, values["bright"]);
            Assert.Equal(string.Empty, values["ct"]);
        }

        [Fact]
        public async Task GetProperties_RefusesNamesThatAreNotNames_WithoutSendingAnything()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Device.GetPropertiesAsync(null!));
            await Assert.ThrowsAsync<ArgumentException>(() => harness.Device.GetPropertiesAsync([]));
            await Assert.ThrowsAsync<ArgumentException>(() => harness.Device.GetPropertiesAsync(["power", " "]));
            await Assert.ThrowsAsync<ArgumentException>(() => harness.Device.GetPropertiesAsync(["power", string.Empty]));

            Assert.Equal(before, harness.Bulb.Requests.Count);
        }

        [Fact]
        public async Task ALightThatDoesNotListGetProp_IsNotAskedForProperties()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Support = "set_power toggle" });

            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.GetPropertiesAsync(["power"]));
            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.RefreshAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.SetBrightnessAsync(10));

            Assert.Empty(harness.Bulb.Requests);
            await harness.Device.TurnOffAsync();
            Assert.Single(harness.Bulb.Requests);
        }

        [Fact]
        public async Task SetName_StoresTheNameOnTheLight_AndTheDeviceFollows()
        {
            await using var harness = await Harness.StartAsync();

            await harness.Device.SetNameAsync("kitchen");

            await Wait.UntilAsync(() => harness.Device.Name == "kitchen", "the report of the new name");
            Assert.Equal("kitchen", harness.Bulb.Name);
            Assert.Equal("kitchen", harness.Bulb.RequestsOf("set_name").Single().Parameters[0].GetString());
        }

        [Fact]
        public async Task SetName_AcceptsExactly64BytesInUtf8_AndRefusesMore_WithoutSendingAnything()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAsync<ArgumentException>(() => harness.Device.SetNameAsync(new string('a', 65)));
            await Assert.ThrowsAsync<ArgumentException>(() => harness.Device.SetNameAsync(new string('\u00e9', 33)));
            await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Device.SetNameAsync(null!));
            Assert.Equal(before, harness.Bulb.Requests.Count);

            var multibyte = new string('\u00e9', 32);
            await harness.Device.SetNameAsync(new string('a', 64));
            await harness.Device.SetNameAsync(multibyte);

            await Wait.UntilAsync(() => harness.Device.Name == multibyte, "the report of the name of 64 bytes");
            Assert.Equal(2, harness.Bulb.RequestsOf("set_name").Count);
        }

        [Fact]
        public async Task TheSleepTimer_CanBeSet_ReadAndCancelled()
        {
            await using var harness = await Harness.StartAsync();
            Assert.Null(await harness.Device.GetSleepTimerAsync());

            await harness.Device.SetSleepTimerAsync(TimeSpan.FromMinutes(15));

            await Wait.UntilAsync(() => harness.Device.SleepTimerRemaining == TimeSpan.FromMinutes(15), "the report of the timer");
            Assert.Equal(TimeSpan.FromMinutes(15), await harness.Device.GetSleepTimerAsync());
            var added = harness.Bulb.RequestsOf("cron_add").Single();
            Assert.Equal(0, added.Parameters[0].GetInt32());
            Assert.Equal(15, added.Parameters[1].GetInt32());

            await harness.Device.CancelSleepTimerAsync();

            await Wait.UntilAsync(() => harness.Device.SleepTimerRemaining == TimeSpan.Zero, "the report that the timer is gone");
            Assert.Null(await harness.Device.GetSleepTimerAsync());
            Assert.Equal(0, harness.Bulb.RequestsOf("cron_del").Single().Parameters[0].GetInt32());
        }

        [Fact]
        public async Task TheSleepTimer_IsInWholeMinutes_RoundedUp_AndAtLeastOne()
        {
            await using var harness = await Harness.StartAsync();

            await harness.Device.SetSleepTimerAsync(TimeSpan.FromSeconds(61));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetSleepTimerAsync(TimeSpan.FromSeconds(59)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetSleepTimerAsync(TimeSpan.Zero));

            Assert.Equal(2, harness.Bulb.RequestsOf("cron_add").Single().Parameters[1].GetInt32());
        }

        [Fact]
        public async Task ASleepTimer_IsRefusedByALightThatIsOff()
        {
            await using var harness = await Harness.StartAsync();
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.SetSleepTimerAsync(TimeSpan.FromMinutes(5)));

            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
            Assert.Equal(TimeSpan.Zero, harness.Device.SleepTimerRemaining);
        }

        [Fact]
        public async Task ToggleAll_SwitchesTheMainLightAndTheBackgroundLightTogether()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            var device = harness.Device;
            await device.Background.TurnOnAsync();
            await Wait.UntilAsync(() => device.IsOn && device.Background.IsOn, "both lights to be on");

            await device.ToggleAllAsync();

            await Wait.UntilAsync(() => !device.IsOn && !device.Background.IsOn, "both lights to be off");
            await device.ToggleAllAsync();
            await Wait.UntilAsync(() => device.IsOn && device.Background.IsOn, "both lights to be on again");
            Assert.Equal(2, harness.Bulb.RequestsOf("dev_toggle").Count);
        }

        [Fact]
        public async Task ToggleAll_IsNotSentToALightThatHasNoBackgroundLight()
        {
            await using var harness = await Harness.StartAsync();

            await Assert.ThrowsAsync<NotSupportedException>(() => harness.Device.ToggleAllAsync());

            Assert.Empty(harness.Bulb.RequestsOf("dev_toggle"));
        }

        [Fact]
        public async Task Toggle_SwitchesTheLightWithoutKnowingWhatItIs()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);

            await harness.Device.ToggleAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");
            await harness.Device.ToggleAsync();

            await Wait.UntilAsync(() => harness.Device.IsOn, "the light to be on");
            Assert.Equal(2, harness.Bulb.RequestsOf("toggle").Count);
        }

        [Fact]
        public async Task TheColourCommands_ChangeTheModeOfTheLight()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;

            await device.SetHsvAsync(120, 50);
            await Wait.UntilAsync(() => device.ColorMode == ColorMode.Hsv && device.Hue == 120 && device.Saturation == 50, "the light to show a hue");
            await device.SetRgbAsync(255, 128, 0);
            await Wait.UntilAsync(() => device.ColorMode == ColorMode.Rgb && device.Rgb == 0xFF8000, "the light to show a colour");
            await device.SetColorTemperatureAsync(2700);

            await Wait.UntilAsync(() => device.ColorMode == ColorMode.ColorTemperature && device.ColorTemperature == 2700, "the light to show a colour temperature");
            Assert.Equal(Color.FromArgb(255, 255, 128, 0), device.Color);
        }

        [Fact]
        public async Task ATransition_IsSmoothWhenThereIsOne_AndSuddenWhenThereIsNot()
        {
            await using var harness = await Harness.StartAsync();

            await harness.Device.SetBrightnessAsync(10);
            await harness.Device.SetBrightnessAsync(20, TimeSpan.Zero);
            await harness.Device.SetBrightnessAsync(30, TimeSpan.FromMilliseconds(30));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetBrightnessAsync(40, TimeSpan.FromMilliseconds(29)));

            var sent = harness.Bulb.RequestsOf("set_bright");
            Assert.Equal(3, sent.Count);
            Assert.Equal(new[] { "sudden", "sudden", "smooth" }, sent.Select(request => request.Parameters[1].GetString()));
            Assert.Equal(new[] { 0, 0, 30 }, sent.Select(request => request.Parameters[2].GetInt32()));
        }

        [Fact]
        public async Task ValuesThatTheLightWouldRefuse_AreRefusedBeforeTheyAreSent()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetBrightnessAsync(101));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetBrightnessAsync(0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetColorTemperatureAsync(1699));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetColorTemperatureAsync(6501));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetRgbAsync(256, 0, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetHsvAsync(360, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetHsvAsync(0, 101));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.AdjustBrightnessAsync(101));

            Assert.Equal(before, harness.Bulb.Requests.Count);
        }

        [Fact]
        public async Task SwitchingOn_InAMode_SendsTheModeAndTheLightShowsIt()
        {
            await using var harness = await Harness.StartAsync();
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            await harness.Device.TurnOnAsync(null, PowerOnMode.Rgb);

            await Wait.UntilAsync(() => harness.Device.IsOn && harness.Device.ColorMode == ColorMode.Rgb, "the light to be on in the colour mode");
            var on = harness.Bulb.RequestsOf("set_power").Last();
            Assert.Equal(4, on.Parameters.Length);
            Assert.Equal((int)PowerOnMode.Rgb, on.Parameters[3].GetInt32());
        }

        [Fact]
        public async Task SetDefault_IsSentToALightThatIsOn()
        {
            await using var harness = await Harness.StartAsync();

            await harness.Device.SetDefaultAsync();

            Assert.Single(harness.Bulb.RequestsOf("set_default"));
        }

        [Fact]
        public async Task AScene_SetsWhatItSays_AndSwitchesALightThatIsOffOn()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            await device.TurnOffAsync();
            await Wait.UntilAsync(() => !device.IsOn, "the light to be off");

            await device.SetSceneAsync(YeelightScene.Rgb(10, 20, 30, 40));
            await Wait.UntilAsync(() => device.IsOn && device.Rgb == 0x0A141E && device.Brightness == 40 && device.ColorMode == ColorMode.Rgb, "the colour scene");
            await device.SetSceneAsync(YeelightScene.Hsv(200, 60, 70));
            await Wait.UntilAsync(() => device.Hue == 200 && device.Saturation == 60 && device.Brightness == 70 && device.ColorMode == ColorMode.Hsv, "the scene of a hue");
            await device.SetSceneAsync(YeelightScene.ColorTemperature(3300, 55));
            await Wait.UntilAsync(() => device.ColorTemperature == 3300 && device.Brightness == 55 && device.ColorMode == ColorMode.ColorTemperature, "the scene of a colour temperature");
            await device.SetSceneAsync(YeelightScene.AutoDelayOff(25, 10));

            await Wait.UntilAsync(() => device.Brightness == 25 && device.SleepTimerRemaining == TimeSpan.FromMinutes(10), "the scene that switches the light off");
            Assert.Equal(4, harness.Bulb.RequestsOf("set_scene").Count);
            Assert.Equal("color", harness.Bulb.RequestsOf("set_scene").First().Parameters[0].GetString());
        }

        [Fact]
        public async Task AColorFlow_RunsOnTheLight_AndItsParametersAreReadBackAsTheSameFlow()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            var flow = new ColorFlow(
                [
                    ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(1000), 100),
                    ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMilliseconds(500), 50),
                    ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(250)),
                    ColorFlowStep.Rgb(0, 0, 255, TimeSpan.FromMilliseconds(100)),
                ],
                count: 6,
                action: ColorFlowAction.Stay);

            await device.StartColorFlowAsync(flow);

            await Wait.UntilAsync(() => device.IsFlowing && device.FlowParameters.Length > 0, "the light to report the flow");
            Assert.True(ColorFlow.TryParse(device.FlowParameters, out var reported));
            Assert.Equal(6, reported!.Count);
            Assert.Equal(ColorFlowAction.Stay, reported.Action);
            Assert.Equal(flow.Steps.Count, reported.Steps.Count);
            for (var index = 0; index < flow.Steps.Count; index++)
            {
                Assert.Equal(flow.Steps[index].Mode, reported.Steps[index].Mode);
                Assert.Equal(flow.Steps[index].Value, reported.Steps[index].Value);
                Assert.Equal(flow.Steps[index].Brightness, reported.Steps[index].Brightness);
                Assert.Equal(flow.Steps[index].Duration, reported.Steps[index].Duration);
            }

            Assert.Equal(flow.ToString(), device.FlowParameters);

            await device.StopColorFlowAsync();

            await Wait.UntilAsync(() => !device.IsFlowing && device.FlowParameters.Length == 0, "the flow to stop");
        }

        [Fact]
        public async Task AFlowOfTheMainLight_AndAFlowOfTheBackgroundLight_AreReportedEachOnItsOwn()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            var device = harness.Device;
            var flow = new ColorFlow([ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(500)), ColorFlowStep.Rgb(0, 255, 0, TimeSpan.FromMilliseconds(500))]);
            await device.Background.TurnOnAsync();

            await device.SetSceneAsync(YeelightScene.Flow(flow));
            await Wait.UntilAsync(() => device.IsFlowing, "the main light to flow");
            await device.Background.StartColorFlowAsync(flow);

            await Wait.UntilAsync(() => device.Background.IsFlowing, "the background light to flow");
            await device.Background.StopColorFlowAsync();
            await Wait.UntilAsync(() => !device.Background.IsFlowing, "the background flow to stop");
            Assert.True(device.IsFlowing);
            Assert.Equal(flow.ToString(), device.FlowParameters);
        }

        [Fact]
        public async Task TheAdjustCommands_AreSentWithTheirParameters_AndTheStateFollowsTheLight()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            await device.SetBrightnessAsync(50);
            await Wait.UntilAsync(() => device.Brightness == 50, "the brightness");

            await device.AdjustAsync(AdjustAction.Decrease, AdjustProperty.Brightness);
            await Wait.UntilAsync(() => device.Brightness == int.Parse(harness.Bulb.Get("bright")) && device.Brightness < 50, "the brightness to follow the adjustment");
            await device.AdjustAsync(AdjustAction.Circle, AdjustProperty.Color);
            await device.AdjustAsync(AdjustAction.Increase, AdjustProperty.ColorTemperature);
            await device.AdjustBrightnessAsync(-20, TimeSpan.FromMilliseconds(500));
            await device.AdjustColorTemperatureAsync(10);
            await device.AdjustColorAsync();
            await Assert.ThrowsAsync<ArgumentException>(() => device.AdjustAsync(AdjustAction.Increase, AdjustProperty.Color));

            var adjusts = harness.Bulb.RequestsOf("set_adjust");
            Assert.Equal(3, adjusts.Count);
            Assert.Equal(new[] { "decrease", "bright" }, adjusts[0].Parameters.Select(parameter => parameter.GetString()));
            Assert.Equal(new[] { "circle", "color" }, adjusts[1].Parameters.Select(parameter => parameter.GetString()));
            Assert.Equal(new[] { "increase", "ct" }, adjusts[2].Parameters.Select(parameter => parameter.GetString()));
            var brightness = harness.Bulb.RequestsOf("adjust_bright").Single();
            Assert.Equal(-20, brightness.Parameters[0].GetInt32());
            Assert.Equal(500, brightness.Parameters[1].GetInt32());
            Assert.Equal(10, harness.Bulb.RequestsOf("adjust_ct").Single().Parameters[0].GetInt32());
            Assert.Equal((int)YeelightLimits.MinTransition.TotalMilliseconds, harness.Bulb.RequestsOf("adjust_color").Single().Parameters[1].GetInt32());
        }

        [Fact]
        public async Task SendCommand_GivesTheAnswerOfTheLight_AndAFailureIsAResultAndNotAnException()
        {
            await using var harness = await Harness.StartAsync();

            var properties = await harness.Device.SendCommandAsync("get_prop", ["power", "bright"]);
            var unknown = await harness.Device.SendCommandAsync("no_such_method");
            var refused = await harness.Device.SendCommandAsync("set_bright", [500, "sudden", 0]);

            Assert.True(properties.Success);
            Assert.Equal("on", properties.GetString(0));
            Assert.Equal("100", properties.GetString(1));
            Assert.Null(properties.GetString(2));
            Assert.False(unknown.Success);
            Assert.Equal("unsupported method", unknown.Error!.Message);
            Assert.Equal(YeelightFailureKind.DeviceRejected, unknown.Error.Kind);
            Assert.False(unknown.Error.IsTransportFailure);
            Assert.False(refused.Success);
            Assert.Equal(-1, refused.Error!.Code);
            Assert.Throws<YeelightException>(() => refused.EnsureSuccess("set_bright"));
        }

        [Fact]
        public async Task ANormalSession_LogsNothingAboveInformation()
        {
            await using var harness = await Harness.StartAsync();
            await RefreshedAsync(harness);

            await harness.Device.SetBrightnessAsync(70);
            await harness.Device.ToggleAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn && harness.Device.Brightness == 70, "the reports of the light");

            Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
        }

        [Fact]
        public async Task ALightThatIsOff_TakesTheCommandsThatItAccepts_AndReportsThem()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Name = "lamp" });
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            await harness.Device.SetNameAsync("night lamp");
            await harness.Device.ToggleAsync();

            await Wait.UntilAsync(() => harness.Device.IsOn && harness.Device.Name == "night lamp", "the reports of the light");
        }
    }
}
