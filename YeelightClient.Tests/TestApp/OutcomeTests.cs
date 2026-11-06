namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>What the test app says about a command that succeeded, and about every way in which one can fail.</summary>
    public sealed class OutcomeTests
    {
        [Fact]
        public void ACommandThatSucceeded_SaysSoWithTheTime()
        {
            var outcome = Outcome.Succeeded("Set brightness", TimeSpan.FromMilliseconds(12));

            Assert.True(outcome.IsSuccess);
            Assert.Equal(OutcomeSeverity.Success, outcome.Severity);
            Assert.Contains("Set brightness", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("12 ms", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARefusalOfTheLight_ShowsTheKindTheErrorCodeAndTheMessage()
        {
            var exception = new YeelightException(new YeelightError(-1, "general error"), "set_bright");

            var outcome = Outcome.Failed("Set brightness", exception, TimeSpan.FromMilliseconds(8));

            Assert.Equal(OutcomeSeverity.Warning, outcome.Severity);
            Assert.False(outcome.IsSuccess);
            Assert.Contains("refused", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("set_bright", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("DeviceRejected", outcome.Message, StringComparison.Ordinal);
            Assert.Contains("-1", outcome.Message, StringComparison.Ordinal);
            Assert.Contains("general error", outcome.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(YeelightFailureKind.NotConnected, -100)]
        [InlineData(YeelightFailureKind.Timeout, -101)]
        [InlineData(YeelightFailureKind.RateLimited, -102)]
        [InlineData(YeelightFailureKind.ConnectionLost, -103)]
        public void AFailureOnTheWayToTheLight_IsAnError_WithItsKindAndCode(YeelightFailureKind kind, int code)
        {
            var exception = new YeelightException(YeelightError.Create(kind, "It did not work."), "set_power");

            var outcome = Outcome.Failed("Turn on", exception, TimeSpan.FromSeconds(3));

            Assert.Equal(OutcomeSeverity.Error, outcome.Severity);
            Assert.Contains(kind.ToString(), outcome.Message, StringComparison.Ordinal);
            Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome.Message, StringComparison.Ordinal);
            Assert.Contains("It did not work.", outcome.Message, StringComparison.Ordinal);
            Assert.Contains("did not get through", outcome.Title, StringComparison.Ordinal);
        }

        [Fact]
        public void AnExceptionThatIsNotMadeOfAnError_StillShowsItsMessage()
        {
            var outcome = Outcome.Failed("Connect", new YeelightException("The light cannot be reached."), TimeSpan.Zero);

            Assert.Contains("The light cannot be reached.", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACommandThatTheLightDoesNotList_IsAWarning()
        {
            var outcome = Outcome.Failed("Set colour", new NotSupportedException("The light does not support 'set_rgb'."), TimeSpan.FromMilliseconds(1));

            Assert.Equal(OutcomeSeverity.Warning, outcome.Severity);
            Assert.Contains("does not support", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("set_rgb", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnArgumentThatTheClientRefuses_IsAWarning_ThatSaysWhatIsAccepted()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => YeelightScene.Hsv(400, 0, 50));

            var outcome = Outcome.Failed("Set scene", exception, TimeSpan.Zero);

            Assert.Equal(OutcomeSeverity.Warning, outcome.Severity);
            Assert.Contains("refused the arguments", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("0 to 359", outcome.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACancelledCommand_IsNotAProblem()
        {
            var outcome = Outcome.Failed("Probe", new OperationCanceledException(), TimeSpan.FromSeconds(1));

            Assert.Equal(OutcomeSeverity.Informational, outcome.Severity);
        }

        [Fact]
        public void ADisposedClient_IsExplained()
        {
            var outcome = Outcome.Failed("Toggle", new ObjectDisposedException("YeelightClient"), TimeSpan.Zero);

            Assert.Equal(OutcomeSeverity.Warning, outcome.Severity);
            Assert.Contains("closed", outcome.Title, StringComparison.Ordinal);
        }

        [Fact]
        public void AnythingElse_IsAnError_ThatNamesTheException_AndNeverThrows()
        {
            var outcome = Outcome.Failed("Refresh", new InvalidOperationException("Something that nobody expected."), TimeSpan.Zero);

            Assert.Equal(OutcomeSeverity.Error, outcome.Severity);
            Assert.Contains("InvalidOperationException", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("Something that nobody expected.", outcome.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentNullException>(() => Outcome.Failed("x", null!, TimeSpan.Zero));
        }

        [Theory]
        [InlineData(0, "0 ms")]
        [InlineData(12, "12 ms")]
        [InlineData(999.4, "999 ms")]
        [InlineData(1000, "1.0 s")]
        [InlineData(2500, "2.5 s")]
        public void TheTime_IsWrittenTheWayAPersonReadsIt(double milliseconds, string expected)
        {
            Assert.Equal(expected, Outcome.FormatElapsed(TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Fact]
        public async Task TheOutcomesOfTheRealThing_AreTheOnesThatAreShown()
        {
            await using var harness = await Harness.StartAsync();
            await harness.SettleAsync();
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            // A light that is off refuses a change of brightness.
            var refused = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.SetBrightnessAsync(50));
            // The client refuses a brightness that no light takes, before anything is sent.
            var arguments = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.SetBrightnessAsync(101));
            // A light that does not list a method.
            await using var plain = await Harness.StartAsync(new SimulatedBulbOptions { Support = "get_prop set_power toggle", Id = "0x0000000000000002" });
            var unsupported = await Assert.ThrowsAsync<NotSupportedException>(() => plain.Device.SetRgbAsync(1, 2, 3));

            Assert.Equal(OutcomeSeverity.Warning, Outcome.Failed("Set brightness", refused, TimeSpan.Zero).Severity);
            Assert.Contains("general error", Outcome.Failed("Set brightness", refused, TimeSpan.Zero).Message, StringComparison.Ordinal);
            Assert.Equal(OutcomeSeverity.Warning, Outcome.Failed("Set brightness", arguments, TimeSpan.Zero).Severity);
            Assert.Equal(OutcomeSeverity.Warning, Outcome.Failed("Set colour", unsupported, TimeSpan.Zero).Severity);
        }
    }
}
