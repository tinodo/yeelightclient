using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;
using Yeelight.TestApp.Session;

namespace Yeelight.TestApp.Views;

/// <summary>
/// Every command of a light, as controls: power, brightness, colour temperature, colour, adjusting, scenes and colour flows. It works on
/// the main light of a device, or on its background light (the same commands, with the prefix <c>bg_</c>), and it follows the light: what
/// the light says it is now is what the controls show, unless a person is in the middle of changing a control.
/// </summary>
public sealed partial class LightPanel : UserControl
{
    private readonly Dictionary<TextBlock, string> _notes = [];
    private ICommandHost? _host;
    private LightItem? _item;
    private bool _background;
    private bool _updating;
    private bool _brightnessDirty;
    private bool _temperatureDirty;
    private bool _colorDirty;
    private bool _hsvDirty;
    private bool _ready;

    /// <summary>Initializes a new instance of the <see cref="LightPanel"/> class.</summary>
    public LightPanel()
    {
        this.InitializeComponent();
        this._updating = true;
        this.ModeBox.ItemsSource = new[]
        {
            new Choice<PowerOnMode>(PowerOnMode.Normal, "Normal (the mode it was in)"),
            new Choice<PowerOnMode>(PowerOnMode.ColorTemperature, "Colour temperature mode"),
            new Choice<PowerOnMode>(PowerOnMode.Rgb, "Colour (RGB) mode"),
            new Choice<PowerOnMode>(PowerOnMode.Hsv, "Colour (HSV) mode"),
            new Choice<PowerOnMode>(PowerOnMode.ColorFlow, "Colour flow mode"),
            new Choice<PowerOnMode>(PowerOnMode.NightLight, "Night light mode (ceiling lights)"),
        };
        this.ModeBox.SelectedIndex = 0;
        this.AdjustActionBox.ItemsSource = new[]
        {
            new Choice<AdjustAction>(AdjustAction.Increase, "Increase"),
            new Choice<AdjustAction>(AdjustAction.Decrease, "Decrease"),
            new Choice<AdjustAction>(AdjustAction.Circle, "Circle"),
        };
        this.AdjustActionBox.SelectedIndex = 0;
        this.AdjustPropertyBox.ItemsSource = new[]
        {
            new Choice<AdjustProperty>(AdjustProperty.Brightness, "Brightness"),
            new Choice<AdjustProperty>(AdjustProperty.ColorTemperature, "Colour temperature"),
            new Choice<AdjustProperty>(AdjustProperty.Color, "Colour"),
        };
        this.AdjustPropertyBox.SelectedIndex = 0;
        this.SceneClassBox.ItemsSource = new[]
        {
            new Choice<SceneClass>(SceneClass.Color, "Colour (RGB) and brightness"),
            new Choice<SceneClass>(SceneClass.Hsv, "Colour (hue, saturation) and brightness"),
            new Choice<SceneClass>(SceneClass.ColorTemperature, "Colour temperature and brightness"),
            new Choice<SceneClass>(SceneClass.Flow, "Colour flow"),
            new Choice<SceneClass>(SceneClass.AutoDelayOff, "On, and off after a delay"),
        };
        this.SceneClassBox.SelectedIndex = 0;
        this.InitializeFlowEditor();
        this._updating = false;

        foreach (var note in new[] { this.PowerNote, this.BrightnessNote, this.TemperatureNote, this.RgbNote, this.HsvNote, this.AdjustNote, this.SceneNote, this.FlowNote })
        {
            this._notes[note] = note.Text;
        }

        this.ShowSceneClass();
        this.UpdateSwatches();
        this._ready = true;
        this.IsEnabled = false;
    }

    /// <summary>The classes of scene that the library makes.</summary>
    private enum SceneClass
    {
        Color,
        Hsv,
        ColorTemperature,
        Flow,
        AutoDelayOff,
    }

