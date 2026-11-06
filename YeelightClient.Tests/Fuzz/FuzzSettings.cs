namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Globalization;

    /// <summary>
    /// How many cases the fuzz tests try, and from which seed. Both are fixed by default, so that a run is the same every time and a
    /// failure can be repeated; the environment says otherwise: <c>YEELIGHT_FUZZ_CASES=20000 YEELIGHT_FUZZ_SEED=7 dotnet test --filter Category=Fuzz</c>.
    /// </summary>
    internal static class FuzzSettings
    {
        public const string CasesVariable = "YEELIGHT_FUZZ_CASES";

        public const string SeedVariable = "YEELIGHT_FUZZ_SEED";

        private const ulong DefaultCases = 300;

        private const ulong DefaultSeed = 20261106;

        /// <summary>Gets the number of cases of every fuzz test.</summary>
        public static int Cases => (int)Math.Min(ReadNumber(CasesVariable, DefaultCases), 10_000_000);

        /// <summary>Gets the seed that the cases are made from.</summary>
        public static ulong Seed => ReadNumber(SeedVariable, DefaultSeed);

        /// <summary>Gets a line that says what was run, for the output of a test and for the message of a failure.</summary>
        public static string Description => $"{Cases} cases from seed {Seed} ({CasesVariable} and {SeedVariable} change them)";

        private static ulong ReadNumber(string name, ulong fallback)
        {
            return ulong.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
        }
    }
}
