namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Yeelight.TestApp.Models;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>What the Raw page shows of the answer of a light: every part of it, the line of the history, and the outcome in the bar.</summary>
    public sealed class RawFormatTests
    {
        private static JsonElement Value(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static YeelightResult Success(params string[] values) => new(7, null, [.. values.Select(Value)]);

        private static YeelightResult Failure(int code, string message) => new(9, new YeelightError(code, message), []);

        [Fact]
        public void TheDetailsOfASuccess_HoldTheIdTheValuesAndTheTime()
        {
            var text = RawFormat.Details(Success("\"ok\""), TimeSpan.FromMilliseconds(12));

            Assert.Equal(
                $"Id:       7{Environment.NewLine}Success:  true{Environment.NewLine}Error:    none{Environment.NewLine}Values:   [\"ok\"]{Environment.NewLine}Elapsed:  12 ms",
                text);
        }

        [Fact]
        public void TheDetailsOfAFailure_HoldTheCodeTheMessageAndTheKind()
        {
            var text = RawFormat.Details(Failure(-1, "unsupported method"), TimeSpan.FromMilliseconds(1));

            Assert.Contains("Success:  false", text, StringComparison.Ordinal);
            Assert.Contains("Error:    code -1, \"unsupported method\", kind DeviceRejected", text, StringComparison.Ordinal);
            Assert.Contains("Values:   []", text, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDetailsOfALongAnswer_PutTheValuesOnTheirOwnLines()
        {
            var values = Enumerable.Range(0, 30).Select(index => "\"value-number-" + index + "\"").ToArray();

            var text = RawFormat.Details(Success(values), TimeSpan.FromSeconds(1.5));

            Assert.Contains($"Values:   {Environment.NewLine}[", text, StringComparison.Ordinal);
            Assert.Contains("\"value-number-29\"", text, StringComparison.Ordinal);
            Assert.EndsWith("Elapsed:  1.5 s", text, StringComparison.Ordinal);
        }

        [Fact]
        public void ATransportFailure_HasItsKindInTheDetails()
        {
            var text = RawFormat.Details(new YeelightResult(0, YeelightError.Create(YeelightFailureKind.Timeout, "The light did not answer."), []), TimeSpan.FromSeconds(3));

            Assert.Contains("kind Timeout", text, StringComparison.Ordinal);
            Assert.Contains("code -101", text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0, "success, no values")]
        [InlineData(1, "[\"ok\"]")]
        [InlineData(2, "[\"on\",\"100\"]")]
        public void TheSummaryOfASuccess_IsItsValues(int count, string expected)
        {
            var values = new[] { "\"on\"", "\"100\"" };
            var result = count switch
            {
                0 => Success(),
                1 => Success("\"ok\""),
                _ => Success(values),
            };

            Assert.Equal(expected, RawFormat.Summary(result));
        }

        [Fact]
        public void TheSummaryOfAFailure_IsItsCodeAndMessage()
        {
            Assert.Equal("error -1: general error", RawFormat.Summary(Failure(-1, "general error")));
        }

        [Fact]
        public void ASuccess_IsASuccessInTheBar()
        {
            var outcome = RawFormat.ToOutcome("set_power", Success("\"ok\""), TimeSpan.FromMilliseconds(8));

            Assert.True(outcome.IsSuccess);
            Assert.Contains("set_power", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("[\"ok\"]", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARefusalOfTheLight_IsAWarning()
        {
            var outcome = RawFormat.ToOutcome("set_bright", Failure(-1, "general error"), TimeSpan.FromMilliseconds(3));

            Assert.Equal(OutcomeSeverity.Warning, outcome.Severity);
            Assert.Contains("refused", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("DeviceRejected", outcome.Message, StringComparison.Ordinal);
            Assert.Contains("-1", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACommandThatDidNotGetThrough_IsAnError()
        {
            var outcome = RawFormat.ToOutcome("get_prop", new YeelightResult(0, YeelightError.Create(YeelightFailureKind.ConnectionLost, "The connection was lost."), []), TimeSpan.FromMilliseconds(300));

            Assert.Equal(OutcomeSeverity.Error, outcome.Severity);
            Assert.Contains("did not get through", outcome.Title, StringComparison.Ordinal);
            Assert.Contains("ConnectionLost", outcome.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ANullResult_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => RawFormat.Details(null!, TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => RawFormat.Summary(null!));
            Assert.Throws<ArgumentNullException>(() => RawFormat.ToOutcome("x", null!, TimeSpan.Zero));
        }

        [Theory]
        [InlineData("set_power", "[\"on\", \"sudden\", 0]", "set_power [\"on\", \"sudden\", 0]")]
        [InlineData("toggle", "[]", "toggle")]
        [InlineData("toggle", "", "toggle")]
        public void TheCommandOfAHistoryItem_IsTheMethodAndItsParameters(string method, string parameters, string expected)
        {
            var item = new RawHistoryItem(DateTimeOffset.Now, RawTarget.Device, method, parameters, true, "ok");

            Assert.Equal(expected, item.Command);
        }

        [Fact]
        public void AHistoryItem_SaysWhereItWentAndHowItWent()
        {
            var time = new DateTimeOffset(2026, 11, 6, 3, 4, 5, TimeSpan.Zero);

            var ok = new RawHistoryItem(time, RawTarget.Background, "bg_toggle", "[]", true, "ok");
            var failed = new RawHistoryItem(time, RawTarget.Music, "set_bright", "[1]", false, "x");

            Assert.Equal("background light", ok.TargetText);
            Assert.Equal("music session", failed.TargetText);
            Assert.Equal("light", RawHistoryItem.TargetName(RawTarget.Device));
            Assert.Equal("\u2713", ok.Mark);
            Assert.Equal("\u2717", failed.Mark);
            Assert.Equal(time.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ok.TimeText);
        }
    }
}