    /// <summary>Gets the light that the panel works on: the device, or its background light.</summary>
    internal YeelightLight? Light => this._item is null ? null : this._background ? this._item.Device.Background : this._item.Device;

    /// <summary>Gets the editor of the flow, which a flow scene starts too; for the smoke test.</summary>
    internal FlowEditorModel Flow => this._flow;

    private string Prefix => this._background ? "bg_" : string.Empty;

    private string Label => this._background ? "Background light: " : string.Empty;

    /// <summary>Connects the panel to a light, or to none.</summary>
    /// <param name="item">The light.</param>
    /// <param name="background">Whether the panel works on the background light of the device.</param>
    /// <param name="host">The page that runs the commands.</param>
    internal void Bind(LightItem? item, bool background, ICommandHost host)
    {
        this._host = host;
        if (this._item is not null)
        {
            this._item.Changed -= this.OnItemChanged;
        }

        this._item = item;
        this._background = background;
        this._brightnessDirty = false;
        this._temperatureDirty = false;
        this._colorDirty = false;
        this._hsvDirty = false;
        if (item is not null)
        {
            item.Changed += this.OnItemChanged;
        }

        this.Refresh();
    }

    /// <summary>Reads the light again, and shows what it says: the state, and what the light supports.</summary>
    internal void Refresh()
    {
        var light = this.Light;
        this.IsEnabled = light is not null;
        if (light is null || this._item is null)
        {
            return;
        }

        this._updating = true;
        try
        {
            this.PowerSwitch.IsOn = light.IsOn;
            if (!this._brightnessDirty && light.Brightness is >= YeelightLimits.MinBrightness and <= YeelightLimits.MaxBrightness)
            {
                this.BrightnessSlider.Value = light.Brightness;
            }

            if (!this._temperatureDirty && light.ColorTemperature is >= YeelightLimits.MinColorTemperature and <= YeelightLimits.MaxColorTemperature)
            {
                this.TemperatureSlider.Value = light.ColorTemperature;
            }

            if (!this._colorDirty)
            {
                var color = Rgb24.FromValue(light.Rgb);
                var wanted = Color.FromArgb(255, color.R, color.G, color.B);
                if (this.RgbPicker.Color != wanted)
                {
                    this.RgbPicker.Color = wanted;
                }
            }

            if (!this._hsvDirty)
            {
                this.HueSlider.Value = Math.Clamp(light.Hue, 0, YeelightLimits.MaxHue);
                this.SaturationSlider.Value = Math.Clamp(light.Saturation, 0, YeelightLimits.MaxSaturation);
            }

            this.FlowStatus.Text = (light.IsFlowing ? "The light is flowing. " : "The light is not flowing. ") + (light.FlowParameters.Length > 0
                ? "It reports: " + FlowEditorModel.DescribeParameters(light.FlowParameters)
                : "It reports no flow.");
            this.LoadFromLightButton.IsEnabled = light.FlowParameters.Length > 0;
            this.ApplySupport();
        }
        finally
        {
            this._updating = false;
        }

        this.UpdateSwatches();
    }

    private bool Can(string method)
    {
        var device = this._item?.Device;
        return device is not null && (device.SupportedMethods.Count == 0 || device.Supports(this.Prefix + method));
    }

    private void Support(string method, TextBlock note, params Control[] controls)
    {
        var can = this.Can(method);
        foreach (var control in controls)
        {
            control.IsEnabled = can;
        }

        note.Text = can ? this._notes[note] : $"This light does not list {this.Prefix}{method}, so it cannot do this.";
    }

