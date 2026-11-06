namespace Yeelight
{
    using System;
    using System.Drawing;
    using System.Globalization;

    /// <summary>
    /// One step of a <see cref="ColorFlow"/>: a change of colour or colour temperature, or a pause, and how long it takes.
    /// </summary>
    /// <remarks>
    /// Make a step with <see cref="Rgb(int, int, int, TimeSpan, int?)"/>, <see cref="Rgb(Color, TimeSpan, int?)"/>,
    /// <see cref="ColorTemperature(int, TimeSpan, int?)"/> or <see cref="Sleep(TimeSpan)"/>.
    /// </remarks>
    public sealed class ColorFlowStep
    {
        private ColorFlowStep(int milliseconds, ColorFlowMode mode, int value, int? brightness)
        {
            this.Milliseconds = milliseconds;
            this.Mode = mode;
            this.Value = value;
            this.Brightness = brightness;
        }

        /// <summary>
        /// Gets how long the step takes: the time that the light needs to change gradually, or the time that it sleeps.
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromMilliseconds(this.Milliseconds);

        /// <summary>
        /// Gets what the step does.
        /// </summary>
        public ColorFlowMode Mode { get; }

        /// <summary>
        /// Gets the colour as a number (<see cref="ColorFlowMode.Rgb"/>) or the colour temperature in kelvin (<see cref="ColorFlowMode.ColorTemperature"/>),
        /// and <c>0</c> for a sleep.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Gets the brightness that the step changes to, or <c>null</c> when the step leaves the brightness as it is (and always for a sleep).
        /// </summary>
        public int? Brightness { get; }

        internal int Milliseconds { get; }

        /// <summary>
        /// Makes a step that changes the colour.
        /// </summary>
        /// <param name="red">The red part of the colour, 0 to 255.</param>
        /// <param name="green">The green part of the colour, 0 to 255.</param>
        /// <param name="blue">The blue part of the colour, 0 to 255.</param>
        /// <param name="duration">How long the change takes, at least <see cref="YeelightLimits.MinFlowStep"/>.</param>
        /// <param name="brightness">The brightness to change to, 1 to 100, or <c>null</c> to leave it as it is.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static ColorFlowStep Rgb(int red, int green, int blue, TimeSpan duration, int? brightness = null)
        {
            var value = Validate.Rgb(red, green, blue);
            return new ColorFlowStep(Validate.Milliseconds(duration, YeelightLimits.MinFlowStep, nameof(duration)), ColorFlowMode.Rgb, value, CheckBrightness(brightness));
        }

        /// <summary>
        /// Makes a step that changes the colour.
        /// </summary>
        /// <param name="color">The colour. Its alpha is ignored.</param>
        /// <param name="duration">How long the change takes, at least <see cref="YeelightLimits.MinFlowStep"/>.</param>
        /// <param name="brightness">The brightness to change to, 1 to 100, or <c>null</c> to leave it as it is.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static ColorFlowStep Rgb(Color color, TimeSpan duration, int? brightness = null)
        {
            return new ColorFlowStep(Validate.Milliseconds(duration, YeelightLimits.MinFlowStep, nameof(duration)), ColorFlowMode.Rgb, Validate.Rgb(color), CheckBrightness(brightness));
        }

        /// <summary>
        /// Makes a step that changes the colour temperature.
        /// </summary>
        /// <param name="kelvin">The colour temperature, 1700 to 6500.</param>
        /// <param name="duration">How long the change takes, at least <see cref="YeelightLimits.MinFlowStep"/>.</param>
        /// <param name="brightness">The brightness to change to, 1 to 100, or <c>null</c> to leave it as it is.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static ColorFlowStep ColorTemperature(int kelvin, TimeSpan duration, int? brightness = null)
        {
            Validate.ColorTemperature(kelvin, nameof(kelvin));
            return new ColorFlowStep(Validate.Milliseconds(duration, YeelightLimits.MinFlowStep, nameof(duration)), ColorFlowMode.ColorTemperature, kelvin, CheckBrightness(brightness));
        }

        /// <summary>
        /// Makes a step that does nothing for a while.
        /// </summary>
        /// <param name="duration">How long the light sleeps, at least <see cref="YeelightLimits.MinFlowStep"/>.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The duration is shorter than the protocol allows.</exception>
        public static ColorFlowStep Sleep(TimeSpan duration)
        {
            return new ColorFlowStep(Validate.Milliseconds(duration, YeelightLimits.MinFlowStep, nameof(duration)), ColorFlowMode.Sleep, 0, null);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var time = $"over {this.Milliseconds}ms";
            var brightness = this.Brightness is { } value ? $" and brightness to {value}%" : string.Empty;
            return this.Mode switch
            {
                ColorFlowMode.Rgb => string.Create(CultureInfo.InvariantCulture, $"Change colour to #{this.Value:X6}{brightness} {time}"),
                ColorFlowMode.ColorTemperature => string.Create(CultureInfo.InvariantCulture, $"Change colour temperature to {this.Value}K{brightness} {time}"),
                _ => $"Sleep {this.Milliseconds}ms",
            };
        }

        /// <summary>
        /// Makes a step of what a light reports (the four numbers of a step), without the range checks of a person: a light says what it runs.
        /// </summary>
        internal static bool TryCreate(int milliseconds, int mode, int value, int brightness, out ColorFlowStep? step)
        {
            step = null;
            var kind = (ColorFlowMode)mode;
            if (milliseconds < 0 || !Enum.IsDefined(kind))
            {
                return false;
            }

            step = kind == ColorFlowMode.Sleep
                ? new ColorFlowStep(milliseconds, kind, 0, null)
                : new ColorFlowStep(milliseconds, kind, value, brightness is >= 1 and <= 100 ? brightness : null);
            return true;
        }

        internal string ToExpression()
        {
            return this.Mode == ColorFlowMode.Sleep
                ? string.Create(CultureInfo.InvariantCulture, $"{this.Milliseconds},7,0,0")
                : string.Create(CultureInfo.InvariantCulture, $"{this.Milliseconds},{(int)this.Mode},{this.Value},{this.Brightness ?? -1}");
        }

        private static int? CheckBrightness(int? brightness)
        {
            if (brightness is { } value)
            {
                Validate.Brightness(value, nameof(brightness));
            }

            return brightness;
        }
    }
}
