using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Yeelight.Tests.TestSupport;
using Yeelight.TestApp.Controls;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Views;

namespace Yeelight.TestApp;

internal static partial class SmokeTest
{
    private sealed partial class Scenario
    {
        private static void Succeeded(Outcome? outcome, string what)
        {
            Check(outcome is { IsSuccess: true }, $"{what}: the page reports success ({outcome?.Summary})");
        }

        private static Color Rgb(byte red, byte green, byte blue) => Color.FromArgb(255, red, green, blue);

        private async Task<Outcome?> DoAsync(ControlPage page, SimulatedBulb bulb, string what, Action trigger, params string[] expected)
        {
            Outcome? outcome = null;
            await ExpectAsync(bulb, what, async () => outcome = await RunAsync(page, trigger), expected);
            return outcome;
        }

        private async Task<ControlPage> ShowLightAsync(SimulatedKind kind, string tab = "light")
        {
            var item = Item(kind);
            Session.Select(item, byUser: true);
            var page = await this.OpenAsync<ControlPage>("control");
            await UntilAsync(() => page.Current == item, $"the Control page shows {item.Title}");
            page.SelectTab(tab);
            await Task.Delay(120);
            return page;
        }

        private async Task ControlColorAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            var page = await this.ShowLightAsync(SimulatedKind.Color);
            var panel = page.MainLightPanel;
            T P<T>(string name)
                where T : class => Find<T>(panel, name);
            Button B(string name) => Find<Button>(panel, name);

            Check(!page.ShowsEmptyText && panel.IsEnabled, "the Control page has a light to control");
            Check(page.Summary.Contains("Simulated colour bulb", StringComparison.Ordinal) && page.Summary.Contains("On", StringComparison.Ordinal), $"the live state names the light and says that it is on ({page.Summary})");
            Check(page.StateRows.Any(row => row.Name == "Brightness" && row.Value == "100%") && page.StateRows.Any(row => row.Name == "Colour temperature" && row.Value == "4000 K"), "the live state shows the brightness and the colour temperature");
            Check(Find<SelectorBarItem>(page, "BackgroundTab").Visibility == Visibility.Collapsed, "a light without a background light has no Background light tab");
            Check(P<Slider>("BrightnessSlider").Value == 100 && P<ToggleSwitch>("PowerSwitch").IsOn, "the controls show what the light is now");

            // Power, with the switch.
            Succeeded(await this.DoAsync(page, bulb, "the power switch turns the light off", () => Flip(P<ToggleSwitch>("PowerSwitch"), "power"), "set_power [\"off\",\"sudden\",0]"), "turning the light off");
            await UntilAsync(() => !item.Device.IsOn && !P<ToggleSwitch>("PowerSwitch").IsOn && page.Summary.Contains("Off", StringComparison.Ordinal), "the state follows: the light is off, the switch is off, the summary says Off");

            // A command that the light refuses is reported, and the page goes on.
            P<Slider>("BrightnessSlider").Value = 35;
            var refused = await this.DoAsync(page, bulb, "setting the brightness of a light that is off", () => Click(B("ApplyBrightnessButton"), "Apply brightness"), "set_bright [35,\"sudden\",0]");
            Check(refused is { Severity: OutcomeSeverity.Warning } && refused.Message.Contains("-1", StringComparison.Ordinal) && refused.Title.Contains("refused", StringComparison.Ordinal), $"the refusal is shown with its error code ({refused?.Summary})");
            Check(page.OutcomeText.Contains("refused", StringComparison.Ordinal) && Find<InfoBar>(page, "OutcomeBar").Severity == InfoBarSeverity.Warning, "the bar at the bottom shows the refusal as a warning");
            await UntilAsync(() => P<Slider>("BrightnessSlider").Value == 100, "the slider goes back to what the light says");

            // Turn on in a mode, as a person chooses it in the list.
            P<ComboBox>("ModeBox").SelectedIndex = 2;
            Succeeded(await this.DoAsync(page, bulb, "Turn on in colour mode", () => Click(B("TurnOnButton"), "Turn on"), "set_power [\"on\",\"sudden\",0,2]"), "turning on in a mode");
            await UntilAsync(() => item.Device.IsOn && item.Device.ColorMode == ColorMode.Rgb && P<ToggleSwitch>("PowerSwitch").IsOn, "the light is on, in colour mode, and the switch follows");

