namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// The error that a command ended with: what a light answered in the <c>error</c> member of its answer (always <c>-1</c>), or why the
    /// answer never came (the codes from <c>-100</c> down, which are the client's own).
    /// </summary>
    public class YeelightErrorTests
    {
        [Theory]
        [InlineData(-100, YeelightFailureKind.NotConnected)]
        [InlineData(-101, YeelightFailureKind.Timeout)]
        [InlineData(-102, YeelightFailureKind.RateLimited)]
        [InlineData(-103, YeelightFailureKind.ConnectionLost)]
        public void TheCodesOfTheClient_AreTheFailuresOnTheWayToTheLight(int code, YeelightFailureKind kind)
        {
            var error = new YeelightError(code, "message");

            Assert.Equal(kind, error.Kind);
            Assert.True(error.IsTransportFailure);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-2)]
        [InlineData(-99)]
        [InlineData(-104)]
        [InlineData(-1000)]
        [InlineData(100)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void AnyOtherCode_IsARefusalOfTheLight(int code)
        {
            var error = new YeelightError(code, "message");

            Assert.Equal(YeelightFailureKind.DeviceRejected, error.Kind);
            Assert.False(error.IsTransportFailure);
        }

        [Fact]
        public void TheCodesOfTheClient_AreThoseThatTheDocumentationNames()
        {
            Assert.Equal(-100, YeelightError.NotConnectedCode);
            Assert.Equal(-101, YeelightError.TimeoutCode);
            Assert.Equal(-102, YeelightError.RateLimitedCode);
            Assert.Equal(-103, YeelightError.ConnectionLostCode);
        }

        [Theory]
        [InlineData(YeelightFailureKind.NotConnected, -100)]
        [InlineData(YeelightFailureKind.Timeout, -101)]
        [InlineData(YeelightFailureKind.RateLimited, -102)]
        [InlineData(YeelightFailureKind.ConnectionLost, -103)]
        [InlineData(YeelightFailureKind.DeviceRejected, -1)]
        public void Create_MakesTheErrorOfAKind_WithItsCode(YeelightFailureKind kind, int code)
        {
            var error = YeelightError.Create(kind, "the message");

            Assert.Equal(code, error.Code);
            Assert.Equal("the message", error.Message);
            Assert.Equal(kind, error.Kind);
        }

        [Fact]
        public void Create_OfAKindThatDoesNotExist_IsARefusal()
        {
            var error = YeelightError.Create((YeelightFailureKind)99, "message");

            Assert.Equal(-1, error.Code);
            Assert.Equal(YeelightFailureKind.DeviceRejected, error.Kind);
        }

        [Fact]
        public void EveryKind_SurvivesCreateAndKind()
        {
            Assert.All(Enum.GetValues<YeelightFailureKind>(), kind => Assert.Equal(kind, YeelightError.Create(kind, "x").Kind));
        }

        [Fact]
        public void EveryKindThatIsNotARefusal_IsATransportFailure()
        {
            Assert.All(
                Enum.GetValues<YeelightFailureKind>().Where(kind => kind != YeelightFailureKind.DeviceRejected),
                kind => Assert.True(YeelightError.Create(kind, "x").IsTransportFailure));
            Assert.False(YeelightError.Create(YeelightFailureKind.DeviceRejected, "x").IsTransportFailure);
        }

        [Fact]
        public void TwoErrorsOfTheSameCodeAndMessage_AreEqual()
        {
            var first = new YeelightError(-1, "unsupported method");
            var second = new YeelightError(-1, "unsupported method");

            Assert.Equal(first, second);
            Assert.True(first == second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.NotEqual(first, new YeelightError(-1, "other"));
            Assert.NotEqual(first, new YeelightError(-2, "unsupported method"));
        }

        [Fact]
        public void AnError_CanBeTakenApart_AndCopiedWithAnotherMessage()
        {
            var (code, message) = new YeelightError(-1, "unsupported method");
            var copy = new YeelightError(-1, "unsupported method") with { Message = "changed" };

            Assert.Equal(-1, code);
            Assert.Equal("unsupported method", message);
            Assert.Equal(new YeelightError(-1, "changed"), copy);
        }

        [Fact]
        public void ToString_NamesTheCodeAndTheMessage()
        {
            var text = new YeelightError(-1, "client quota exceeded").ToString();

            Assert.Contains("-1", text);
            Assert.Contains("client quota exceeded", text);
        }
    }
}
