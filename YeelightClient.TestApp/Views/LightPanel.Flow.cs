using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Yeelight.TestApp.Models;
using Yeelight.TestApp.Services;

namespace Yeelight.TestApp.Views;

/// <summary>The editor of the colour flow of a light: the steps, the presets, the count and the action, and the commands that start and stop it.</summary>
public sealed partial class LightPanel
{
    private readonly FlowEditorModel _flow = new();

    private void InitializeFlowEditor()
    {
        this.PresetBox.ItemsSource = FlowEditorModel.Presets.Select(preset => new Choice<FlowPreset>(preset, preset.Name)).ToArray();
        this.PresetBox.SelectedIndex = 0;
        this.StepKindBox.ItemsSource = new[]
        {
            new Choice<FlowStepKind>(FlowStepKind.Color, "Colour"),
            new Choice<FlowStepKind>(FlowStepKind.ColorTemperature, "Colour temperature"),
            new Choice<FlowStepKind>(FlowStepKind.Sleep, "Sleep"),
        };
        this.StepKindBox.SelectedIndex = 0;
        this.FlowActionBox.ItemsSource = new[]
        {
            new Choice<ColorFlowAction>(ColorFlowAction.RecoverState, "Go back to the state before"),
            new Choice<ColorFlowAction>(ColorFlowAction.Stay, "Stay as it is"),
            new Choice<ColorFlowAction>(ColorFlowAction.TurnOff, "Turn the light off"),
        };
        this.FlowActionBox.SelectedIndex = 0;
        this.StepList.ItemsSource = this._flow.Steps;
        this._flow.Steps.CollectionChanged += this.OnStepsChanged;
        this.StepColorButton.Color = Color.FromArgb(255, 255, 140, 0);
        this.ShowStepKind();
    }

