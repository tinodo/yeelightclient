namespace Yeelight.TestApp.Models
{
    using System;
    using System.Globalization;

    /// <summary>What a step of a colour flow does.</summary>
    internal enum FlowStepKind
    {
        /// <summary>The step changes the colour.</summary>
        Color,

        /// <summary>The step changes the colour temperature.</summary>
        ColorTemperature,

        /// <summary>The step waits.</summary>
        Sleep,
    }

    /// <summary>
    /// One step of the colour flow that the editor builds: a change of colour or colour temperature, or a pause, over a time, with a
    /// brightness or without. It is a value that is replaced, not changed, so that a list of steps can be shown and compared simply.
    /// </summary>
    /// <param name="Kind">What the step does.</param>
    /// <param name="DurationMs">How long it takes, in milliseconds.</param>
    /// <param name="Value">The colour as the protocol says it (<see cref="FlowStepKind.Color"/>), the colour temperature in kelvin (<see cref="FlowStepKind.ColorTemperature"/>), and zero for a pause.</param>
    /// <param name="Brightness">The brightness to change to, or <c>null</c> to leave it as it is.</param>
    internal sealed record FlowStepModel(FlowStepKind Kind, int DurationMs, int Value, int? Brightness)
    {
        /// <summary>Gets the kind of the step, in words.</summary>
        public string KindText => this.Kind switch
        {
            FlowStepKind.Color => "Colour",
            FlowStepKind.ColorTemperature => "Colour temperature",
            _ => "Sleep",
        };

        /// <summary>Gets what the step changes to, in words: the colour, the colour temperature, or nothing.</summary>
        public string ValueText => this.Kind switch
        {
            FlowStepKind.Color => Rgb24.FromValue(this.Value).Hex,
            FlowStepKind.ColorTemperature => string.Create(CultureInfo.InvariantCulture, $"{this.Value} K"),
            _ => string.Empty,
        };

        /// <summary>Gets the brightness in words: the percentage, or that it is left as it is.</summary>
        public string BrightnessText => this.Kind == FlowStepKind.Sleep
            ? string.Empty
            : this.Brightness is { } brightness ? string.Create(CultureInfo.InvariantCulture, $"{brightness}%") : "keep brightness";

        /// <summary>Gets the step on one line, for the list of the editor.</summary>
        public string Description => this.Kind == FlowStepKind.Sleep
            ? string.Create(CultureInfo.InvariantCulture, $"Sleep for {this.DurationMs} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{this.KindText} {this.ValueText}, {this.BrightnessText}, over {this.DurationMs} ms");

        /// <summary>Makes a step that changes the colour.</summary>
        /// <param name="rgb">The colour as the protocol says it.</param>
        /// <param name="durationMs">How long the change takes, in milliseconds.</param>
        /// <param name="brightness">The brightness to change to, or <c>null</c> to leave it.</param>
        /// <returns>The step.</returns>
        public static FlowStepModel Color(int rgb, int durationMs, int? brightness = null) => new(FlowStepKind.Color, durationMs, rgb, brightness);

        /// <summary>Makes a step that changes the colour temperature.</summary>
        /// <param name="kelvin">The colour temperature.</param>
        /// <param name="durationMs">How long the change takes, in milliseconds.</param>
        /// <param name="brightness">The brightness to change to, or <c>null</c> to leave it.</param>
        /// <returns>The step.</returns>
        public static FlowStepModel Temperature(int kelvin, int durationMs, int? brightness = null) => new(FlowStepKind.ColorTemperature, durationMs, kelvin, brightness);

        /// <summary>Makes a step that waits.</summary>
        /// <param name="durationMs">How long it waits, in milliseconds.</param>
        /// <returns>The step.</returns>
        public static FlowStepModel Sleep(int durationMs) => new(FlowStepKind.Sleep, durationMs, 0, null);

        /// <summary>Makes the step of the editor of a step of the library, for example of a flow that a light reports.</summary>
        /// <param name="step">The step.</param>
        /// <returns>The step of the editor.</returns>
        public static FlowStepModel From(ColorFlowStep step)
        {
            ArgumentNullException.ThrowIfNull(step);
            var milliseconds = (int)Math.Round(step.Duration.TotalMilliseconds);
            return step.Mode switch
            {
                ColorFlowMode.Rgb => Color(step.Value, milliseconds, step.Brightness),
                ColorFlowMode.ColorTemperature => Temperature(step.Value, milliseconds, step.Brightness),
                _ => Sleep(milliseconds),
            };
        }

        /// <summary>Makes the step of the library. The library checks the values, so a value that a light would refuse is refused here.</summary>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A value is out of the range that the protocol allows (a duration under 50 ms, a brightness of 101).</exception>
        public ColorFlowStep ToStep()
        {
            var duration = TimeSpan.FromMilliseconds(this.DurationMs);
            switch (this.Kind)
            {
                case FlowStepKind.Color:
                    var color = Rgb24.FromValue(this.Value);
                    return ColorFlowStep.Rgb(color.R, color.G, color.B, duration, this.Brightness);
                case FlowStepKind.ColorTemperature:
                    return ColorFlowStep.ColorTemperature(this.Value, duration, this.Brightness);
                default:
                    return ColorFlowStep.Sleep(duration);
            }
        }
    }
}