            // The transition applies to every command that takes one.
            var transitionSwitch = Find<ToggleSwitch>(page, "TransitionSwitch");
            var transitionBox = Find<NumberBox>(page, "TransitionBox");
            Check(((ICommandHost)page).Transition is null, "the transition is off at the start");
            transitionSwitch.IsOn = true;
            transitionBox.Value = 500;
            Check(((ICommandHost)page).Transition == TimeSpan.FromMilliseconds(500), "the transition is 500 ms when it is on");
            P<Slider>("BrightnessSlider").Value = 35;
            Succeeded(await this.DoAsync(page, bulb, "brightness with a transition", () => Click(B("ApplyBrightnessButton"), "Apply brightness"), "set_bright [35,\"smooth\",500]"), "setting the brightness");
            await UntilAsync(() => item.Device.Brightness == 35 && P<Slider>("BrightnessSlider").Value == 35 && page.StateRows.Any(row => row.Name == "Brightness" && row.Value == "35%"), "the brightness is 35 on the light, the slider and the live state");
            transitionBox.Value = 10;
            Check(((ICommandHost)page).Transition == TimeSpan.FromMilliseconds(30), "a transition is never shorter than 30 ms");
            P<Slider>("TemperatureSlider").Value = 3000;
            Succeeded(await this.DoAsync(page, bulb, "colour temperature with the shortest transition", () => Click(B("ApplyTemperatureButton"), "Apply colour temperature"), "set_ct_abx [3000,\"smooth\",30]"), "setting the colour temperature");
            await UntilAsync(() => item.Device.ColorTemperature == 3000 && item.Device.ColorMode == ColorMode.ColorTemperature && page.StateRows.Any(row => row.Name == "Colour mode" && row.Value == "Colour temperature"), "the light is in colour temperature mode, at 3000 K");
            transitionSwitch.IsOn = false;
            Check(((ICommandHost)page).Transition is null, "turning the transition off makes the commands sudden again");

            // Colour: the picker, then hue and saturation.
            P<ColorPicker>("RgbPicker").Color = Rgb(255, 140, 0);
            Succeeded(await this.DoAsync(page, bulb, "colour from the picker", () => Click(B("ApplyRgbButton"), "Apply colour"), "set_rgb [16747520,\"sudden\",0]"), "setting the colour");
            await UntilAsync(() => item.Device.Rgb == 16747520 && item.Device.ColorMode == ColorMode.Rgb && page.StateRows.Any(row => row.Name == "Colour mode" && row.Value == "Colour (RGB)"), "the light shows #FF8C00 in colour mode");
            P<Slider>("HueSlider").Value = 200;
            P<Slider>("SaturationSlider").Value = 80;
            Succeeded(await this.DoAsync(page, bulb, "hue and saturation", () => Click(B("ApplyHsvButton"), "Apply hue and saturation"), "set_hsv [200,80,\"sudden\",0]"), "setting hue and saturation");
            await UntilAsync(() => item.Device.Hue == 200 && item.Device.Saturation == 80 && item.Device.ColorMode == ColorMode.Hsv, "the light shows hue 200 and saturation 80");
            Check(item.SwatchBrush is Microsoft.UI.Xaml.Media.SolidColorBrush { Color: { A: 255 } }, "the swatch of the light is a colour");

            // Toggle, twice, and Set default.
            Succeeded(await this.DoAsync(page, bulb, "Toggle (off)", () => Click(B("ToggleButton"), "Toggle"), "toggle []"), "toggling");
            await UntilAsync(() => !item.Device.IsOn, "the light is off after Toggle");
            Succeeded(await this.DoAsync(page, bulb, "Toggle (on)", () => Click(B("ToggleButton"), "Toggle"), "toggle []"), "toggling back");
            await UntilAsync(() => item.Device.IsOn, "the light is on after the second Toggle");
            Succeeded(await this.DoAsync(page, bulb, "Set default", () => Click(B("DefaultButton"), "Set default"), "set_default []"), "Set default");

            // Adjust.
            var action = P<ComboBox>("AdjustActionBox");
            var property = P<ComboBox>("AdjustPropertyBox");
            action.SelectedIndex = 0;
            property.SelectedIndex = 0;
            var before = item.Device.Brightness;
            Succeeded(await this.DoAsync(page, bulb, "Adjust: increase the brightness", () => Click(B("AdjustButton"), "Adjust"), "set_adjust [\"increase\",\"bright\"]"), "adjusting");
            await UntilAsync(() => item.Device.Brightness == int.Parse(bulb.Get("bright"), System.Globalization.CultureInfo.InvariantCulture), $"the brightness follows what the light did ({before} to {bulb.Get("bright")})");
            action.SelectedIndex = 2;
            property.SelectedIndex = 2;
            Succeeded(await this.DoAsync(page, bulb, "Adjust: circle the colour", () => Click(B("AdjustButton"), "Adjust"), "set_adjust [\"circle\",\"color\"]"), "adjusting the colour");
            action.SelectedIndex = 0;
            var rejected = await this.DoAsync(page, bulb, "Adjust: increase the colour, which the protocol does not allow", () => Click(B("AdjustButton"), "Adjust"));
            Check(rejected is { Severity: OutcomeSeverity.Warning } && rejected.Title.Contains("refused the arguments", StringComparison.Ordinal), $"the library refuses it before anything is sent, and the page says so ({rejected?.Summary})");
            P<NumberBox>("PercentBox").Value = -20;
            P<NumberBox>("AdjustDurationBox").Value = 300;
            Succeeded(await this.DoAsync(page, bulb, "adjust brightness by -20 percent in 300 ms", () => Click(B("AdjustBrightnessButton"), "Adjust brightness"), "adjust_bright [-20,300]"), "adjusting the brightness by a percentage");
            P<NumberBox>("PercentBox").Value = 15;
            P<NumberBox>("AdjustDurationBox").Value = 300;
            Succeeded(await this.DoAsync(page, bulb, "adjust colour temperature by 15 percent in 300 ms", () => Click(B("AdjustTemperatureButton"), "Adjust colour temperature"), "adjust_ct [15,300]"), "adjusting the colour temperature by a percentage");
            Succeeded(await this.DoAsync(page, bulb, "move the colour on in 300 ms", () => Click(B("AdjustColorButton"), "Adjust colour"), "adjust_color [0,300]"), "moving the colour on");

