namespace Yeelight
{
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// What a light is doing, as it told: the state of one light of a device (its main light or its background light).
    /// </summary>
    /// <remarks>
    /// Nothing is checked here beyond what is needed to keep the state sound: a value that a light reports and that is not one (text where a
    /// number is, a number out of range) is ignored, and the state keeps what it had.
    /// </remarks>
    internal sealed class LightState
    {
        private bool _isOn;
        private int _brightness;
        private ColorMode _colorMode = ColorMode.Rgb;
        private int _colorTemperature;
        private int _rgb;
        private int _hue;
        private int _saturation;
        private bool _isFlowing;
        private string _flowParameters = string.Empty;

        public bool IsOn => this._isOn;

        public int Brightness => this._brightness;

        public ColorMode ColorMode => this._colorMode;

        public int ColorTemperature => this._colorTemperature;

        public int Rgb => this._rgb;

        public int Hue => this._hue;

        public int Saturation => this._saturation;

        public bool IsFlowing => this._isFlowing;

        public string FlowParameters => this._flowParameters;

        /// <summary>
        /// Takes over one property, given by its name for the main light (<c>power</c>, <c>bright</c>, ...).
        /// </summary>
        /// <param name="key">The name of the property, without the prefix of the background light.</param>
        /// <param name="value">The value, as the light sent it.</param>
        /// <param name="handled">Whether the property is one of the state of a light at all.</param>
        /// <returns>The name of the property of <see cref="YeelightLight"/> that changed, or <c>null</c> when nothing did.</returns>
        public string? Apply(string key, string value, out bool handled)
        {
            handled = true;
            switch (key)
            {
                case YeelightProperties.Power:
                    return value switch
                    {
                        "on" => Change(ref this._isOn, true, nameof(YeelightLight.IsOn)),
                        "off" => Change(ref this._isOn, false, nameof(YeelightLight.IsOn)),
                        _ => null,
                    };
                case YeelightProperties.Brightness:
                    return TryNumber(value, 0, 100, out var brightness)
                        ? Change(ref this._brightness, brightness, nameof(YeelightLight.Brightness))
                        : null;
                case YeelightProperties.ColorTemperature:
                    return TryNumber(value, 0, 100_000, out var temperature)
                        ? Change(ref this._colorTemperature, temperature, nameof(YeelightLight.ColorTemperature))
                        : null;
                case YeelightProperties.Rgb:
                    return TryNumber(value, 0, YeelightLimits.MaxRgb, out var rgb)
                        ? Change(ref this._rgb, rgb, nameof(YeelightLight.Rgb))
                        : null;
                case YeelightProperties.Hue:
                    return TryNumber(value, 0, YeelightLimits.MaxHue, out var hue)
                        ? Change(ref this._hue, hue, nameof(YeelightLight.Hue))
                        : null;
                case YeelightProperties.Saturation:
                    return TryNumber(value, 0, YeelightLimits.MaxSaturation, out var saturation)
                        ? Change(ref this._saturation, saturation, nameof(YeelightLight.Saturation))
                        : null;
                case YeelightProperties.ColorMode:
                    return TryNumber(value, 1, 3, out var mode)
                        ? Change(ref this._colorMode, (ColorMode)mode, nameof(YeelightLight.ColorMode))
                        : null;
                case YeelightProperties.Flowing:
                    return value switch
                    {
                        "1" => Change(ref this._isFlowing, true, nameof(YeelightLight.IsFlowing)),
                        "0" => Change(ref this._isFlowing, false, nameof(YeelightLight.IsFlowing)),
                        _ => null,
                    };
                case YeelightProperties.FlowParams:
                    return Change(ref this._flowParameters, value, nameof(YeelightLight.FlowParameters));
                default:
                    handled = false;
                    return null;
            }
        }

        internal static bool TryNumber(string value, int minimum, int maximum, out int number)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= minimum && number <= maximum;
        }

        private static string? Change<T>(ref T field, T next, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, next))
            {
                return null;
            }

            field = next;
            return name;
        }
    }
}
