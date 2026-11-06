namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The sleep timer: <c>cron_add</c>, <c>cron_get</c> and <c>cron_del</c> with the type 0 (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf),
    /// and what the light answers to <c>cron_get</c>: <c>[{"type": 0, "delay": 15, "mix": 0}]</c>, or nothing at all when there is no timer.
    /// </summary>
    public class SleepTimerTests
    {
        private static async Task<TimeSpan?> AskAsync(string result)
        {
            await using var h = await Wire.StartAsync();
            h.Bulb.ReplyOverride = request => request.Method == "cron_get" ? [$"{{\"id\":{request.Id},\"result\":{result}}}"] : null;

            return await h.Device.GetSleepTimerAsync();
        }

        [Fact]
        public async Task TheAnswerOfTheExampleOfTheSpecification_IsATimerOf15Minutes()
        {
            var timer = await AskAsync("""[{"type": 0, "delay": 15, "mix": 0}]""");

            Assert.Equal(TimeSpan.FromMinutes(15), timer);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("""[{"type":0,"mix":0}]""")]
        [InlineData("""[{"type":0,"delay":"soon"}]""")]
        [InlineData("""[{"type":0,"delay":null}]""")]
        [InlineData("""[{"type":0,"delay":1.5}]""")]
        [InlineData("""["ok"]""")]
        [InlineData("[1,null,true,[]]")]
        public async Task AnAnswerWithoutADelay_IsNoTimer(string result)
        {
            var timer = await AskAsync(result);

            Assert.Null(timer);
        }

        [Theory]
        [InlineData("""[{"type":0,"delay":"20","mix":0}]""", 20)]
        [InlineData("""[1,null,{"delay":7}]""", 7)]
        [InlineData("""["ok",{"delay":9}]""", 9)]
        [InlineData("""[{"delay":3},{"delay":9}]""", 3)]
        [InlineData("""[{"mix":0},{"delay":4}]""", 4)]
        [InlineData("""[{"type":0,"delay":60,"mix":0}]""", 60)]
        public async Task TheFirstTimerWithADelay_IsTheTimer(string result, int minutes)
        {
            var timer = await AskAsync(result);

            Assert.Equal(TimeSpan.FromMinutes(minutes), timer);
        }

        [Fact]
        public async Task AnErrorOfTheLight_IsAFailure_NotNoTimer()
        {
            await using var h = await Wire.StartAsync();
            h.Bulb.ReplyOverride = request => request.Method == "cron_get" ? [$"{{\"id\":{request.Id},\"error\":{{\"code\":-1,\"message\":\"general error\"}}}}"] : null;

            var failure = await Assert.ThrowsAsync<YeelightException>(() => h.Device.GetSleepTimerAsync());

            Assert.Equal(YeelightFailureKind.DeviceRejected, failure.Kind);
            Assert.Equal("cron_get", failure.Method);
        }

        [Fact]
        public async Task TheRemainingTimeOfTheTimer_FollowsWhatTheLightReportsAsDelayoff()
        {
            await using var h = await Wire.StartAsync();
            Assert.Equal(TimeSpan.Zero, h.Device.SleepTimerRemaining);

            await h.Device.SetSleepTimerAsync(TimeSpan.FromMinutes(15));
            await Wait.UntilAsync(() => h.Device.SleepTimerRemaining == TimeSpan.FromMinutes(15), "the report of the timer");
            await h.Bulb.ChangeAsync(("delayoff", "14"));
            await Wait.UntilAsync(() => h.Device.SleepTimerRemaining == TimeSpan.FromMinutes(14), "the report of the minute that passed");
            await h.Device.CancelSleepTimerAsync();

            await Wait.UntilAsync(() => h.Device.SleepTimerRemaining == TimeSpan.Zero, "the report that the timer is gone");
        }
    }
}