            // Scenes: colour, hue and saturation, colour temperature, and on-and-off-after-a-delay.
            var scene = P<ComboBox>("SceneClassBox");
            P<NumberBox>("SceneBrightnessBox").Value = 60;
            scene.SelectedIndex = 0;
            P<NumberBox>("SceneRedBox").Value = 255;
            P<NumberBox>("SceneGreenBox").Value = 140;
            P<NumberBox>("SceneBlueBox").Value = 0;
            Succeeded(await this.DoAsync(page, bulb, "colour scene", () => Click(B("SetSceneButton"), "Set scene"), "set_scene [\"color\",16747520,60]"), "the colour scene");
            scene.SelectedIndex = 1;
            P<NumberBox>("SceneHueBox").Value = 200;
            P<NumberBox>("SceneSaturationBox").Value = 80;
            Succeeded(await this.DoAsync(page, bulb, "hue and saturation scene", () => Click(B("SetSceneButton"), "Set scene"), "set_scene [\"hsv\",200,80,60]"), "the HSV scene");
            scene.SelectedIndex = 2;
            P<NumberBox>("SceneKelvinBox").Value = 2700;
            Succeeded(await this.DoAsync(page, bulb, "colour temperature scene", () => Click(B("SetSceneButton"), "Set scene"), "set_scene [\"ct\",2700,60]"), "the colour temperature scene");
            scene.SelectedIndex = 4;
            P<NumberBox>("SceneMinutesBox").Value = 15;
            Succeeded(await this.DoAsync(page, bulb, "auto delay off scene", () => Click(B("SetSceneButton"), "Set scene"), "set_scene [\"auto_delay_off\",60,15]"), "the scene that turns the light off after a delay");
            await UntilAsync(() => item.Device.SleepTimerRemaining == TimeSpan.FromMinutes(15) && page.StateRows.Any(row => row.Name == "Sleep timer" && row.Value == "15 min left"), "SleepTimerRemaining is 15 minutes, in the live state as well");

            Find<ScrollViewer>(page, "LightView").ChangeView(null, 0, null, disableAnimation: true);
            await this.ShotAsync("control");
        }

        private async Task ControlFlowAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            var page = await this.ShowLightAsync(SimulatedKind.Color);
            var panel = page.MainLightPanel;
            T P<T>(string name)
                where T : class => Find<T>(panel, name);
            Button B(string name) => Find<Button>(panel, name);

            // Build a flow the way a person does: add three steps.
            Check(panel.Flow.Steps.Count == 0 && !B("ClearStepsButton").IsEnabled && !B("StartFlowButton").IsEnabled, "the editor starts empty: Clear and Start have nothing to work on");
            P<ComboBox>("StepKindBox").SelectedIndex = 0;
            P<NumberBox>("StepDurationBox").Value = 1000;
            P<ColorButton>("StepColorButton").Color = Rgb(255, 0, 0);
            P<NumberBox>("StepBrightnessBox").Value = 100;
            P<CheckBox>("StepKeepBox").IsChecked = false;
            Click(B("AddStepButton"), "Add");
            P<ComboBox>("StepKindBox").SelectedIndex = 1;
            P<NumberBox>("StepDurationBox").Value = 800;
            P<NumberBox>("StepKelvinBox").Value = 2700;
            P<CheckBox>("StepKeepBox").IsChecked = true;
            Click(B("AddStepButton"), "Add");
            P<ComboBox>("StepKindBox").SelectedIndex = 2;
            P<NumberBox>("StepDurationBox").Value = 500;
            Click(B("AddStepButton"), "Add");
            Check(panel.Flow.Steps.Select(step => step.Kind).SequenceEqual([FlowStepKind.Color, FlowStepKind.ColorTemperature, FlowStepKind.Sleep]), "three steps were added: a colour, a colour temperature and a sleep");

