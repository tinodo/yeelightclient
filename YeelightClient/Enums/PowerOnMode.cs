namespace Yeelight
{
    /// <summary>
    /// The mode that a light is switched on in.
    /// </summary>
    public enum PowerOnMode
    {
        /// <summary>A normal turn on: the light shows what it showed before it was switched off.</summary>
        Normal = 0,

        /// <summary>Turn on and switch to colour temperature mode.</summary>
        ColorTemperature = 1,

        /// <summary>Turn on and switch to colour (RGB) mode.</summary>
        Rgb = 2,

        /// <summary>Turn on and switch to HSV mode.</summary>
        Hsv = 3,

        /// <summary>Turn on and switch to colour flow mode.</summary>
        ColorFlow = 4,

        /// <summary>Turn on and switch to night light mode (ceiling lights only).</summary>
        NightLight = 5,
    }
}