    private void ApplySupport()
    {
        this.Support("set_power", this.PowerNote, this.PowerSwitch, this.TurnOnButton, this.TurnOffButton, this.ModeBox);
        this.ToggleButton.IsEnabled = this.Can("toggle");
        this.DefaultButton.IsEnabled = this.Can("set_default");
        this.Support("set_bright", this.BrightnessNote, this.BrightnessSlider, this.ApplyBrightnessButton);
        this.Support("set_ct_abx", this.TemperatureNote, this.TemperatureSlider, this.ApplyTemperatureButton);
        this.Support("set_rgb", this.RgbNote, this.RgbPicker, this.ApplyRgbButton);
        this.Support("set_hsv", this.HsvNote, this.HueSlider, this.SaturationSlider, this.ApplyHsvButton);
        this.Support("set_adjust", this.AdjustNote, this.AdjustActionBox, this.AdjustPropertyBox, this.AdjustButton);
        this.AdjustBrightnessButton.IsEnabled = this.Can("adjust_bright");
        this.AdjustTemperatureButton.IsEnabled = this.Can("adjust_ct");
        this.AdjustColorButton.IsEnabled = this.Can("adjust_color");
        this.PercentBox.IsEnabled = this.AdjustBrightnessButton.IsEnabled || this.AdjustTemperatureButton.IsEnabled;
        this.AdjustDurationBox.IsEnabled = this.PercentBox.IsEnabled || this.AdjustColorButton.IsEnabled;
        if (!this.Can("set_adjust") && !this.PercentBox.IsEnabled && !this.AdjustColorButton.IsEnabled)
        {
            this.AdjustNote.Text = $"This light lists none of {this.Prefix}set_adjust, {this.Prefix}adjust_bright, {this.Prefix}adjust_ct and {this.Prefix}adjust_color.";
        }
        else if (!this.Can("set_adjust") || !this.Can("adjust_ct") || !this.Can("adjust_color"))
        {
            var missing = new[] { "set_adjust", "adjust_bright", "adjust_ct", "adjust_color" }.Where(method => !this.Can(method)).Select(method => this.Prefix + method);
            this.AdjustNote.Text = $"This light does not list {string.Join(", ", missing)}: those buttons are off.";
        }
        else
        {
            this.AdjustNote.Text = this._notes[this.AdjustNote];
        }

        var canScene = this.Can("set_scene");
        this.SceneClassBox.IsEnabled = canScene;
        this.SetSceneButton.IsEnabled = canScene;
        this.SceneNote.Text = canScene ? this._notes[this.SceneNote] : $"This light does not list {this.Prefix}set_scene, so it cannot do this.";
        this.StopFlowButton.IsEnabled = this.Can("stop_cf");
        this.UpdateFlowButtons();
        this.FlowNote.Text = this.Can("start_cf") && this.Can("stop_cf")
            ? this._notes[this.FlowNote]
            : $"This light does not list {this.Prefix}start_cf or {this.Prefix}stop_cf: the flow buttons that it lacks are off.";
    }

    private void UpdateSwatches()
    {
        this.BrightnessValue.Text = $"{(int)this.BrightnessSlider.Value} %";
        var kelvin = (int)this.TemperatureSlider.Value;
        this.TemperatureValue.Text = $"{kelvin} K";
        this.TemperatureSwatch.Background = Brush(LightFormat.KelvinToRgb(kelvin));
        this.HueValue.Text = $"{(int)this.HueSlider.Value}\u00b0";
        this.SaturationValue.Text = $"{(int)this.SaturationSlider.Value} %";
        this.HsvSwatch.Background = Brush(LightFormat.HsvToRgb((int)this.HueSlider.Value, (int)this.SaturationSlider.Value, 100));
        this.SceneSwatch.Background = Brush(new Rgb24(Channel(this.SceneRedBox), Channel(this.SceneGreenBox), Channel(this.SceneBlueBox)));
    }

    private static byte Channel(NumberBox box) => double.IsNaN(box.Value) ? (byte)0 : (byte)Math.Clamp((int)Math.Round(box.Value), 0, 255);

    private static SolidColorBrush Brush(Rgb24 color) => new(Color.FromArgb(255, color.R, color.G, color.B));

    private static int Number(NumberBox box, string name)
    {
        return double.IsNaN(box.Value) ? throw new ArgumentException($"Type a number for {name}.") : (int)Math.Round(box.Value);
    }

