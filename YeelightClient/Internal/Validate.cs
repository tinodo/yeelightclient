namespace Yeelight
{
    using System;
    using System.Drawing;
    using System.Globalization;

    /// <summary>
    /// The checks of the values that go to a light. A value that a light would refuse is refused here, before anything is sent, with an
    /// <see cref="ArgumentOutOfRangeException"/> that names the argument.
    /// </summary>
    internal static class Validate
    {
        public static int Brightness(int value, string name)
        {
            return InRange(value, YeelightLimits.MinBrightness, YeelightLimits.MaxBrightness, name, "a brightness is a percentage");
        }

        public static int ColorTemperature(int value, string name)
        {
            return InRange(value, YeelightLimits.MinColorTemperature, YeelightLimits.MaxColorTemperature, name, "a colour temperature is in kelvin");
        }

        public static int Hue(int value, string name)
        {
            return InRange(value, 0, YeelightLimits.MaxHue, name, "a hue is in degrees");
        }

        public static int Saturation(int value, string name)
        {
            return InRange(value, 0, YeelightLimits.MaxSaturation, name, "a saturation is a percentage");
        }

        public static int Percentage(int value, string name)
        {
            return InRange(value, -YeelightLimits.MaxAdjustPercentage, YeelightLimits.MaxAdjustPercentage, name, "an adjustment is a percentage");
        }

        /// <summary>
        /// Checks the three parts of a colour and makes the number that a light takes: red times 65536 plus green times 256 plus blue.
        /// </summary>
        public static int Rgb(int red, int green, int blue)
        {
            InRange(red, 0, 255, nameof(red), "a part of a colour");
            InRange(green, 0, 255, nameof(green), "a part of a colour");
            InRange(blue, 0, 255, nameof(blue), "a part of a colour");
            return (red << 16) | (green << 8) | blue;
        }

        public static int Rgb(Color color)
        {
            return (color.R << 16) | (color.G << 8) | color.B;
        }

        /// <summary>
        /// Makes the effect and the duration of a command from a transition: none (or zero) is "sudden", anything else is "smooth".
        /// </summary>
        public static (string Effect, int Duration) Transition(TimeSpan? transition, string name)
        {
            if (transition is not { } value || value == TimeSpan.Zero)
            {
                return ("sudden", 0);
            }

            return ("smooth", Milliseconds(value, YeelightLimits.MinTransition, name));
        }

        /// <summary>
        /// Makes the duration of an adjustment, which has no effect to say that it is sudden: none is the shortest that a light supports.
        /// </summary>
        public static int Duration(TimeSpan? duration, string name)
        {
            return duration is not { } value || value == TimeSpan.Zero
                ? (int)YeelightLimits.MinTransition.TotalMilliseconds
                : Milliseconds(value, YeelightLimits.MinTransition, name);
        }

        /// <summary>
        /// Makes whole milliseconds of a duration that has to be at least <paramref name="minimum"/>.
        /// </summary>
        public static int Milliseconds(TimeSpan duration, TimeSpan minimum, string name)
        {
            var milliseconds = Math.Round(duration.TotalMilliseconds);
            if (milliseconds < minimum.TotalMilliseconds || milliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    duration,
                    string.Create(CultureInfo.InvariantCulture, $"A duration of at least {minimum.TotalMilliseconds:0} milliseconds is needed (and one that fits in whole milliseconds)."));
            }

            return (int)milliseconds;
        }

        private static int InRange(int value, int minimum, int maximum, string name, string what)
        {
            if (value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(what[0])}{what[1..]}: {minimum} to {maximum} is accepted."));
            }

            return value;
        }
    }
}
