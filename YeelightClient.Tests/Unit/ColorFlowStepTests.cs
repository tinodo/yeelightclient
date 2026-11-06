namespace Yeelight.Tests.Unit
{
    using System;
    using System.Drawing;
    using Xunit;

    /// <summary>
    /// One step of a colour flow: the four numbers of a flow tuple of start_cf (duration, mode, value, brightness; section 4.1 of the Yeelight
    /// Inter-Operation Specification, https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf), made safe to build.
    /// </summary>
    public class ColorFlowStepTests
    {
        private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);

        [Fact]
        public void Rgb_IsAChangeOfColor_WithTheNumberOfTheColorAndABrightness()
        {
            var step = ColorFlowStep.Rgb(255, 0, 0, Half, 50);

            Assert.Equal(ColorFlowMode.Rgb, step.Mode);
            Assert.Equal(16711680, step.Value);
            Assert.Equal(50, step.Brightness);
            Assert.Equal(Half, step.Duration);
            Assert.Equal(500, step.Milliseconds);
            Assert.Equal("500,1,16711680,50", step.ToExpression());
        }

        [Fact]
        public void Rgb_OfAColor_IgnoresItsAlpha()
        {
            var step = ColorFlowStep.Rgb(Color.FromArgb(10, 0, 255, 0), Half, 100);

            Assert.Equal(ColorFlowMode.Rgb, step.Mode);
            Assert.Equal(65280, step.Value);
            Assert.Equal("500,1,65280,100", step.ToExpression());
        }

        [Fact]
        public void Rgb_WithoutABrightness_LeavesTheBrightnessAsItIs()
        {
            var step = ColorFlowStep.Rgb(0, 0, 255, Half);

            Assert.Null(step.Brightness);
            Assert.Equal("500,1,255,-1", step.ToExpression());
            Assert.Null(ColorFlowStep.Rgb(Color.Red, Half).Brightness);
        }

        [Fact]
        public void ColorTemperature_IsAChangeOfColorTemperature()
        {
            var step = ColorFlowStep.ColorTemperature(2700, TimeSpan.FromSeconds(1), 100);

            Assert.Equal(ColorFlowMode.ColorTemperature, step.Mode);
            Assert.Equal(2700, step.Value);
            Assert.Equal(100, step.Brightness);
            Assert.Equal(TimeSpan.FromSeconds(1), step.Duration);
            Assert.Equal("1000,2,2700,100", step.ToExpression());
            Assert.Equal("1000,2,2700,-1", ColorFlowStep.ColorTemperature(2700, TimeSpan.FromSeconds(1)).ToExpression());
        }

        [Fact]
        public void Sleep_DoesNothing_ForAWhile()
        {
            var step = ColorFlowStep.Sleep(TimeSpan.FromSeconds(5));

            Assert.Equal(ColorFlowMode.Sleep, step.Mode);
            Assert.Equal(0, step.Value);
            Assert.Null(step.Brightness);
            Assert.Equal(TimeSpan.FromSeconds(5), step.Duration);
            Assert.Equal("5000,7,0,0", step.ToExpression());
        }

        [Fact]
        public void TheTuplesOfTheExampleOfTheSpecification_AreMadeByTheFactories()
        {
            // "change CT to 2700K & maximum brightness gradually in 1000ms, then change color to ... & 10% brightness gradually in 500ms, then
            // stay at this state for 5 seconds, then change CT to 5000K & minimum brightness gradually in 500ms".
            Assert.Equal("1000,2,2700,100", ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMilliseconds(1000), 100).ToExpression());
            Assert.Equal("500,1,255,10", ColorFlowStep.Rgb(0, 0, 255, Half, 10).ToExpression());
            Assert.Equal("5000,7,0,0", ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(5000)).ToExpression());
            Assert.Equal("500,2,5000,1", ColorFlowStep.ColorTemperature(5000, Half, 1).ToExpression());
        }

        [Theory]
        [InlineData(50)]
        [InlineData(51)]
        [InlineData(60_000)]
        [InlineData(int.MaxValue)]
        public void ADurationOfAtLeast50Milliseconds_IsAccepted(int milliseconds)
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);

            Assert.Equal(milliseconds, ColorFlowStep.Rgb(1, 2, 3, duration).Milliseconds);
            Assert.Equal(milliseconds, ColorFlowStep.Rgb(Color.Red, duration).Milliseconds);
            Assert.Equal(milliseconds, ColorFlowStep.ColorTemperature(3000, duration).Milliseconds);
            Assert.Equal(milliseconds, ColorFlowStep.Sleep(duration).Milliseconds);
        }

        [Theory]
        [InlineData(49)]
        [InlineData(30)]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-50)]
        public void ADurationOfLessThan50Milliseconds_IsRefused_ByEveryFactory(int milliseconds)
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            Action[] makes =
            [
                () => ColorFlowStep.Rgb(1, 2, 3, duration),
                () => ColorFlowStep.Rgb(Color.Red, duration),
                () => ColorFlowStep.ColorTemperature(3000, duration),
                () => ColorFlowStep.Sleep(duration),
            ];

            Assert.All(makes, make =>
            {
                var refusal = Assert.Throws<ArgumentOutOfRangeException>(make);
                Assert.Equal("duration", refusal.ParamName);
                Assert.Contains("50 milliseconds", refusal.Message);
            });
        }

        [Fact]
        public void ADurationThatDoesNotFitInWholeMilliseconds_IsRefused()
        {
            var tooLong = TimeSpan.FromMilliseconds(int.MaxValue + 1L);

            Assert.Throws<ArgumentOutOfRangeException>(() => ColorFlowStep.Sleep(tooLong));
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorFlowStep.Sleep(TimeSpan.MaxValue));
        }

        [Fact]
        public void ADuration_IsRoundedToTheNearestMillisecond()
        {
            Assert.Equal(500, ColorFlowStep.Sleep(TimeSpan.FromTicks(5_004_000)).Milliseconds);
            Assert.Equal(501, ColorFlowStep.Sleep(TimeSpan.FromTicks(5_006_000)).Milliseconds);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        [InlineData(100)]
        public void ABrightnessFrom1To100_IsAccepted(int brightness)
        {
            Assert.Equal(brightness, ColorFlowStep.Rgb(1, 2, 3, Half, brightness).Brightness);
            Assert.Equal(brightness, ColorFlowStep.Rgb(Color.Blue, Half, brightness).Brightness);
            Assert.Equal(brightness, ColorFlowStep.ColorTemperature(3000, Half, brightness).Brightness);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ABrightnessOutsideTheRange_IsRefused_ByEveryFactoryThatHasOne(int brightness)
        {
            Action[] makes =
            [
                () => ColorFlowStep.Rgb(1, 2, 3, Half, brightness),
                () => ColorFlowStep.Rgb(Color.Blue, Half, brightness),
                () => ColorFlowStep.ColorTemperature(3000, Half, brightness),
            ];

            Assert.All(makes, make => Assert.Equal("brightness", Assert.Throws<ArgumentOutOfRangeException>(make).ParamName));
        }

        [Theory]
        [InlineData(1700)]
        [InlineData(6500)]
        public void AColorTemperatureFrom1700To6500_IsAccepted(int kelvin)
        {
            Assert.Equal(kelvin, ColorFlowStep.ColorTemperature(kelvin, Half).Value);
        }

        [Theory]
        [InlineData(1699)]
        [InlineData(6501)]
        [InlineData(0)]
        public void AColorTemperatureOutsideTheRange_IsRefused(int kelvin)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => ColorFlowStep.ColorTemperature(kelvin, Half));

            Assert.Equal("kelvin", refusal.ParamName);
        }

        [Theory]
        [InlineData(-1, 0, 0, "red")]
        [InlineData(256, 0, 0, "red")]
        [InlineData(0, -1, 0, "green")]
        [InlineData(0, 256, 0, "green")]
        [InlineData(0, 0, -1, "blue")]
        [InlineData(0, 0, 256, "blue")]
        public void APartOfAColorOutsideTheRange_IsRefused(int red, int green, int blue, string name)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => ColorFlowStep.Rgb(red, green, blue, Half));

            Assert.Equal(name, refusal.ParamName);
        }

        [Fact]
        public void ToString_SaysWhatTheStepDoes()
        {
            Assert.Equal("Change colour to #FF0000 and brightness to 50% over 500ms", ColorFlowStep.Rgb(255, 0, 0, Half, 50).ToString());
            Assert.Equal("Change colour to #0000FF over 500ms", ColorFlowStep.Rgb(0, 0, 255, Half).ToString());
            Assert.Equal("Change colour temperature to 2700K and brightness to 100% over 1000ms", ColorFlowStep.ColorTemperature(2700, TimeSpan.FromSeconds(1), 100).ToString());
            Assert.Equal("Change colour temperature to 6500K over 50ms", ColorFlowStep.ColorTemperature(6500, TimeSpan.FromMilliseconds(50)).ToString());
            Assert.Equal("Sleep 5000ms", ColorFlowStep.Sleep(TimeSpan.FromSeconds(5)).ToString());
        }

        [Theory]
        [InlineData(1000, 2, 2700, 100, ColorFlowMode.ColorTemperature, 2700, 100)]
        [InlineData(500, 1, 255, 10, ColorFlowMode.Rgb, 255, 10)]
        [InlineData(500, 1, 255, -1, ColorFlowMode.Rgb, 255, null)]
        [InlineData(500, 1, 255, 0, ColorFlowMode.Rgb, 255, null)]
        [InlineData(500, 1, 255, 101, ColorFlowMode.Rgb, 255, null)]
        [InlineData(500, 2, 4000, 1, ColorFlowMode.ColorTemperature, 4000, 1)]
        [InlineData(5000, 7, 0, 0, ColorFlowMode.Sleep, 0, null)]
        [InlineData(5000, 7, 99, 50, ColorFlowMode.Sleep, 0, null)]
        [InlineData(0, 1, 255, 10, ColorFlowMode.Rgb, 255, 10)]
        public void TryCreate_OfWhatALightReports_TakesItAsItIs_WithoutTheChecksOfAPerson(int milliseconds, int mode, int value, int brightness, ColorFlowMode expectedMode, int expectedValue, int? expectedBrightness)
        {
            Assert.True(ColorFlowStep.TryCreate(milliseconds, mode, value, brightness, out var step));

            Assert.NotNull(step);
            Assert.Equal(expectedMode, step.Mode);
            Assert.Equal(expectedValue, step.Value);
            Assert.Equal(expectedBrightness, step.Brightness);
            Assert.Equal(milliseconds, step.Milliseconds);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(500, 0)]
        [InlineData(500, 3)]
        [InlineData(500, 4)]
        [InlineData(500, 6)]
        [InlineData(500, 8)]
        [InlineData(500, -1)]
        [InlineData(500, int.MaxValue)]
        public void TryCreate_OfADurationThatIsNegativeOrAModeThatDoesNotExist_Fails(int milliseconds, int mode)
        {
            Assert.False(ColorFlowStep.TryCreate(milliseconds, mode, 255, 10, out var step));
            Assert.Null(step);
        }

        [Fact]
        public void ToExpression_ThenTryCreate_GivesTheSameStepBack()
        {
            ColorFlowStep[] steps =
            [
                ColorFlowStep.Rgb(1, 2, 3, Half, 42),
                ColorFlowStep.Rgb(1, 2, 3, Half),
                ColorFlowStep.ColorTemperature(2200, TimeSpan.FromMilliseconds(50), 1),
                ColorFlowStep.ColorTemperature(6500, TimeSpan.FromHours(1)),
                ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(50)),
            ];

            foreach (var step in steps)
            {
                var numbers = Array.ConvertAll(step.ToExpression().Split(','), int.Parse);
                Assert.True(ColorFlowStep.TryCreate(numbers[0], numbers[1], numbers[2], numbers[3], out var again));

                Assert.Equal(step.Mode, again!.Mode);
                Assert.Equal(step.Value, again.Value);
                Assert.Equal(step.Brightness, again.Brightness);
                Assert.Equal(step.Milliseconds, again.Milliseconds);
            }
        }
    }
}
