namespace Yeelight
{
    /// <summary>
    /// What a light does when a <see cref="ColorFlow"/> has ended.
    /// </summary>
    public enum ColorFlowAction
    {
        /// <summary>The light goes back to the state that it was in before the flow started.</summary>
        RecoverState = 0,

        /// <summary>The light stays in the state in which the flow ended.</summary>
        Stay = 1,

        /// <summary>The light is switched off.</summary>
        TurnOff = 2,
    }
}
