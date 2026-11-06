namespace Yeelight
{
    /// <summary>
    /// How a light makes its light: the mode decides which of the colour properties of the light are valid.
    /// </summary>
    /// <remarks>
    /// The three modes are exclusive. A light that is in colour temperature mode reports a <see cref="YeelightLight.Rgb"/>, but it is
    /// not the one that it shows.
    /// </remarks>
    public enum ColorMode
    {
        /// <summary>Colour mode: the light shows the colour of <see cref="YeelightLight.Rgb"/>.</summary>
        Rgb = 1,

        /// <summary>Colour temperature mode: the light shows the white of <see cref="YeelightLight.ColorTemperature"/>.</summary>
        ColorTemperature = 2,

        /// <summary>HSV mode: the light shows the colour of <see cref="YeelightLight.Hue"/> and <see cref="YeelightLight.Saturation"/>.</summary>
        Hsv = 3,
    }
}
