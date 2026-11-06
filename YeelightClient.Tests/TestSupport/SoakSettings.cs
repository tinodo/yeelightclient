namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Globalization;

    /// <summary>
    /// How long the soak tests run, and from which seed the chaos is made. Both come from the environment, so that the default run is
    /// short (a few seconds, for every test run) and a longer one can be asked for by the build that has the time:
    /// <c>YEELIGHT_SOAK_SECONDS=300 YEELIGHT_SOAK_SEED=7 dotnet test --filter Category=Soak</c>.
    /// </summary>
    internal static class SoakSettings
    {
        public const string SecondsVariable = "YEELIGHT_SOAK_SECONDS";

        public const string SeedVariable = "YEELIGHT_SOAK_SEED";

        private const double DefaultSeconds = 5;

        private const int DefaultSeed = 20261106;

        /// <summary>Gets how long every soak test runs: from a second to a day.</summary>
        public static TimeSpan Duration
        {
            get
            {
                var seconds = double.TryParse(Environment.GetEnvironmentVariable(SecondsVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
                    ? value
                    : DefaultSeconds;
                return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 86_400));
            }
        }

        /// <summary>Gets the seed of the chaos: the same seed makes the same sequence of cuts, stalls and refusals.</summary>
        public static int Seed => int.TryParse(Environment.GetEnvironmentVariable(SeedVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : DefaultSeed;

        /// <summary>Whether this is the short run of every test run, and not one that was asked for.</summary>
        public static bool IsShort => Duration <= TimeSpan.FromSeconds(30);

        /// <summary>Gets a line that says what is run, for the output of a test.</summary>
        public static string Description => $"{Duration.TotalSeconds:0.#} seconds, seed {Seed} ({SecondsVariable} and {SeedVariable} change them)";
    }
}
