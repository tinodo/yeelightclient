namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A light answers "client quota exceeded" to more than about 60 commands a minute on a connection, so the commands to one light are
    /// held to a token bucket: a short burst at once, after that one command per refill, in order.
    /// </summary>
    public class YeelightRateLimiterTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        [Fact]
        public void TryTake_AllowsABurst_ThenRefuses()
        {
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 3, new UnitClock());

            Assert.True(limiter.TryTake());
            Assert.True(limiter.TryTake());
            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
        }

        [Fact]
        public void TheDefaults_Are60AMinuteAndABurstOfTen()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(time: clock);

            for (var count = 0; count < 10; count++)
            {
                Assert.True(limiter.TryTake(), $"Command {count + 1} of the burst.");
            }

            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
        }

        [Fact]
        public void TryTake_RefillsAtTheSustainedPace()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());

            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.False(limiter.TryTake());

            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
        }

        [Theory]
        [InlineData(6, 10_000)]
        [InlineData(30, 2_000)]
        [InlineData(60, 1_000)]
        [InlineData(120, 500)]
        [InlineData(600, 100)]
        public void TryTake_ARefillTakesAsLongAsTheRateSays(int perMinute, int millisecondsPerToken)
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute, burst: 1, clock);
            Assert.True(limiter.TryTake());

            clock.Advance(TimeSpan.FromMilliseconds(millisecondsPerToken - 1));
            Assert.False(limiter.TryTake());

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(limiter.TryTake());
        }

        [Fact]
        public void TryTake_PartsOfATokenAddUp()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 5, clock);
            while (limiter.TryTake())
            {
            }

            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromMilliseconds(400));

            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
        }

        [Fact]
        public void TryTake_AQuietHour_GivesNoMoreThanTheBurst()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 4, clock);
            clock.Advance(TimeSpan.FromHours(1));

            var taken = 0;
            while (limiter.TryTake())
            {
                taken++;
            }

            Assert.Equal(4, taken);
        }

        [Fact]
        public void TryTake_OverTenMinutes_AllowsTheBurstPlusTheSustainedRate()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 10, clock);

            var taken = 0;
            for (var step = 0; step < 6_000; step++)
            {
                while (limiter.TryTake())
                {
                    taken++;
                }

                clock.Advance(TimeSpan.FromMilliseconds(100));
            }

            // Ten minutes at 60 a minute is 600, and the burst is 10 (give or take the token that is still on its way).
            Assert.InRange(taken, 608, 610);
        }

        [Fact]
        public void TryTake_FromManyThreadsAtOnce_NeverGivesMoreThanThereAre()
        {
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 50, new UnitClock());
            var taken = 0;

            Parallel.For(0, 400, _ =>
            {
                if (limiter.TryTake())
                {
                    Interlocked.Increment(ref taken);
                }
            });

            Assert.Equal(50, taken);
        }

        [Fact]
        public async Task WaitAsync_ATokenThatIsThere_IsTakenAtOnce()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 2, clock);

            Assert.True(await limiter.WaitAsync(TimeSpan.Zero));
            Assert.True(await limiter.WaitAsync(TimeSpan.Zero));
            Assert.False(await limiter.WaitAsync(TimeSpan.Zero));
            Assert.Equal(0, clock.PendingTimers);
        }

        [Fact]
        public async Task WaitAsync_WaitsForTheNextToken_WhenThatIsWithinTheMaxWait()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());

            var waiting = limiter.WaitAsync(TimeSpan.FromSeconds(2)).AsTask();
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, clock.PendingTimers);

            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.True(await waiting.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_ARefusalBecauseTheWaitIsTooLong_TakesNothing()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());

            Assert.False(await limiter.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(0, clock.PendingTimers);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryTake());
        }

        [Fact]
        public async Task WaitAsync_ARefusal_DoesNotMakeTheOnesThatComeAfterWaitLonger()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());
            Assert.False(await limiter.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(await limiter.WaitAsync(TimeSpan.FromMilliseconds(100)));

            var waiting = limiter.WaitAsync(TimeSpan.FromSeconds(1)).AsTask();
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.True(await waiting.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_AWaitThatIsExactlyTheMaxWait_IsNotRefused_AndOneMillisecondMoreIs()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());

            Assert.False(await limiter.WaitAsync(TimeSpan.FromMilliseconds(999)));
            var waiting = limiter.WaitAsync(TimeSpan.FromSeconds(1)).AsTask();
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.True(await waiting.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_TheOnesThatWait_GetTheirTurnInTheOrderTheyAskedAndOneAfterTheOther()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());

            var first = limiter.WaitAsync(TimeSpan.FromSeconds(10)).AsTask();
            var second = limiter.WaitAsync(TimeSpan.FromSeconds(10)).AsTask();
            var third = limiter.WaitAsync(TimeSpan.FromSeconds(10)).AsTask();
            Assert.Equal(3, clock.PendingTimers);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(await first.WaitAsync(Patience));
            Assert.False(second.IsCompleted);
            Assert.False(third.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(await second.WaitAsync(Patience));
            Assert.False(third.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(await third.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_AWaitingCommand_ReservesItsToken()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());
            var waiting = limiter.WaitAsync(TimeSpan.FromSeconds(5)).AsTask();

            // The token that is on its way is spoken for: a command that does not wait cannot take it.
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(await waiting.WaitAsync(Patience));
            Assert.False(limiter.TryTake());

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryTake());
        }

        [Fact]
        public async Task WaitAsync_ACommandThatIsCancelledWhileWaiting_GivesItsTokenBack()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());
            using var cancellation = new CancellationTokenSource();
            var cancelled = limiter.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).AsTask();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            // Had the token stayed reserved, the next command would have to wait two seconds, which is more than it is willing to.
            var next = limiter.WaitAsync(TimeSpan.FromMilliseconds(1500)).AsTask();
            Assert.False(next.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(await next.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_ACancellationThatIsAlreadyRequested_StillLeavesTheBalanceAsItWas()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 60, burst: 1, clock);
            Assert.True(limiter.TryTake());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await limiter.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token));

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryTake());
        }

        [Fact]
        public async Task WaitAsync_AWaitOfAFewMilliseconds_RunsOnTheClockOfTheLimiter()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 6000, burst: 1, clock);
            Assert.True(limiter.TryTake());

            var waiting = limiter.WaitAsync(TimeSpan.FromSeconds(1)).AsTask();
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(10));

            Assert.True(await waiting.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitAsync_OnTheSystemClock_WaitsForTheNextToken()
        {
            // 1200 a minute is twenty a second: a token every 50 ms.
            var limiter = new YeelightRateLimiter(perMinute: 1200, burst: 1);
            Assert.True(limiter.TryTake());

            Assert.True(await limiter.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ARateOfLessThanOneAMinute_IsRefused(int perMinute)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new YeelightRateLimiter(perMinute: perMinute));

            Assert.Equal("perMinute", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ABurstOfLessThanOne_IsRefused(int burst)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new YeelightRateLimiter(burst: burst));

            Assert.Equal("burst", refusal.ParamName);
        }

        [Fact]
        public void ARateAndABurstOfOne_AreTheSmallestThatAreAccepted()
        {
            var clock = new UnitClock();
            var limiter = new YeelightRateLimiter(perMinute: 1, burst: 1, clock);

            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromSeconds(59));
            Assert.False(limiter.TryTake());
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(limiter.TryTake());
        }

        [Fact]
        public void WithoutAClock_TheSystemClockIsUsed()
        {
            // A refill takes a minute at this rate: the third command is refused whatever the machine is doing.
            var limiter = new YeelightRateLimiter(perMinute: 1, burst: 2);

            Assert.True(limiter.TryTake());
            Assert.True(limiter.TryTake());
            Assert.False(limiter.TryTake());
        }
    }
}
