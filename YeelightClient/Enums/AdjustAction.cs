namespace Yeelight
{
    /// <summary>
    /// The direction in which <see cref="YeelightLight.AdjustAsync(AdjustAction, AdjustProperty, System.Threading.CancellationToken)"/> changes a property.
    /// </summary>
    public enum AdjustAction
    {
        /// <summary>Increase the property.</summary>
        Increase,

        /// <summary>Decrease the property.</summary>
        Decrease,

        /// <summary>Increase the property, and go back to its minimum when it has reached its maximum.</summary>
        Circle,
    }
}
