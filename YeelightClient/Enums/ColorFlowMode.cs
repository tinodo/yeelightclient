namespace Yeelight
{
    /// <summary>
    /// What a step of a <see cref="ColorFlow"/> does.
    /// </summary>
    public enum ColorFlowMode
    {
        /// <summary>The step changes the colour (RGB) of the light.</summary>
        Rgb = 1,

        /// <summary>The step changes the colour temperature of the light.</summary>
        ColorTemperature = 2,

        /// <summary>The step does nothing, for the length of its duration.</summary>
        Sleep = 7,
    }
}
