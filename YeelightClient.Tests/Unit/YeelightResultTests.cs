namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Xunit;

    /// <summary>
    /// The answer to a command, as the light gave it (or the failure that took its place): <c>{"id":1,"result":["ok"]}</c> or
    /// <c>{"id":2,"error":{"code":-1,"message":"unsupported method"}}</c> (section 4.2 of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf).
    /// </summary>
    public class YeelightResultTests
    {
        private static JsonElement Element(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static YeelightResult Success(params string[] values)
        {
            return new YeelightResult(5, null, values.Select(Element).ToArray());
        }

        private static YeelightResult Failure(int code, string message)
        {
            return new YeelightResult(6, new YeelightError(code, message), []);
        }

        [Fact]
        public void AResultWithoutAnError_Succeeded()
        {
            var result = Success("\"ok\"");

            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.Equal(5, result.Id);
            Assert.Equal("ok", Assert.Single(result.Values).GetString());
        }

        [Fact]
        public void AResultWithAnError_DidNotSucceed()
        {
            var result = Failure(-1, "unsupported method");

            Assert.False(result.Success);
            Assert.Equal(new YeelightError(-1, "unsupported method"), result.Error);
            Assert.Empty(result.Values);
            Assert.Equal(6, result.Id);
        }

        [Fact]
        public void AnEmptyResult_IsValidAndSucceeded()
        {
            // The list of timers of a light that has none.
            var result = Success();

            Assert.True(result.Success);
            Assert.Empty(result.Values);
        }

        [Theory]
        [InlineData("\"on\"", "on")]
        [InlineData("\"\"", "")]
        [InlineData("\"100\"", "100")]
        [InlineData("100", "100")]
        [InlineData("-5", "-5")]
        [InlineData("1.50", "1.50")]
        [InlineData("true", null)]
        [InlineData("false", null)]
        [InlineData("null", null)]
        [InlineData("{\"delay\":15}", null)]
        [InlineData("[1,2]", null)]
        public void GetString_IsAStringAsItIsAndANumberAsItWasWritten_AndNothingElseIsText(string json, string? expected)
        {
            Assert.Equal(expected, Success(json).GetString());
        }

        [Fact]
        public void GetString_TakesTheValueAtTheIndex()
        {
            var result = Success("\"on\"", "100", "\"\"", "null");

            Assert.Equal("on", result.GetString(0));
            Assert.Equal("100", result.GetString(1));
            Assert.Equal(string.Empty, result.GetString(2));
            Assert.Null(result.GetString(3));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(100)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void GetString_AtAnIndexThatIsNotThere_IsNull(int index)
        {
            Assert.Null(Success("\"a\"", "\"b\"").GetString(index));
        }

        [Fact]
        public void GetString_OfAResultWithoutValues_IsNull()
        {
            Assert.Null(Success().GetString());
            Assert.Null(Failure(-1, "x").GetString());
        }

        [Fact]
        public void GetString_OfATextWithHalfOfASurrogatePair_NeverThrows()
        {
            var result = Success("\"o\\ud800n\"");

            var exception = Record.Exception(() => result.GetString());

            Assert.True(exception is null, $"GetString threw {exception?.GetType().Name}: {exception?.Message}");
        }

        [Fact]
        public void EnsureSuccess_OfASuccess_ReturnsTheResultItself()
        {
            var result = Success("\"ok\"");

            Assert.Same(result, result.EnsureSuccess());
            Assert.Same(result, result.EnsureSuccess("set_power"));
        }

        [Fact]
        public void EnsureSuccess_OfARefusal_ThrowsWhatTheLightSaid()
        {
            var result = Failure(-1, "invalid params");

            var exception = Assert.Throws<YeelightException>(() => result.EnsureSuccess("set_bright"));

            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
            Assert.Same(result.Error, exception.Error);
            Assert.Equal("set_bright", exception.Method);
            Assert.Equal("The light refused 'set_bright': invalid params (code -1).", exception.Message);
        }

        [Fact]
        public void EnsureSuccess_WithoutAMethod_NamesTheCommand()
        {
            var exception = Assert.Throws<YeelightException>(() => Failure(-1, "invalid params").EnsureSuccess());

            Assert.Null(exception.Method);
            Assert.Equal("The light refused the command: invalid params (code -1).", exception.Message);
        }

        [Theory]
        [InlineData(-100, YeelightFailureKind.NotConnected)]
        [InlineData(-101, YeelightFailureKind.Timeout)]
        [InlineData(-102, YeelightFailureKind.RateLimited)]
        [InlineData(-103, YeelightFailureKind.ConnectionLost)]
        public void EnsureSuccess_OfAFailureOnTheWayToTheLight_ThrowsItsKindAndItsOwnMessage(int code, YeelightFailureKind kind)
        {
            var result = Failure(code, "the way to the light failed");

            var exception = Assert.Throws<YeelightException>(() => result.EnsureSuccess("toggle"));

            Assert.Equal(kind, exception.Kind);
            Assert.Equal("toggle", exception.Method);
            Assert.Equal("the way to the light failed", exception.Message);
            Assert.Same(result.Error, exception.Error);
        }

        [Theory]
        [InlineData(YeelightFailureKind.NotConnected)]
        [InlineData(YeelightFailureKind.Timeout)]
        [InlineData(YeelightFailureKind.RateLimited)]
        [InlineData(YeelightFailureKind.ConnectionLost)]
        [InlineData(YeelightFailureKind.DeviceRejected)]
        public void Failed_IsAResultWithoutAnAnswer_ThatCarriesTheKindOfFailure(YeelightFailureKind kind)
        {
            var result = YeelightResult.Failed(kind, "no answer");

            Assert.False(result.Success);
            Assert.Equal(0, result.Id);
            Assert.Empty(result.Values);
            Assert.Equal(kind, result.Error!.Kind);
            Assert.Equal("no answer", result.Error.Message);
        }

        [Fact]
        public void ToString_SaysWhatTheLightAnswered()
        {
            Assert.Equal("Result 5: [ok]", Success("\"ok\"").ToString());
            Assert.Equal("Result 5: [on, 100]", Success("\"on\"", "\"100\"").ToString());
            Assert.Equal("Result 5: []", Success().ToString());
            Assert.Equal("Failed 6: unsupported method (-1)", Failure(-1, "unsupported method").ToString());
        }

        [Fact]
        public void TheValues_AreTheOnesThatWereGiven()
        {
            var values = new List<JsonElement> { Element("1"), Element("\"two\"") };

            var result = new YeelightResult(1, null, values);

            Assert.Equal(2, result.Values.Count);
            Assert.Equal(1, result.Values[0].GetInt32());
            Assert.Equal("two", result.Values[1].GetString());
        }
    }
}
