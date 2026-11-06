namespace Yeelight.TestApp.Models
{
    using System;
    using System.Globalization;

    /// <summary>Where the Raw page sends a command.</summary>
    internal enum RawTarget
    {
        /// <summary>To the device, as the protocol has the method (<c>YeelightLight.SendCommandAsync</c> of the device).</summary>
        Device,

        /// <summary>To the background light of the device, with the method as it is typed: the library adds no prefix.</summary>
        Background,

        /// <summary>Over the music mode session, which has no answer.</summary>
        Music,
    }

    /// <summary>One command that the Raw page sent, with how it went, for the history that fills the form again.</summary>
    /// <param name="Time">When it was sent.</param>
    /// <param name="Target">Where it was sent.</param>
    /// <param name="Method">The method, as it was typed.</param>
    /// <param name="Parameters">The parameters, as the JSON array that was typed.</param>
    /// <param name="Success">Whether the light accepted it.</param>
    /// <param name="Summary">How it went, on one line.</param>
    internal sealed record RawHistoryItem(DateTimeOffset Time, RawTarget Target, string Method, string Parameters, bool Success, string Summary)
    {
        /// <summary>Gets the time of day, for the list.</summary>
        public string TimeText => this.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>Gets where the command went, in a word.</summary>
        public string TargetText => TargetName(this.Target);

        /// <summary>Gets the method and its parameters on one line.</summary>
        public string Command => this.Parameters.Length == 0 || this.Parameters == "[]" ? this.Method : $"{this.Method} {this.Parameters}";

        /// <summary>Gets a mark for the list: a check for a command that worked, a cross for one that did not.</summary>
        public string Mark => this.Success ? "\u2713" : "\u2717";

        /// <summary>Gets the name of a target, in a word or two.</summary>
        /// <param name="target">The target.</param>
        /// <returns>The words.</returns>
        public static string TargetName(RawTarget target)
        {
            return target switch
            {
                RawTarget.Device => "light",
                RawTarget.Background => "background light",
                _ => "music session",
            };
        }
    }
}
