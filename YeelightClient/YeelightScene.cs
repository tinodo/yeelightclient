namespace Yeelight
{
    using System;
    using System.Drawing;

    /// <summary>
    /// A scene: a state that a light is set to directly, turning it on first when it is off.
    /// </summary>
    /// <remarks>
    /// Make a scene with one of its static methods, and set it with <see cref="YeelightLight.SetSceneAsync(YeelightScene, System.Threading.CancellationToken)"/>.
    /// Unlike most commands a scene is accepted by a light that is off.
    /// </remarks>
    public sealed class YeelightScene
    {
        private readonly object[] _parameters;

        private YeelightScene(string description, params object[] parameters)
        {
            this.Description = description;
            this._parameters = parameters;
        }

        internal string Description { get; }

        /// <summary>
        /// Makes a copy of the parameters of the scene, as the light takes them: the class of the scene, and what belongs to it.
        /// </summary>
        internal object[] GetParameters() => (object[])this._parameters.Clone();

        /// <summary>
        /// Makes a scene that sets a colour and a brightness.
        /// </summary>
        /// <param name="red">The red part of the colour, 0 to 255.</param>
        /// <param name="green">The green part of the colour, 0 to 255.</param>
        /// <param name="blue">The blue part of the colour, 0 to 255.</param>
        /// <param name="brightness">The brightness, 1 to 100.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static YeelightScene Rgb(int red, int green, int blue, int brightness)
        {
            var rgb = Validate.Rgb(red, green, blue);
            Validate.Brightness(brightness, nameof(brightness));
            return new YeelightScene($"colour #{rgb:X6} at {brightness}%", "color", rgb, brightness);
        }

        /// <summary>
        /// Makes a scene that sets a colour and a brightness.
        /// </summary>
        /// <param name="color">The colour. Its alpha is ignored.</param>
        /// <param name="brightness">The brightness, 1 to 100.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="brightness"/> is out of the range that the protocol allows.</exception>
        public static YeelightScene Rgb(Color color, int brightness)
        {
            return Rgb(color.R, color.G, color.B, brightness);
        }

        /// <summary>
        /// Makes a scene that sets a colour, as a hue and a saturation, and a brightness.
        /// </summary>
        /// <param name="hue">The hue, 0 to 359.</param>
        /// <param name="saturation">The saturation, 0 to 100.</param>
        /// <param name="brightness">The brightness, 1 to 100.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static YeelightScene Hsv(int hue, int saturation, int brightness)
        {
            Validate.Hue(hue, nameof(hue));
            Validate.Saturation(saturation, nameof(saturation));
            Validate.Brightness(brightness, nameof(brightness));
            return new YeelightScene($"hue {hue}, saturation {saturation}% at {brightness}%", "hsv", hue, saturation, brightness);
        }

        /// <summary>
        /// Makes a scene that sets a colour temperature and a brightness.
        /// </summary>
        /// <param name="kelvin">The colour temperature, 1700 to 6500.</param>
        /// <param name="brightness">The brightness, 1 to 100.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static YeelightScene ColorTemperature(int kelvin, int brightness)
        {
            Validate.ColorTemperature(kelvin, nameof(kelvin));
            Validate.Brightness(brightness, nameof(brightness));
            return new YeelightScene($"{kelvin}K at {brightness}%", "ct", kelvin, brightness);
        }

        /// <summary>
        /// Makes a scene that starts a colour flow.
        /// </summary>
        /// <param name="flow">The flow.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="flow"/> is <c>null</c>.</exception>
        public static YeelightScene Flow(ColorFlow flow)
        {
            ArgumentNullException.ThrowIfNull(flow);
            return new YeelightScene($"colour flow of {flow.Steps.Count} steps", "cf", flow.Count, (int)flow.Action, flow.Expression);
        }

        /// <summary>
        /// Makes a scene that turns the light on at a brightness, and turns it off again after a number of minutes.
        /// </summary>
        /// <param name="brightness">The brightness, 1 to 100.</param>
        /// <param name="minutes">The number of minutes after which the light is turned off, at least 1.</param>
        /// <returns>The scene.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public static YeelightScene AutoDelayOff(int brightness, int minutes)
        {
            Validate.Brightness(brightness, nameof(brightness));
            ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
            return new YeelightScene($"{brightness}% for {minutes} minutes", "auto_delay_off", brightness, minutes);
        }

        /// <inheritdoc/>
        public override string ToString() => $"Scene: {this.Description}";
    }
}