            // Move, replace and remove.
            var list = P<ListView>("StepList");
            list.SelectedIndex = 2;
            Click(B("UpStepButton"), "Up");
            Check(panel.Flow.Steps.Select(step => step.Kind).SequenceEqual([FlowStepKind.Color, FlowStepKind.Sleep, FlowStepKind.ColorTemperature]), "Up moves the sleep step before the colour temperature step");
            Click(B("DownStepButton"), "Down");
            Check(panel.Flow.Steps.Select(step => step.Kind).SequenceEqual([FlowStepKind.Color, FlowStepKind.ColorTemperature, FlowStepKind.Sleep]), "Down moves it back");
            list.SelectedIndex = 0;
            P<NumberBox>("StepBrightnessBox").Value = 80;
            P<CheckBox>("StepKeepBox").IsChecked = false;
            Click(B("ReplaceStepButton"), "Replace");
            Check(panel.Flow.Steps[0] == FlowStepModel.Color(0xFF0000, 1000, 80), $"Replace changed the first step to red at 80 percent ({panel.Flow.Steps[0].Description})");
            list.SelectedIndex = 2;
            Click(B("RemoveStepButton"), "Remove");
            Check(panel.Flow.Steps.Count == 2, "Remove took the sleep step out");
            P<ComboBox>("StepKindBox").SelectedIndex = 2;
            P<NumberBox>("StepDurationBox").Value = 500;
            Click(B("AddStepButton"), "Add");
            Check(panel.Flow.Steps.Count == 3 && panel.Flow.Steps[2].Kind == FlowStepKind.Sleep, "the sleep step was added again, at the end");

            P<NumberBox>("FlowCountBox").Value = 4;
            P<ComboBox>("FlowActionBox").SelectedIndex = 1;
            Check(panel.Flow.Count == 4 && panel.Flow.Action == ColorFlowAction.Stay, "the count and the action of the flow are those that were chosen");
            var expression = "1000,1,16711680,80,800,2,2700,-1,500,7,0,0";
            Check(P<TextBlock>("FlowSummary").Text.Contains("3 steps", StringComparison.Ordinal) && P<TextBlock>("FlowSummary").Text.Contains(expression, StringComparison.Ordinal), $"the editor shows the flow as the light has it ({P<TextBlock>("FlowSummary").Text.Replace("\n", " / ")})");

            // Start it, and see that the light flows, and what it reports.
            Succeeded(await this.DoAsync(page, bulb, "Start the colour flow", () => Click(B("StartFlowButton"), "Start"), $"start_cf [4,1,\"{expression}\"]"), "starting the flow");
            await UntilAsync(() => item.Device.IsFlowing && item.Device.FlowParameters == $"4,1,{expression}", "IsFlowing is true and FlowParameters is what was sent");
            await UntilAsync(() => P<TextBlock>("FlowStatus").Text.Contains("is flowing", StringComparison.Ordinal) && P<TextBlock>("FlowStatus").Text.Contains("3 steps", StringComparison.Ordinal), $"the page shows that the light flows and the flow that it reports, as ColorFlow.TryParse reads it ({P<TextBlock>("FlowStatus").Text})");
            Check(ColorFlow.TryParse(item.Device.FlowParameters, out var parsed) && parsed is { Count: 4, Action: ColorFlowAction.Stay } && parsed.Steps.Count == 3, "ColorFlow.TryParse reads the flow that the light reports");
            Click(B("ClearStepsButton"), "Clear");
            Click(B("LoadFromLightButton"), "Load from the light");
            Check(panel.Flow.Steps.Count == 3 && panel.Flow.Count == 4 && panel.Flow.Action == ColorFlowAction.Stay, "Load from the light puts the flow of the light in the editor");
            Succeeded(await this.DoAsync(page, bulb, "Stop the colour flow", () => Click(B("StopFlowButton"), "Stop"), "stop_cf []"), "stopping the flow");
            await UntilAsync(() => !item.Device.IsFlowing && P<TextBlock>("FlowStatus").Text.Contains("not flowing", StringComparison.Ordinal), "the light is no longer flowing, and the page says so");

            // The same flow as a scene.
            P<ComboBox>("SceneClassBox").SelectedIndex = 3;
            Check(P<TextBlock>("SceneFlowText").Visibility == Visibility.Visible && P<TextBlock>("SceneFlowText").Text.Contains("3 steps", StringComparison.Ordinal), "the flow scene says which flow it starts");
            Succeeded(await this.DoAsync(page, bulb, "colour flow scene", () => Click(B("SetSceneButton"), "Set scene"), $"set_scene [\"cf\",4,1,\"{expression}\"]"), "the flow scene");
            Succeeded(await this.DoAsync(page, bulb, "Stop the colour flow again", () => Click(B("StopFlowButton"), "Stop"), "stop_cf []"), "stopping the flow");

