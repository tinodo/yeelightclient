namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What the limiter of the commands promises for any pace of commands and any passing of time: it never lets through more than the
    /// burst and what the time has refilled, a pause refills it up to the burst and no more, and the pace that it allows is allowed.
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class RateLimiterPropertyTests(ITestOutputHelper output)
    {
        private static readonly Gen<(int PerMinute, int Burst)> Settings =
            from perMinute in Gen.Frequency((4, Gen.Choose(1, 120)), (1, Gen.Choose(1, 100_000)))
            from burst in Gen.Frequency((4, Gen.Choose(1, 20)), (1, Gen.Choose(1, 1000)))
            select (perMinute, burst);

        // What happens next: -1 is a command, any other number is that many milliseconds that pass.
        private static readonly Gen<int[]> Operations = Gen.ListOf(
            Gen.Frequency((5, Gen.Constant(-1)), (4, Gen.Choose(0, 2000)), (1, Gen.Choose(0, 120_000))))
            .Select(operations => operations.ToArray());

        [Fact]
        public void TheLimiter_NeverLetsThroughMoreThanTheBurstAndWhatTheTimeHasRefilled()
        {
            FuzzCheck.ForAll(
                Settings,
                Operations,
                (settings, operations) =>
                {
                    var clock = new ManualTimeProvider();
                    var limiter = new YeelightRateLimiter(settings.PerMinute, settings.Burst, clock);
                    var granted = 0L;
                    var elapsed = TimeSpan.Zero;
                    foreach (var operation in operations)
                    {
                        if (operation >= 0)
                        {
                            clock.Advance(TimeSpan.FromMilliseconds(operation));
                            elapsed += TimeSpan.FromMilliseconds(operation);
                            continue;
                        }

                        if (limiter.TryTake())
                        {
                            granted++;
                        }

                        var allowed = settings.Burst + (elapsed.TotalSeconds * settings.PerMinute / 60d);
                        Assert.True(granted <= allowed + 1e-6, $"{granted} commands were let through in {elapsed.TotalSeconds:0.###} s, and no more than {allowed:0.###} are allowed ({settings.PerMinute} a minute, burst {settings.Burst}).");
                    }

                    return true;
                },
                seedOffset: 40);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void AfterAPause_ABurstGoesThrough_AndNotOneMore()
        {
            FuzzCheck.ForAll(
                Settings,
                Gen.Choose(0, 6),
                (settings, used) =>
                {
                    var clock = new ManualTimeProvider();
                    var limiter = new YeelightRateLimiter(settings.PerMinute, settings.Burst, clock);

                    // Use some of the burst, then wait for it to come back.
                    for (var index = 0; index < Math.Min(settings.Burst, used); index++)
                    {
                        Assert.True(limiter.TryTake());
                    }

                    clock.Advance(TimeSpan.FromSeconds((settings.Burst * 60d / settings.PerMinute) + 1));

                    var granted = 0;
                    while (granted <= settings.Burst + 1 && limiter.TryTake())
                    {
                        granted++;
                    }

                    Assert.Equal(settings.Burst, granted);
                    return true;
                },
                seedOffset: 41);
        }

        [Fact]
        public void WaitingForNoTime_IsTheSameAsTaking()
        {
            FuzzCheck.ForAll(
                Settings,
                Operations,
                (settings, operations) =>
                {
                    var firstClock = new ManualTimeProvider();
                    var secondClock = new ManualTimeProvider();
                    var taking = new YeelightRateLimiter(settings.PerMinute, settings.Burst, firstClock);
                    var waiting = new YeelightRateLimiter(settings.PerMinute, settings.Burst, secondClock);
                    foreach (var operation in operations)
                    {
                        if (operation >= 0)
                        {
                            firstClock.Advance(TimeSpan.FromMilliseconds(operation));
                            secondClock.Advance(TimeSpan.FromMilliseconds(operation));
                            continue;
                        }

                        Assert.Equal(taking.TryTake(), WaitedFor(waiting));
                    }

                    return true;
                },
                seedOffset: 42);
        }

        [Fact]
        public void TheSteadyPaceThatIsAllowed_IsAlwaysLetThrough()
        {
            FuzzCheck.ForAll(
                Settings,
                Gen.Choose(1, 40),
                (settings, count) =>
                {
                    var clock = new ManualTimeProvider();
                    var limiter = new YeelightRateLimiter(settings.PerMinute, settings.Burst, clock);
                    while (limiter.TryTake())
                    {
                    }

                    // One command every time that the limiter has refilled one (and a millisecond more, against the rounding).
                    var step = TimeSpan.FromMilliseconds(Math.Ceiling(60_000d / settings.PerMinute) + 1);
                    for (var index = 0; index < count; index++)
                    {
                        clock.Advance(step);
                        Assert.True(limiter.TryTake(), $"Command {index + 1} of a steady pace of one every {step.TotalMilliseconds} ms was refused ({settings.PerMinute} a minute).");
                    }

                    return true;
                },
                seedOffset: 43);
        }

        private static bool WaitedFor(YeelightRateLimiter limiter)
        {
            return limiter.WaitAsync(TimeSpan.Zero).AsTask().GetAwaiter().GetResult();
        }
    }
}
