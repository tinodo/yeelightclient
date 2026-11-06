namespace Yeelight
{
    using System;

    /// <summary>
    /// The limits that the Yeelight protocol puts on the values that a light accepts, and on the way to it.
    /// </summary>
    /// <remarks>
    /// These are the limits of the protocol (the Yeelight WiFi Light Inter-Operation Specification). A light can accept less: the range of colour
    /// temperatures, for one, depends on the model.
    /// </remarks>
    public static class YeelightLimits
    {
        /// <summary>The lowest brightness (a percentage).</summary>
        public const int MinBrightness = 1;

        /// <summary>The highest brightness (a percentage).</summary>
        public const int MaxBrightness = 100;

        /// <summary>The lowest colour temperature, in kelvin.</summary>
        public const int MinColorTemperature = 1700;

        /// <summary>The highest colour temperature, in kelvin.</summary>
        public const int MaxColorTemperature = 6500;

        /// <summary>The highest hue (the lowest is 0).</summary>
        public const int MaxHue = 359;

        /// <summary>The highest saturation (the lowest is 0).</summary>
        public const int MaxSaturation = 100;

        /// <summary>The highest value of a colour as a number: 0xFFFFFF, red times 65536 plus green times 256 plus blue (the lowest is 0).</summary>
        public const int MaxRgb = 0xFFFFFF;

        /// <summary>The shortest transition that a light accepts. A shorter one is refused before it is sent; zero is no transition at all.</summary>
        public static readonly TimeSpan MinTransition = TimeSpan.FromMilliseconds(30);

        /// <summary>The shortest step of a <see cref="ColorFlow"/>.</summary>
        public static readonly TimeSpan MinFlowStep = TimeSpan.FromMilliseconds(50);

        /// <summary>The highest brightness (and colour temperature) adjustment of one command, as a percentage (the lowest is minus this).</summary>
        public const int MaxAdjustPercentage = 100;

        /// <summary>The number of simultaneous control connections that a light accepts: it rejects a fifth.</summary>
        public const int ConnectionsPerLight = 4;

        /// <summary>The number of commands a minute that a light accepts on one connection (144 a minute on all connections together).</summary>
        public const int CommandsPerMinute = 60;

        /// <summary>The UDP port that lights are searched on, and that they announce themselves to.</summary>
        public const int DiscoveryPort = 1982;

        /// <summary>The longest time that a timer of the framework can wait (about 49 days): a longer one is an error of the framework from deep inside.</summary>
        internal static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    }
}
