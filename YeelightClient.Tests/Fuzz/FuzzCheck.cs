namespace Yeelight.Tests.Fuzz
{
    using System;
    using FsCheck;
    using FsCheck.Fluent;
    using Microsoft.FSharp.Core;
    using Xunit.Sdk;

    /// <summary>
    /// Runs a property of FsCheck the way every fuzz test does: from the seed and for the number of cases of <see cref="FuzzSettings"/>,
    /// so that the same run is made every time, and a failure says how to make it again.
    /// </summary>
    internal static class FuzzCheck
    {
        /// <summary>
        /// Checks that a property holds for every value that a generator makes. A property fails when it returns <c>false</c> or throws.
        /// </summary>
        /// <param name="generator">Makes the values.</param>
        /// <param name="property">What has to be true of every value.</param>
        /// <param name="seedOffset">Added to the seed, so that two properties of a test do not try the same cases.</param>
        public static void ForAll<T>(Gen<T> generator, Func<T, bool> property, ulong seedOffset = 0)
        {
            Run(() => Check.One(Configure(seedOffset), Prop.ForAll(Arb.From(generator), property)), seedOffset);
        }

        /// <summary>
        /// Checks that a property holds for every pair of values that two generators make.
        /// </summary>
        public static void ForAll<T1, T2>(Gen<T1> first, Gen<T2> second, Func<T1, T2, bool> property, ulong seedOffset = 0)
        {
            Run(() => Check.One(Configure(seedOffset), Prop.ForAll(Arb.From(first), Arb.From(second), property)), seedOffset);
        }

        private static Config Configure(ulong seedOffset)
        {
            var replay = new Replay(new Rnd(FuzzSettings.Seed + seedOffset), FSharpOption<int>.None);
            return Config.QuickThrowOnFailure.WithMaxTest(FuzzSettings.Cases).WithReplay(FSharpOption<Replay>.Some(replay));
        }

        private static void Run(Action check, ulong seedOffset)
        {
            try
            {
                check();
            }
            catch (Exception ex)
            {
                throw new XunitException($"{ex.Message}\n(Fuzz: {FuzzSettings.Description}; this property uses the seed plus {seedOffset}.)", ex);
            }
        }
    }
}
