namespace Yeelight.Tests.Unit
{
    using System;
    using System.Drawing;
    using Xunit;

    /// <summary>
    /// A value that a light would refuse is refused before anything is sent, with an <see cref="ArgumentOutOfRangeException"/> that names the
    /// argument. The ranges are those of the Yeelight Inter-Operation Specification (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf,
    /// table 4-1 and the notes of the methods).
    /// </summary>
    public class ValidateTests
    {
        private static void AssertRefused(Action refused, string name, object? actual, string range)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(refused);

            Assert.Equal(name, refusal.ParamName);
            Assert.Equal(actual, refusal.ActualValue);
            Assert.Contains(range, refusal.Message);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(50)]
        [InlineData(99)]
        [InlineData(100)]
        public void Brightness_From1To100_IsAccepted_AndReturned(int value)
        {
            Assert.Equal(value, Validate.Brightness(value, "brightness"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        [InlineData(-1)]
        [InlineData(1000)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Brightness_OutsideTheRange_IsRefused_WithTheNameOfTheArgument(int value)
        {
            AssertRefused(() => Validate.Brightness(value, "level"), "level", value, "1 to 100");
        }

        [Theory]
        [InlineData(1700)]
        [InlineData(1701)]
        [InlineData(4000)]
        [InlineData(6499)]
        [InlineData(6500)]
        public void ColorTemperature_From1700To6500_IsAccepted_AndReturned(int value)
        {
            Assert.Equal(value, Validate.ColorTemperature(value, "kelvin"));
        }

        [Theory]
        [InlineData(1699)]
        [InlineData(6501)]
        [InlineData(0)]
        [InlineData(-1700)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void ColorTemperature_OutsideTheRange_IsRefused_WithTheNameOfTheArgument(int value)
        {
            AssertRefused(() => Validate.ColorTemperature(value, "temperature"), "temperature", value, "1700 to 6500");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(180)]
        [InlineData(358)]
        [InlineData(359)]
        public void Hue_From0To359_IsAccepted_AndReturned(int value)
        {
            Assert.Equal(value, Validate.Hue(value, "hue"));
        }

        [Theory]
        [InlineData(360)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Hue_OutsideTheRange_IsRefused_WithTheNameOfTheArgument(int value)
        {
            AssertRefused(() => Validate.Hue(value, "angle"), "angle", value, "0 to 359");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(99)]
        [InlineData(100)]
        public void Saturation_From0To100_IsAccepted_AndReturned(int value)
        {
            Assert.Equal(value, Validate.Saturation(value, "saturation"));
        }

        [Theory]
        [InlineData(101)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Saturation_OutsideTheRange_IsRefused_WithTheNameOfTheArgument(int value)
        {
            AssertRefused(() => Validate.Saturation(value, "chroma"), "chroma", value, "0 to 100");
        }

        [Theory]
        [InlineData(-100)]
        [InlineData(-99)]
        [InlineData(0)]
        [InlineData(99)]
        [InlineData(100)]
        public void Percentage_FromMinus100To100_IsAccepted_AndReturned(int value)
        {
            Assert.Equal(value, Validate.Percentage(value, "percentage"));
        }

        [Theory]
        [InlineData(-101)]
        [InlineData(101)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Percentage_OutsideTheRange_IsRefused_WithTheNameOfTheArgument(int value)
        {
            AssertRefused(() => Validate.Percentage(value, "change"), "change", value, "-100 to 100");
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(255, 0, 0, 16711680)]
        [InlineData(0, 255, 0, 65280)]
        [InlineData(0, 0, 255, 255)]
        [InlineData(255, 255, 0, 16776960)]
        [InlineData(255, 255, 255, 16777215)]
        [InlineData(1, 2, 3, 66051)]
        [InlineData(128, 64, 32, 8405024)]
        public void Rgb_IsRedTimes65536PlusGreenTimes256PlusBlue(int red, int green, int blue, int expected)
        {
            Assert.Equal(expected, Validate.Rgb(red, green, blue));
        }

        [Theory]
        [InlineData(-1, 0, 0, "red")]
        [InlineData(256, 0, 0, "red")]
        [InlineData(0, -1, 0, "green")]
        [InlineData(0, 256, 0, "green")]
        [InlineData(0, 0, -1, "blue")]
        [InlineData(0, 0, 256, "blue")]
        [InlineData(int.MaxValue, 0, 0, "red")]
        [InlineData(0, int.MinValue, 0, "green")]
        public void Rgb_APartOutsideTheRange_IsRefused_WithItsName(int red, int green, int blue, string name)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Rgb(red, green, blue));

            Assert.Equal(name, refusal.ParamName);
            Assert.Contains("0 to 255", refusal.Message);
        }

        [Fact]
        public void Rgb_OfAColor_IgnoresTheAlpha()
        {
            Assert.Equal(16711680, Validate.Rgb(Color.FromArgb(10, 255, 0, 0)));
            Assert.Equal(16711680, Validate.Rgb(Color.FromArgb(255, 255, 0, 0)));
            Assert.Equal(16711680, Validate.Rgb(Color.FromArgb(0, 255, 0, 0)));
            Assert.Equal(0x123456, Validate.Rgb(Color.FromArgb(unchecked((int)0xFF123456))));
            Assert.Equal(0xFF0000, Validate.Rgb(Color.Red));
            Assert.Equal(0, Validate.Rgb(Color.Empty));
        }

        [Fact]
        public void Transition_None_IsSudden()
        {
            Assert.Equal(("sudden", 0), Validate.Transition(null, "transition"));
        }

        [Fact]
        public void Transition_Zero_IsSudden()
        {
            Assert.Equal(("sudden", 0), Validate.Transition(TimeSpan.Zero, "transition"));
        }

        [Theory]
        [InlineData(30, 30)]
        [InlineData(31, 31)]
        [InlineData(500, 500)]
        [InlineData(1000, 1000)]
        [InlineData(60_000, 60_000)]
        [InlineData(int.MaxValue, int.MaxValue)]
        public void Transition_OfAtLeast30Milliseconds_IsSmooth(int milliseconds, int expected)
        {
            Assert.Equal(("smooth", expected), Validate.Transition(TimeSpan.FromMilliseconds(milliseconds), "transition"));
        }

        [Fact]
        public void Transition_IsRoundedToTheNearestMillisecond()
        {
            Assert.Equal(("smooth", 500), Validate.Transition(TimeSpan.FromTicks(5_004_000), "transition"));
            Assert.Equal(("smooth", 501), Validate.Transition(TimeSpan.FromTicks(5_006_000), "transition"));
        }

        [Theory]
        [InlineData(29)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(-500)]
        public void Transition_OfLessThan30Milliseconds_IsRefused(int milliseconds)
        {
            AssertRefused(() => Validate.Transition(TimeSpan.FromMilliseconds(milliseconds), "speed"), "speed", TimeSpan.FromMilliseconds(milliseconds), "30 milliseconds");
        }

        [Fact]
        public void Transition_OfOneTick_IsRefused_AndIsNotTakenForZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(TimeSpan.FromTicks(1), "transition"));
        }

        [Fact]
        public void Transition_ThatDoesNotFitInWholeMilliseconds_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(TimeSpan.FromMilliseconds(int.MaxValue + 1L), "transition"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(TimeSpan.FromDays(30), "transition"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(TimeSpan.MaxValue, "transition"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(TimeSpan.MinValue, "transition"));
        }

        [Fact]
        public void Duration_None_IsTheShortestThatALightSupports()
        {
            Assert.Equal(30, Validate.Duration(null, "duration"));
            Assert.Equal(30, Validate.Duration(TimeSpan.Zero, "duration"));
        }

        [Theory]
        [InlineData(30, 30)]
        [InlineData(500, 500)]
        [InlineData(int.MaxValue, int.MaxValue)]
        public void Duration_OfAtLeast30Milliseconds_IsInMilliseconds(int milliseconds, int expected)
        {
            Assert.Equal(expected, Validate.Duration(TimeSpan.FromMilliseconds(milliseconds), "duration"));
        }

        [Theory]
        [InlineData(29)]
        [InlineData(1)]
        [InlineData(-1)]
        public void Duration_OfLessThan30Milliseconds_IsRefused_WithTheNameOfTheArgument(int milliseconds)
        {
            AssertRefused(() => Validate.Duration(TimeSpan.FromMilliseconds(milliseconds), "length"), "length", TimeSpan.FromMilliseconds(milliseconds), "30 milliseconds");
        }

        [Fact]
        public void Duration_ThatDoesNotFitInWholeMilliseconds_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Duration(TimeSpan.FromMilliseconds(int.MaxValue + 1L), "duration"));
        }

        [Theory]
        [InlineData(50, 50, 50)]
        [InlineData(500, 50, 500)]
        [InlineData(5000, 50, 5000)]
        [InlineData(30, 30, 30)]
        public void Milliseconds_OfAtLeastTheMinimum_AreWholeMilliseconds(int milliseconds, int minimum, int expected)
        {
            Assert.Equal(expected, Validate.Milliseconds(TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMilliseconds(minimum), "duration"));
        }

        [Fact]
        public void Milliseconds_ThatRoundUpToTheMinimum_AreAccepted_AndThoseThatRoundDownAreNot()
        {
            var minimum = TimeSpan.FromMilliseconds(50);

            Assert.Equal(50, Validate.Milliseconds(TimeSpan.FromTicks(496_000), minimum, "duration"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Milliseconds(TimeSpan.FromTicks(494_000), minimum, "duration"));
        }

        [Theory]
        [InlineData(49, 50)]
        [InlineData(0, 50)]
        [InlineData(-50, 50)]
        [InlineData(29, 30)]
        public void Milliseconds_OfLessThanTheMinimum_AreRefused_AndTheMessageNamesTheMinimum(int milliseconds, int minimum)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Milliseconds(TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMilliseconds(minimum), "wait"));

            Assert.Equal("wait", refusal.ParamName);
            Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), refusal.ActualValue);
            Assert.Contains($"{minimum} milliseconds", refusal.Message);
        }
    }
}
