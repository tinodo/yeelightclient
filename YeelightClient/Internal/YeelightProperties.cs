namespace Yeelight
{
    using System;
    using System.Collections.Frozen;
    using System.Collections.Generic;

    /// <summary>
    /// The names of the properties of a light, as the protocol has them (<c>get_prop</c> and the <c>props</c> notification).
    /// </summary>
    internal static class YeelightProperties
    {
        public const string Power = "power";
        public const string Brightness = "bright";
        public const string ColorTemperature = "ct";
        public const string Rgb = "rgb";
        public const string Hue = "hue";
        public const string Saturation = "sat";
        public const string ColorMode = "color_mode";
        public const string Flowing = "flowing";
        public const string DelayOff = "delayoff";
        public const string FlowParams = "flow_params";
        public const string MusicOn = "music_on";
        public const string Name = "name";
        public const string NightLightBrightness = "nl_br";
        public const string ActiveMode = "active_mode";
        public const string BackgroundPrefix = "bg_";
        public const string BackgroundColorMode = "bg_lmode";

        /// <summary>Every property that the protocol defines (table 4-2 of the specification), the main light first.</summary>
        public static readonly IReadOnlyList<string> All =
        [
            "power", "bright", "ct", "rgb", "hue", "sat", "color_mode", "flowing", "delayoff", "flow_params", "music_on", "name",
            "bg_power", "bg_flowing", "bg_flow_params", "bg_ct", "bg_lmode", "bg_bright", "bg_rgb", "bg_hue", "bg_sat",
            "nl_br", "active_mode",
        ];

        private static readonly FrozenSet<string> Known = All.ToFrozenSet(StringComparer.Ordinal);

        public static bool IsKnown(string name) => Known.Contains(name);
    }
}