            // A preset.
            P<ComboBox>("PresetBox").SelectedIndex = 0;
            Click(B("LoadPresetButton"), "Load preset");
            Check(panel.Flow.Steps.Count == FlowEditorModel.Presets[0].Steps.Count && panel.Flow.Steps.Count > 2, "a preset fills the editor");
            Find<ScrollViewer>(page, "LightView").ChangeView(null, 0, null, disableAnimation: true);
            await Task.Delay(150);
            ScrollTo(Find<ScrollViewer>(page, "LightView"), P<TextBlock>("FlowStatus"), 120);
            await this.ShotAsync("control-flow");
            Find<ScrollViewer>(page, "LightView").ChangeView(null, 0, null, disableAnimation: true);
        }

        // Scrolls so that an element of the page is near the top of what is shown, for a picture of it.
        private static void ScrollTo(ScrollViewer scroll, FrameworkElement target, double above)
        {
            if (scroll.Content is UIElement content)
            {
                var y = target.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                scroll.ChangeView(null, Math.Max(0, y - above), null, disableAnimation: true);
            }
        }

        private async Task ControlDeviceAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            var page = await this.ShowLightAsync(SimulatedKind.Color, "device");
            Button B(string name) => Find<Button>(page, name);

            Check(Find<TextBlock>(page, "TimerText").Text.Contains("15 minutes", StringComparison.Ordinal), $"the Device tab shows the sleep timer that the scene started ({Find<TextBlock>(page, "TimerText").Text})");
            Find<TextBox>(page, "NameBox").Text = "Desk lamp";
            Succeeded(await this.DoAsync(page, bulb, "Set name", () => Click(B("SetNameButton"), "Set name"), "set_name [\"Desk lamp\"]"), "naming the light");
            await UntilAsync(() => item.Device.Name == "Desk lamp" && item.Title == "Desk lamp" && page.Summary.StartsWith("Desk lamp", StringComparison.Ordinal), "the name of the light follows: Desk lamp, in the live state and in the list");

            Find<NumberBox>(page, "TimerBox").Value = 25;
            Succeeded(await this.DoAsync(page, bulb, "Set sleep timer", () => Click(B("SetTimerButton"), "Set timer"), "cron_add [0,25]"), "setting the sleep timer");
            await UntilAsync(() => item.Device.SleepTimerRemaining == TimeSpan.FromMinutes(25) && Find<TextBlock>(page, "TimerText").Text.Contains("25 minutes", StringComparison.Ordinal) && page.StateRows.Any(row => row.Name == "Sleep timer" && row.Value == "25 min left"), "SleepTimerRemaining is 25 minutes, and the page shows it");
            var got = await this.DoAsync(page, bulb, "Get sleep timer", () => Click(B("GetTimerButton"), "Get timer"), "cron_get [0]");
            Check(got is { IsSuccess: true } && got.Message.Contains("25 minutes", StringComparison.Ordinal), $"Get timer says what the light answered ({got?.Summary})");
            Succeeded(await this.DoAsync(page, bulb, "Cancel sleep timer", () => Click(B("CancelTimerButton"), "Cancel timer"), "cron_del [0]"), "cancelling the sleep timer");
            await UntilAsync(() => item.Device.SleepTimerRemaining == TimeSpan.Zero && Find<TextBlock>(page, "TimerText").Text.Contains("No sleep timer", StringComparison.Ordinal), "the timer is gone, and the page says so");
            Check(!B("ToggleAllButton").IsEnabled, "a light without a background light cannot Toggle both lights");

            // Connection: close it, and make it again.
            var props = bulb.RequestsOf("get_prop").Count;
            Click(B("DisconnectButton"), "Disconnect");
            await UntilAsync(() => !item.Device.IsConnected && bulb.ClientCount == 0 && B("ConnectButton").IsEnabled, "Disconnect closes the connection, on both sides");
            Click(B("ConnectButton"), "Connect");
            await UntilAsync(() => item.Device.IsConnected && bulb.ClientCount == 1 && bulb.RequestsOf("get_prop").Count == props + 1, "Connect makes a connection, and the client reads the state again");
            await Task.Delay(150);

            // Refresh reads every property.
            props = bulb.RequestsOf("get_prop").Count;
            var refreshed = await RunAsync(page, () => Click(B("RefreshButton"), "Refresh"));
            Succeeded(refreshed, "Refresh");
            Check(bulb.RequestsOf("get_prop").Count == props + 1 && bulb.RequestsOf("get_prop")[^1].Parameters.Length >= 20 && bulb.RequestsOf("get_prop")[^1].Parameters.Any(parameter => parameter.GetString() == "active_mode"), "Refresh asked for all the properties with one get_prop");
            await this.ShotAsync("control-device");
        }

        private async Task ControlFollowsAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            var page = await this.ShowLightAsync(SimulatedKind.Color);
            var panel = page.MainLightPanel;
            T P<T>(string name)
                where T : class => Find<T>(panel, name);

            // Another app changes the light: the pages follow.
            await bulb.ChangeAsync(("bright", "20"));
            await UntilAsync(() => item.Device.Brightness == 20 && P<Slider>("BrightnessSlider").Value == 20 && page.StateRows.Any(row => row.Name == "Brightness" && row.Value == "20%") && item.StateText == "On \u00b7 20%", "a change made by another app shows in the slider, the live state and the list (brightness 20)");

            // A slider that a person is moving is not taken away from them.
            P<Slider>("BrightnessSlider").Value = 77;
            await bulb.ChangeAsync(("bright", "30"));
            await UntilAsync(() => item.Device.Brightness == 30 && page.StateRows.Any(row => row.Name == "Brightness" && row.Value == "30%"), "the live state shows 30 percent");
            await Task.Delay(200);
            Check(P<Slider>("BrightnessSlider").Value == 77, "the slider that was moved by hand stays where it is, until Apply");
            Succeeded(await this.DoAsync(page, bulb, "Apply the slider that was moved", () => Click(P<Button>("ApplyBrightnessButton"), "Apply brightness"), "set_bright [77,\"sudden\",0]"), "applying the brightness");
            await UntilAsync(() => item.Device.Brightness == 77 && P<Slider>("BrightnessSlider").Value == 77, "the brightness is 77");

            await bulb.ChangeAsync(("power", "off"));
            await UntilAsync(() => !item.Device.IsOn && !P<ToggleSwitch>("PowerSwitch").IsOn && !item.IsOn, "switching the light off from outside turns the switch off");
            await bulb.ChangeAsync(("power", "on"));
            await UntilAsync(() => item.Device.IsOn && P<ToggleSwitch>("PowerSwitch").IsOn, "switching it on from outside turns the switch on");
        }

        private async Task ControlMonoAsync()
        {
            var bulb = Bulb(SimulatedKind.White);
            var page = await this.ShowLightAsync(SimulatedKind.White);
            var panel = page.MainLightPanel;
            T P<T>(string name)
                where T : class => Find<T>(panel, name);
            await UntilAsync(() => panel.Light == page.Current?.Device, "the panel works on the white bulb");

            Check(!P<Button>("ApplyTemperatureButton").IsEnabled && !P<Button>("ApplyRgbButton").IsEnabled && !P<Button>("ApplyHsvButton").IsEnabled, "a light without colours has no use for the colour temperature, colour and hue buttons");
            Check(!P<ColorPicker>("RgbPicker").IsEnabled && !P<Slider>("HueSlider").IsEnabled && !P<Slider>("TemperatureSlider").IsEnabled, "the sliders and the picker of the colours are disabled too");
            Check(P<TextBlock>("TemperatureNote").Text.Contains("does not list set_ct_abx", StringComparison.Ordinal), $"the page says why ({P<TextBlock>("TemperatureNote").Text})");
            Check(P<Button>("ApplyBrightnessButton").IsEnabled && P<ToggleSwitch>("PowerSwitch").IsEnabled && P<Button>("ToggleButton").IsEnabled, "brightness and power are available");
            Check(P<Button>("AdjustBrightnessButton").IsEnabled && !P<Button>("AdjustTemperatureButton").IsEnabled && !P<Button>("AdjustColorButton").IsEnabled, "the adjustments that the light does not list are disabled");
            Check(Find<SelectorBarItem>(page, "BackgroundTab").Visibility == Visibility.Collapsed, "there is no Background light tab");
            P<Slider>("BrightnessSlider").Value = 20;
            Succeeded(await this.DoAsync(page, bulb, "brightness of the white bulb", () => Click(P<Button>("ApplyBrightnessButton"), "Apply brightness"), "set_bright [20,\"sudden\",0]"), "setting the brightness of the white bulb");
            await UntilAsync(() => Item(SimulatedKind.White).Device.Brightness == 20, "the white bulb is at 20 percent");
            Check(Bulb(SimulatedKind.Color).Requests.All(request => !(request.Method == "set_bright" && Args(request).StartsWith("[20,", StringComparison.Ordinal))), "the command went to the white bulb only");
        }

        private async Task ControlBackgroundAsync()
        {
            var bulb = Bulb(SimulatedKind.Ceiling);
            var item = Item(SimulatedKind.Ceiling);
            var page = await this.ShowLightAsync(SimulatedKind.Ceiling);
            await UntilAsync(() => Find<SelectorBarItem>(page, "BackgroundTab").Visibility == Visibility.Visible, "a light with a background light has a Background light tab");
            page.SelectTab("background");
            await Task.Delay(150);
            var panel = page.BackgroundLightPanel;
            var main = page.MainLightPanel;
            T P<T>(string name)
                where T : class => Find<T>(panel, name);
            Button B(string name) => Find<Button>(panel, name);
            await UntilAsync(() => panel.Light == item.Device.Background && main.Light == item.Device, "one panel works on the main light and the other on the background light");
            var mainBrightness = bulb.Get("bright");
            Check(page.StateRows.Any(row => row.Name == "Background Power" && row.Value == "off") && page.StateRows.Any(row => row.Name == "Background Brightness" && row.Value == "50%"), "the live state shows the background light too");
            Check(!P<ToggleSwitch>("PowerSwitch").IsOn && P<Slider>("BrightnessSlider").Value == 50, "the background panel shows the background light, which is off, at 50 percent");

            Succeeded(await this.DoAsync(page, bulb, "background power on", () => Flip(P<ToggleSwitch>("PowerSwitch"), "background power"), "bg_set_power [\"on\",\"sudden\",0]"), "turning the background light on");
            await UntilAsync(() => item.Device.Background.IsOn && P<ToggleSwitch>("PowerSwitch").IsOn, "the background light is on");
            P<Slider>("BrightnessSlider").Value = 40;
            Succeeded(await this.DoAsync(page, bulb, "background brightness", () => Click(B("ApplyBrightnessButton"), "Apply brightness"), "bg_set_bright [40,\"sudden\",0]"), "the brightness of the background light");
            P<ColorPicker>("RgbPicker").Color = Rgb(255, 140, 0);
            Succeeded(await this.DoAsync(page, bulb, "background colour", () => Click(B("ApplyRgbButton"), "Apply colour"), "bg_set_rgb [16747520,\"sudden\",0]"), "the colour of the background light");
            P<Slider>("HueSlider").Value = 200;
            P<Slider>("SaturationSlider").Value = 80;
            Succeeded(await this.DoAsync(page, bulb, "background hue and saturation", () => Click(B("ApplyHsvButton"), "Apply hue and saturation"), "bg_set_hsv [200,80,\"sudden\",0]"), "the hue of the background light");
            P<Slider>("TemperatureSlider").Value = 2500;
            Succeeded(await this.DoAsync(page, bulb, "background colour temperature", () => Click(B("ApplyTemperatureButton"), "Apply colour temperature"), "bg_set_ct_abx [2500,\"sudden\",0]"), "the colour temperature of the background light");
            await UntilAsync(() => item.Device.Background.Brightness == 40 && item.Device.Background.ColorTemperature == 2500 && item.Device.Background.ColorMode == ColorMode.ColorTemperature, "the background light is at 40 percent, 2500 K");
            Succeeded(await this.DoAsync(page, bulb, "background Toggle", () => Click(B("ToggleButton"), "Toggle"), "bg_toggle []"), "toggling the background light");
            await UntilAsync(() => !item.Device.Background.IsOn, "the background light is off after Toggle");
            Succeeded(await this.DoAsync(page, bulb, "background Toggle back", () => Click(B("ToggleButton"), "Toggle"), "bg_toggle []"), "toggling it back");
            Succeeded(await this.DoAsync(page, bulb, "background Set default", () => Click(B("DefaultButton"), "Set default"), "bg_set_default []"), "Set default of the background light");
            P<ComboBox>("AdjustActionBox").SelectedIndex = 0;
            P<ComboBox>("AdjustPropertyBox").SelectedIndex = 0;
            Succeeded(await this.DoAsync(page, bulb, "background Adjust", () => Click(B("AdjustButton"), "Adjust"), "bg_set_adjust [\"increase\",\"bright\"]"), "adjusting the background light");
            P<ComboBox>("SceneClassBox").SelectedIndex = 0;
            Succeeded(await this.DoAsync(page, bulb, "background scene", () => Click(B("SetSceneButton"), "Set scene"), "bg_set_scene [\"color\",16747520,60]"), "a scene for the background light");
            P<ComboBox>("PresetBox").SelectedIndex = 0;
            Click(B("LoadPresetButton"), "Load preset");
            var flow = panel.Flow.ToFlow().ToString();
            Succeeded(await this.DoAsync(page, bulb, "background colour flow", () => Click(B("StartFlowButton"), "Start"), $"bg_start_cf [{flow.Split(',')[0]},{flow.Split(',')[1]},{Json(string.Join(',', flow.Split(',').Skip(2)))}]"), "starting a flow on the background light");
            await UntilAsync(() => item.Device.Background.IsFlowing && !item.Device.IsFlowing, "the background light flows, and the main light does not");
            Succeeded(await this.DoAsync(page, bulb, "background Stop flow", () => Click(B("StopFlowButton"), "Stop"), "bg_stop_cf []"), "stopping the flow of the background light");
            Check(bulb.Get("bright") == mainBrightness, "the main light was not touched by all this");
            await this.ShotAsync("control-background");

            // Toggle both lights, from the Device tab.
            page.SelectTab("device");
            await Task.Delay(120);
            var wasOn = item.Device.IsOn;
            Check(Find<Button>(page, "ToggleAllButton").IsEnabled, "a ceiling light can Toggle both lights");
            Succeeded(await this.DoAsync(page, bulb, "Toggle both lights", () => Click(Find<Button>(page, "ToggleAllButton"), "Toggle both lights"), "dev_toggle []"), "toggling both lights");
            await UntilAsync(() => item.Device.IsOn == !wasOn && item.Device.Background.IsOn == !wasOn, "both lights changed");
            Succeeded(await this.DoAsync(page, bulb, "Toggle both lights back", () => Click(Find<Button>(page, "ToggleAllButton"), "Toggle both lights"), "dev_toggle []"), "toggling both lights back");
            page.SelectTab("light");
        }

        private async Task ControlMusicAsync()
        {
            var bulb = Bulb(SimulatedKind.Color);
            var item = Item(SimulatedKind.Color);
            var page = await this.ShowLightAsync(SimulatedKind.Color, "music");
            Button B(string name) => Find<Button>(page, name);
            Check(B("StartMusicButton").IsEnabled && !B("StopMusicButton").IsEnabled && !B("RunDemoButton").IsEnabled, "before music mode: Start is there, Stop and the demo wait");

            // Start it. The light connects to the app, which listens on this computer.
            var sets = bulb.RequestsOf("set_music").Count;
            var started = await RunAsync(page, () => Click(B("StartMusicButton"), "Start music mode"));
            Succeeded(started, "starting music mode");
            await UntilAsync(() => bulb.RequestsOf("set_music").Count == sets + 1 && bulb.InMusicMode && App.Session.GetMusic(item.Id) is not null, "the light is in music mode, and the app holds the session");
            var told = bulb.RequestsOf("set_music")[^1];
            Check(told.Parameters.Length == 3 && told.Parameters[0].GetInt32() == 1 && told.Parameters[1].GetString() == "127.0.0.1" && told.Parameters[2].GetInt32() > 0, $"the light was told where to connect: {Describe(told)}");
            Check(page.MusicText.Contains("Music mode is on", StringComparison.Ordinal) && B("StopMusicButton").IsEnabled && B("RunDemoButton").IsEnabled && !B("StartMusicButton").IsEnabled, $"the page says that music mode is on ({page.MusicText})");

            // The demo: far more commands than a control connection takes in a minute.
            var music = bulb.Requests.Count(r => r.OverMusic);
            Find<NumberBox>(page, "DemoSecondsBox").Value = 4;
            Click(B("RunDemoButton"), "Run demo");
            await UntilAsync(() => page.IsDemoRunning, "the demo runs", 3);
            await Task.Delay(900);
            Check(page.DemoText.Contains("Sent", StringComparison.Ordinal) && page.IsDemoRunning, $"the page counts the commands while the demo runs ({page.DemoText})");
            await UntilAsync(() => !page.IsDemoRunning, "the demo ends after its time", 12);
            var sent = bulb.Requests.Count(r => r.OverMusic) - music;
            Check(sent > YeelightLimits.CommandsPerMinute, $"the light got {sent} commands over the music session in a few seconds: more than the {YeelightLimits.CommandsPerMinute} a minute of a control connection, and none was refused");
            var overMusic = bulb.Requests.Where(r => r.OverMusic).Skip(music).ToArray();
            Check(overMusic.All(r => r.Method is "set_hsv" or "set_rgb") && overMusic.Any(r => r.Method == "set_hsv") && overMusic.Any(r => r.Method == "set_rgb"), "the demo sent set_hsv and set_rgb");
            var shown = System.Text.RegularExpressions.Regex.Match(page.DemoText, @"Sent (\d+) commands");
            Check(shown.Success && Math.Abs(int.Parse(shown.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - sent) <= 3 && page.DemoText.Contains("no quota", StringComparison.OrdinalIgnoreCase), $"the page shows how many commands it sent, and that no quota applies ({page.DemoText})");
            Check(page.Runner.Last is { IsSuccess: true, Title: "Music mode demo: done" }, "the bar at the bottom says that the demo is done");
            await this.ShotAsync("control-music");

            // The raw page can send over the session too.
            var raw = await this.OpenAsync<RawPage>("raw");
            raw.SelectTab("send");
            raw.Fill("set_bright", "[10, \"sudden\", 0]", RawTarget.Music);
            var rawMusic = bulb.Requests.Count(r => r.OverMusic);
            var outcome = await RunAsync(raw, () => Click(Find<Button>(raw, "SendButton"), "Send"));
            Succeeded(outcome, "a raw command over the music session");
            await UntilAsync(() => bulb.Requests.Count(r => r.OverMusic) == rawMusic + 1 && bulb.Requests[^1] is { OverMusic: true, Method: "set_bright" }, "the command arrived over the music connection");
            Check(raw.ResultText.Contains("music mode session", StringComparison.Ordinal), "the Raw page says that it was sent over the music session");

            // Stop it.
            page = await this.ShowLightAsync(SimulatedKind.Color, "music");
            sets = bulb.RequestsOf("set_music").Count;
            Succeeded(await RunAsync(page, () => Click(B("StopMusicButton"), "Stop music mode")), "stopping music mode");
            await UntilAsync(() => !bulb.InMusicMode && App.Session.GetMusic(item.Id) is null && page.MusicText.Contains("Music mode is off", StringComparison.Ordinal), "music mode is off, on the light and on the page");
            Check(bulb.RequestsOf("set_music").Count == sets + 1 && Args(bulb.RequestsOf("set_music")[^1]) == "[0]", "the light was told to stop with set_music [0]");

            // A session that the light ends by itself is noticed (another program tells the light to leave music mode).
            Succeeded(await RunAsync(page, () => Click(B("StartMusicButton"), "Start music mode")), "starting music mode again");
            await UntilAsync(() => bulb.InMusicMode && App.Session.GetMusic(item.Id) is not null, "music mode is on again");
            raw = await this.OpenAsync<RawPage>("raw");
            raw.Fill("set_music", "[0]", RawTarget.Device);
            await ExpectAsync(bulb, "another program ends music mode", async () => await RunAsync(raw, () => Click(Find<Button>(raw, "SendButton"), "Send")), "set_music [0]");
            page = await this.ShowLightAsync(SimulatedKind.Color, "music");
            await UntilAsync(() => App.Session.GetMusic(item.Id) is null && page.MusicText.Contains("ended by itself", StringComparison.Ordinal), () => $"when the light closes the connection, session.Completion ends it and the page says so ({page.MusicText})");
            Check(B("StartMusicButton").IsEnabled && !B("StopMusicButton").IsEnabled, "Start music mode can be pressed again");
        }
    }
}