    private TimeSpan? Milliseconds(NumberBox box, string name)
    {
        var value = Number(box, name);
        return value <= 0 ? null : TimeSpan.FromMilliseconds(value);
    }

    private void OnItemChanged(object? sender, EventArgs e) => this.Refresh();

    // Runs a command of the light and shows the state again afterwards: a switch that the light refused goes back to what it is.
    private async Task DoAsync(string what, Func<YeelightLight, ICommandHost, CancellationToken, Task> command, string? success = null)
    {
        if (this.Light is not { } light || this._host is not { } host)
        {
            return;
        }

        try
        {
            await host.RunAsync(this.Label + what, token => command(light, host, token), success);
        }
        catch (Exception exception)
        {
            host.Reject(this.Label + what, exception.Message);
        }
        finally
        {
            this.Refresh();
        }
    }

    private async void PowerSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this._ready || this._updating)
        {
            return;
        }

        var on = this.PowerSwitch.IsOn;
        await this.DoAsync(on ? "Turn on" : "Turn off", (light, host, token) => light.SetPowerAsync(on, host.Transition, PowerOnMode.Normal, token));
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Toggle", (light, _, token) => light.ToggleAsync(token));
    }

    private async void TurnOffButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Turn off", (light, host, token) => light.TurnOffAsync(host.Transition, token));
    }

    private async void TurnOnButton_Click(object sender, RoutedEventArgs e)
    {
        var mode = (this.ModeBox.SelectedItem as Choice<PowerOnMode>)?.Value ?? PowerOnMode.Normal;
        await this.DoAsync($"Turn on ({mode})", (light, host, token) => light.TurnOnAsync(host.Transition, mode, token));
    }

    private async void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Set default", (light, _, token) => light.SetDefaultAsync(token));
    }

    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!this._ready)
        {
            return;
        }

        this._brightnessDirty |= !this._updating;
        this.UpdateSwatches();
    }

    private async void ApplyBrightnessButton_Click(object sender, RoutedEventArgs e)
    {
        var brightness = (int)Math.Round(this.BrightnessSlider.Value);
        this._brightnessDirty = false;
        await this.DoAsync($"Set brightness to {brightness}%", (light, host, token) => light.SetBrightnessAsync(brightness, host.Transition, token));
    }

    private void TemperatureSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!this._ready)
        {
            return;
        }

        this._temperatureDirty |= !this._updating;
        this.UpdateSwatches();
    }

    private async void ApplyTemperatureButton_Click(object sender, RoutedEventArgs e)
    {
        var kelvin = (int)Math.Round(this.TemperatureSlider.Value);
        this._temperatureDirty = false;
        await this.DoAsync($"Set colour temperature to {kelvin} K", (light, host, token) => light.SetColorTemperatureAsync(kelvin, host.Transition, token));
    }

    private void RgbPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        this._colorDirty |= this._ready && !this._updating;
    }

    private async void ApplyRgbButton_Click(object sender, RoutedEventArgs e)
    {
        var color = this.RgbPicker.Color;
        this._colorDirty = false;
        await this.DoAsync($"Set colour to {new Rgb24(color.R, color.G, color.B).Hex}", (light, host, token) => light.SetRgbAsync(color.R, color.G, color.B, host.Transition, token));
    }

    private void HsvSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!this._ready)
        {
            return;
        }

        this._hsvDirty |= !this._updating;
        this.UpdateSwatches();
    }

    private async void ApplyHsvButton_Click(object sender, RoutedEventArgs e)
    {
        var hue = (int)Math.Round(this.HueSlider.Value);
        var saturation = (int)Math.Round(this.SaturationSlider.Value);
        this._hsvDirty = false;
        await this.DoAsync($"Set hue {hue}\u00b0, saturation {saturation}%", (light, host, token) => light.SetHsvAsync(hue, saturation, host.Transition, token));
    }

    private async void AdjustButton_Click(object sender, RoutedEventArgs e)
    {
        var action = (this.AdjustActionBox.SelectedItem as Choice<AdjustAction>)?.Value ?? AdjustAction.Increase;
        var property = (this.AdjustPropertyBox.SelectedItem as Choice<AdjustProperty>)?.Value ?? AdjustProperty.Brightness;
        await this.DoAsync($"Adjust: {action} the {property}", (light, _, token) => light.AdjustAsync(action, property, token));
    }

    private async void AdjustBrightnessButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync(
            "Adjust brightness by a percentage",
            (light, _, token) => light.AdjustBrightnessAsync(Number(this.PercentBox, "the percentage"), this.Milliseconds(this.AdjustDurationBox, "the duration"), token));
    }

    private async void AdjustTemperatureButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync(
            "Adjust colour temperature by a percentage",
            (light, _, token) => light.AdjustColorTemperatureAsync(Number(this.PercentBox, "the percentage"), this.Milliseconds(this.AdjustDurationBox, "the duration"), token));
    }

    private async void AdjustColorButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Move the colour on", (light, _, token) => light.AdjustColorAsync(this.Milliseconds(this.AdjustDurationBox, "the duration"), token));
    }

    private void SceneClassBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._ready)
        {
            this.ShowSceneClass();
        }
    }

    private void SceneColorBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (this._ready)
        {
            this.UpdateSwatches();
        }
    }

    private SceneClass CurrentSceneClass => (this.SceneClassBox.SelectedItem as Choice<SceneClass>)?.Value ?? SceneClass.Color;

    private void ShowSceneClass()
    {
        var scene = this.CurrentSceneClass;
        this.SceneColorPanel.Visibility = scene == SceneClass.Color ? Visibility.Visible : Visibility.Collapsed;
        this.SceneHsvPanel.Visibility = scene == SceneClass.Hsv ? Visibility.Visible : Visibility.Collapsed;
        this.SceneKelvinPanel.Visibility = scene == SceneClass.ColorTemperature ? Visibility.Visible : Visibility.Collapsed;
        this.SceneMinutesPanel.Visibility = scene == SceneClass.AutoDelayOff ? Visibility.Visible : Visibility.Collapsed;
        this.SceneBrightnessBox.Visibility = scene == SceneClass.Flow ? Visibility.Collapsed : Visibility.Visible;
        this.SceneFlowText.Visibility = scene == SceneClass.Flow ? Visibility.Visible : Visibility.Collapsed;
        this.UpdateFlowSummary();
    }

    private YeelightScene BuildScene()
    {
        return this.CurrentSceneClass switch
        {
            SceneClass.Color => YeelightScene.Rgb(Number(this.SceneRedBox, "red"), Number(this.SceneGreenBox, "green"), Number(this.SceneBlueBox, "blue"), Number(this.SceneBrightnessBox, "the brightness")),
            SceneClass.Hsv => YeelightScene.Hsv(Number(this.SceneHueBox, "the hue"), Number(this.SceneSaturationBox, "the saturation"), Number(this.SceneBrightnessBox, "the brightness")),
            SceneClass.ColorTemperature => YeelightScene.ColorTemperature(Number(this.SceneKelvinBox, "the colour temperature"), Number(this.SceneBrightnessBox, "the brightness")),
            SceneClass.Flow => YeelightScene.Flow(this._flow.ToFlow()),
            _ => YeelightScene.AutoDelayOff(Number(this.SceneBrightnessBox, "the brightness"), Number(this.SceneMinutesBox, "the minutes")),
        };
    }

    private async void SetSceneButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (this.SceneClassBox.SelectedItem as Choice<SceneClass>)?.Text ?? "scene";
        await this.DoAsync($"Set scene: {name.ToLowerInvariant()}", (light, _, token) => light.SetSceneAsync(this.BuildScene(), token));
    }
}
