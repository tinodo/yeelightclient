namespace Yeelight
{
    /// <summary>
    /// The property that <see cref="YeelightLight.AdjustAsync(AdjustAction, AdjustProperty, System.Threading.CancellationToken)"/> changes.
    /// </summary>
    public enum AdjustProperty
    {
        /// <summary>The brightness.</summary>
        Brightness,

        /// <summary>The colour temperature.</summary>
        ColorTemperature,

        /// <summary>The colour. Only <see cref="AdjustAction.Circle"/> is valid for it, and only a light that has colours has it.</summary>
        Color,
    }
}
