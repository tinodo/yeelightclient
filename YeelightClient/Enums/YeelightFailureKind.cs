namespace Yeelight
{
    /// <summary>
    /// Why a command did not succeed.
    /// </summary>
    public enum YeelightFailureKind
    {
        /// <summary>The light received the command and refused it (an unsupported method, a quota that is used up, parameters it does not accept).</summary>
        DeviceRejected = 0,

        /// <summary>There is no connection to the light, and none was made in time.</summary>
        NotConnected = 1,

        /// <summary>The light did not answer in time.</summary>
        Timeout = 2,

        /// <summary>The client held the command back, because it would have gone over the number of commands a minute that a light accepts.</summary>
        RateLimited = 3,

        /// <summary>The connection to the light was lost while the command was waiting for its answer.</summary>
        ConnectionLost = 4,
    }
}
