namespace Yeelight.Tests.Unit
{
    using System;
    using Xunit;

    /// <summary>
    /// Thrown when a command does not succeed: the light refused it, or it did not get to the light, or its answer did not come back.
    /// </summary>
    public class YeelightExceptionTests
    {
        [Fact]
        public void AnException_IsAnExceptionThatCanBeDerivedFrom()
        {
            Assert.True(typeof(Exception).IsAssignableFrom(typeof(YeelightException)));
            Assert.False(typeof(YeelightException).IsSealed);
        }

        [Fact]
        public void TheDefaultException_HasNoErrorAndNoMethod()
        {
            var exception = new YeelightException();

            Assert.Null(exception.Error);
            Assert.Null(exception.Method);
            Assert.Null(exception.InnerException);
            Assert.False(string.IsNullOrEmpty(exception.Message));
        }

        [Fact]
        public void AnExceptionWithAMessage_HasThatMessage()
        {
            var exception = new YeelightException("something went wrong");

            Assert.Equal("something went wrong", exception.Message);
            Assert.Null(exception.Error);
            Assert.Null(exception.Method);
            Assert.Null(exception.InnerException);
        }

        [Fact]
        public void AnExceptionWithAMessageAndAnInnerException_HasBoth()
        {
            var inner = new InvalidOperationException("the cause");

            var exception = new YeelightException("something went wrong", inner);

            Assert.Equal("something went wrong", exception.Message);
            Assert.Same(inner, exception.InnerException);
            Assert.Null(exception.Error);
        }

        [Fact]
        public void AnExceptionThatWasNotMadeFromAnError_IsARefusalByDefault()
        {
            Assert.Equal(YeelightFailureKind.DeviceRejected, new YeelightException("x").Kind);
            Assert.Equal(YeelightFailureKind.DeviceRejected, new YeelightException().Kind);
        }

        [Fact]
        public void AnExceptionOfARefusal_NamesTheMethodTheMessageAndTheCode()
        {
            var error = new YeelightError(-1, "unsupported method");

            var exception = new YeelightException(error, "set_music");

            Assert.Equal("The light refused 'set_music': unsupported method (code -1).", exception.Message);
            Assert.Equal("set_music", exception.Method);
            Assert.Same(error, exception.Error);
            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
        }

        [Fact]
        public void AnExceptionOfARefusal_WithoutAMethod_SpeaksOfTheCommand()
        {
            var exception = new YeelightException(new YeelightError(-1, "invalid params"), null);

            Assert.Equal("The light refused the command: invalid params (code -1).", exception.Message);
            Assert.Null(exception.Method);
        }

        [Theory]
        [InlineData(YeelightFailureKind.NotConnected)]
        [InlineData(YeelightFailureKind.Timeout)]
        [InlineData(YeelightFailureKind.RateLimited)]
        [InlineData(YeelightFailureKind.ConnectionLost)]
        public void AnExceptionOfAFailureOnTheWayToTheLight_HasTheMessageOfTheClient_AndItsKind(YeelightFailureKind kind)
        {
            var error = YeelightError.Create(kind, "The light did not answer within 3 seconds.");

            var exception = new YeelightException(error, "toggle");

            Assert.Equal("The light did not answer within 3 seconds.", exception.Message);
            Assert.Equal(kind, exception.Kind);
            Assert.Equal("toggle", exception.Method);
            Assert.Same(error, exception.Error);
        }

        [Fact]
        public void TheKind_IsTheKindOfTheError()
        {
            Assert.All(
                Enum.GetValues<YeelightFailureKind>(),
                kind => Assert.Equal(kind, new YeelightException(YeelightError.Create(kind, "x"), "m").Kind));
        }
    }
}