    private FlowStepKind CurrentStepKind => (this.StepKindBox.SelectedItem as Choice<FlowStepKind>)?.Value ?? FlowStepKind.Color;

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.StepsEmpty.Visibility = this._flow.Steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        this.UpdateFlowSummary();
    }

    private void UpdateFlowButtons()
    {
        var index = this.StepList.SelectedIndex;
        var count = this._flow.Steps.Count;
        this.ReplaceStepButton.IsEnabled = index >= 0;
        this.RemoveStepButton.IsEnabled = index >= 0;
        this.UpStepButton.IsEnabled = index > 0;
        this.DownStepButton.IsEnabled = index >= 0 && index < count - 1;
        this.ClearStepsButton.IsEnabled = count > 0;
        this.StartFlowButton.IsEnabled = count > 0 && this.Can("start_cf");
    }

    private void UpdateFlowSummary()
    {
        this.UpdateFlowButtons();
        if (this._flow.Steps.Count == 0)
        {
            this.FlowSummary.Text = "Add steps, or load a preset, to make a flow.";
            this.SceneFlowText.Text = "The colour flow scene starts the flow of the editor in the Colour flow card, and it has no steps yet.";
            return;
        }

        try
        {
            var flow = this._flow.ToFlow();
            this.FlowSummary.Text = $"{this._flow.Describe()}\nAs the light has it (count, action, steps): {flow}";
            this.SceneFlowText.Text = $"The scene starts the flow of the editor in the Colour flow card: {this._flow.Describe()}";
        }
        catch (ArgumentException exception)
        {
            this.FlowSummary.Text = $"{this._flow.Describe()}\nThis flow cannot be sent: {JsonTools.OneLine(exception.Message)}";
            this.SceneFlowText.Text = "The flow of the editor cannot be sent as it is: " + JsonTools.OneLine(exception.Message);
        }
    }

    private void ShowStepKind()
    {
        var kind = this.CurrentStepKind;
        this.StepColorPanel.Visibility = kind == FlowStepKind.Color ? Visibility.Visible : Visibility.Collapsed;
        this.StepKelvinBox.Visibility = kind == FlowStepKind.ColorTemperature ? Visibility.Visible : Visibility.Collapsed;
        this.StepBrightnessBox.Visibility = kind == FlowStepKind.Sleep ? Visibility.Collapsed : Visibility.Visible;
        this.StepKeepBox.Visibility = kind == FlowStepKind.Sleep ? Visibility.Collapsed : Visibility.Visible;
        this.StepBrightnessBox.IsEnabled = this.StepKeepBox.IsChecked != true;
    }

    private FlowStepModel BuildStep()
    {
        var kind = this.CurrentStepKind;
        var duration = Number(this.StepDurationBox, "the duration of the step");
        int? brightness = kind == FlowStepKind.Sleep || this.StepKeepBox.IsChecked == true ? null : Number(this.StepBrightnessBox, "the brightness of the step");
        return kind switch
        {
            FlowStepKind.Color => FlowStepModel.Color(this.StepColorButton.Rgb, duration, brightness),
            FlowStepKind.ColorTemperature => FlowStepModel.Temperature(Number(this.StepKelvinBox, "the colour temperature of the step"), duration, brightness),
            _ => FlowStepModel.Sleep(duration),
        };
    }

    // A step that the library would refuse is not added: the editor only holds a flow that can be sent.
    private bool TryBuildStep(string what, out FlowStepModel step)
    {
        step = null!;
        try
        {
            step = this.BuildStep();
            _ = step.ToStep();
            return true;
        }
        catch (ArgumentException exception)
        {
            this._host?.Reject(this.Label + what, JsonTools.OneLine(exception.Message));
            return false;
        }
    }

    private void Reject(string what, string problem) => this._host?.Reject(this.Label + what, problem);

    private void LoadStepIntoEditor(FlowStepModel step)
    {
        var wasUpdating = this._updating;
        this._updating = true;
        try
        {
            this.StepKindBox.SelectedItem = ((IEnumerable<Choice<FlowStepKind>>)this.StepKindBox.ItemsSource).First(choice => choice.Value == step.Kind);
            this.StepDurationBox.Value = step.DurationMs;
            if (step.Kind == FlowStepKind.Color)
            {
                var color = Rgb24.FromValue(step.Value);
                this.StepColorButton.Color = Color.FromArgb(255, color.R, color.G, color.B);
            }
            else if (step.Kind == FlowStepKind.ColorTemperature)
            {
                this.StepKelvinBox.Value = step.Value;
            }

            this.StepKeepBox.IsChecked = step.Kind != FlowStepKind.Sleep && step.Brightness is null;
            if (step.Brightness is { } brightness)
            {
                this.StepBrightnessBox.Value = brightness;
            }

            this.ShowStepKind();
        }
        finally
        {
            this._updating = wasUpdating;
        }
    }

    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.StepList.SelectedItem is FlowStepModel step)
        {
            this.LoadStepIntoEditor(step);
        }

        this.UpdateFlowButtons();
    }

    private void StepKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._ready)
        {
            this.ShowStepKind();
        }
    }

    private void StepKeepBox_Changed(object sender, RoutedEventArgs e)
    {
        if (this._ready)
        {
            this.StepBrightnessBox.IsEnabled = this.StepKeepBox.IsChecked != true;
        }
    }

    private void FlowInput_Changed(object sender, object e)
    {
        if (!this._ready || this._updating)
        {
            return;
        }

        this._flow.Count = double.IsNaN(this.FlowCountBox.Value) ? 0 : Math.Max(0, (int)Math.Round(this.FlowCountBox.Value));
        this._flow.Action = (this.FlowActionBox.SelectedItem as Choice<ColorFlowAction>)?.Value ?? ColorFlowAction.RecoverState;
        this.UpdateFlowSummary();
    }

    private void ShowFlowSettings()
    {
        var wasUpdating = this._updating;
        this._updating = true;
        try
        {
            this.FlowCountBox.Value = this._flow.Count;
            this.FlowActionBox.SelectedItem = ((IEnumerable<Choice<ColorFlowAction>>)this.FlowActionBox.ItemsSource).First(choice => choice.Value == this._flow.Action);
        }
        finally
        {
            this._updating = wasUpdating;
        }

        this.UpdateFlowSummary();
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.PresetBox.SelectedItem is Choice<FlowPreset> choice)
        {
            this._flow.Load(choice.Value);
            this.ShowFlowSettings();
            this.StepList.SelectedIndex = -1;
        }
    }

    private void LoadFromLightButton_Click(object sender, RoutedEventArgs e)
    {
        var text = this.Light?.FlowParameters;
        if (ColorFlow.TryParse(text, out var flow) && flow is not null)
        {
            this._flow.Load(flow);
            this.ShowFlowSettings();
            this.StepList.SelectedIndex = -1;
        }
        else
        {
            this.Reject("Load from the light", "The light reports no flow that can be read (flow_params is empty, or is not in the form that the protocol describes).");
        }
    }

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.TryBuildStep("Add step", out var step))
        {
            this._flow.Add(step);
            this.StepList.SelectedIndex = this._flow.Steps.Count - 1;
        }
    }

    private void ReplaceStepButton_Click(object sender, RoutedEventArgs e)
    {
        var index = this.StepList.SelectedIndex;
        if (index < 0)
        {
            this.Reject("Replace step", "Select a step of the list first.");
        }
        else if (this.TryBuildStep("Replace step", out var step))
        {
            this._flow.Replace(index, step);
            this.StepList.SelectedIndex = index;
        }
    }

    private void RemoveStepButton_Click(object sender, RoutedEventArgs e)
    {
        var index = this.StepList.SelectedIndex;
        if (this._flow.RemoveAt(index))
        {
            this.StepList.SelectedIndex = Math.Min(index, this._flow.Steps.Count - 1);
        }
    }

    private void UpStepButton_Click(object sender, RoutedEventArgs e)
    {
        var index = this.StepList.SelectedIndex;
        if (this._flow.MoveUp(index))
        {
            this.StepList.SelectedIndex = index - 1;
        }
    }

    private void DownStepButton_Click(object sender, RoutedEventArgs e)
    {
        var index = this.StepList.SelectedIndex;
        if (this._flow.MoveDown(index))
        {
            this.StepList.SelectedIndex = index + 1;
        }
    }

    private void ClearStepsButton_Click(object sender, RoutedEventArgs e)
    {
        this._flow.Clear();
        this.ShowFlowSettings();
    }

    private async void StartFlowButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Start colour flow", (light, _, token) => light.StartColorFlowAsync(this._flow.ToFlow(), token));
    }

    private async void StopFlowButton_Click(object sender, RoutedEventArgs e)
    {
        await this.DoAsync("Stop colour flow", (light, _, token) => light.StopColorFlowAsync(token));
    }
}
