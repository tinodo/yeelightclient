namespace Yeelight.Tests.Unit
{
    using System;
    using Xunit;

    /// <summary>
    /// What a light is doing, as it told: one property at a time (section 4.3, table 4-2, of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf). A value that is not one (text where a number is, a number out
    /// of range) is ignored and the state keeps what it had; a value that is the same as the state does not count as a change.
    /// </summary>
    public class LightStateTests
    {
        private static string? Apply(LightState state, string key, string value)
        {
            var changed = state.Apply(key, value, out var handled);

            Assert.True(handled, $"{key} should be a property of the state of a light.");
            return changed;
        }

        [Fact]
        public void ALightThatHasToldNothing_IsOffAtZeroInRgbMode()
        {
            var state = new LightState();

            Assert.False(state.IsOn);
            Assert.Equal(0, state.Brightness);
            Assert.Equal(ColorMode.Rgb, state.ColorMode);
            Assert.Equal(0, state.ColorTemperature);
            Assert.Equal(0, state.Rgb);
            Assert.Equal(0, state.Hue);
            Assert.Equal(0, state.Saturation);
            Assert.False(state.IsFlowing);
            Assert.Equal(string.Empty, state.FlowParameters);
        }

        [Fact]
        public void Power_IsOnOrOff_AndOnlyChangesWhenItIsAnotherValue()
        {
            var state = new LightState();

            Assert.Null(Apply(state, "power", "off"));
            Assert.Equal("IsOn", Apply(state, "power", "on"));
            Assert.True(state.IsOn);
            Assert.Null(Apply(state, "power", "on"));
            Assert.Equal("IsOn", Apply(state, "power", "off"));
            Assert.False(state.IsOn);
        }

        [Theory]
        [InlineData("ON")]
        [InlineData("Off")]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData(" on")]
        [InlineData("on ")]
        [InlineData("")]
        public void Power_OfAnyOtherText_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "power", "on");

            Assert.Null(Apply(state, "power", value));
            Assert.True(state.IsOn);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("10", 10)]
        [InlineData("50", 50)]
        [InlineData("99", 99)]
        [InlineData("100", 100)]
        public void Brightness_From1To100_IsTaken(string value, int expected)
        {
            var state = new LightState();

            Assert.Equal("Brightness", Apply(state, "bright", value));
            Assert.Equal(expected, state.Brightness);
            Assert.Null(Apply(state, "bright", value));
        }

        [Fact]
        public void Brightness_OfZero_IsTakenToo_ButIsNotAChangeForALightThatHasNone()
        {
            var state = new LightState();

            Assert.Null(Apply(state, "bright", "0"));
            Apply(state, "bright", "40");
            Assert.Equal("Brightness", Apply(state, "bright", "0"));
            Assert.Equal(0, state.Brightness);
        }

        [Theory]
        [InlineData("101")]
        [InlineData("-1")]
        [InlineData("1000")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("5.5")]
        [InlineData("1e1")]
        [InlineData("0x10")]
        [InlineData("99999999999")]
        public void Brightness_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "bright", "40");

            Assert.Null(Apply(state, "bright", value));
            Assert.Equal(40, state.Brightness);
        }

        [Theory]
        [InlineData("1700", 1700)]
        [InlineData("2700", 2700)]
        [InlineData("4000", 4000)]
        [InlineData("6500", 6500)]
        [InlineData("100000", 100_000)]
        public void ColorTemperature_InKelvin_IsTaken(string value, int expected)
        {
            var state = new LightState();

            Assert.Equal("ColorTemperature", Apply(state, "ct", value));
            Assert.Equal(expected, state.ColorTemperature);
            Assert.Null(Apply(state, "ct", value));
        }

        [Theory]
        [InlineData("100001")]
        [InlineData("-1")]
        [InlineData("warm")]
        [InlineData("")]
        [InlineData("2700.5")]
        public void ColorTemperature_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "ct", "2700");

            Assert.Null(Apply(state, "ct", value));
            Assert.Equal(2700, state.ColorTemperature);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("255", 255)]
        [InlineData("16711680", 0xFF0000)]
        [InlineData("16777215", 0xFFFFFF)]
        public void Rgb_UpTo16777215_IsTaken(string value, int expected)
        {
            var state = new LightState();

            Assert.Equal("Rgb", Apply(state, "rgb", value));
            Assert.Equal(expected, state.Rgb);
            Assert.Null(Apply(state, "rgb", value));
        }

        [Theory]
        [InlineData("16777216")]
        [InlineData("-1")]
        [InlineData("ff0000")]
        [InlineData("#ff0000")]
        [InlineData("")]
        public void Rgb_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "rgb", "255");

            Assert.Null(Apply(state, "rgb", value));
            Assert.Equal(255, state.Rgb);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("180", 180)]
        [InlineData("359", 359)]
        public void Hue_From0To359_IsTaken(string value, int expected)
        {
            var state = new LightState();
            Apply(state, "hue", "10");

            Assert.Equal("Hue", Apply(state, "hue", value));
            Assert.Equal(expected, state.Hue);
            Assert.Null(Apply(state, "hue", value));
        }

        [Theory]
        [InlineData("360")]
        [InlineData("-1")]
        [InlineData("blue")]
        [InlineData("")]
        public void Hue_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "hue", "120");

            Assert.Null(Apply(state, "hue", value));
            Assert.Equal(120, state.Hue);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("35", 35)]
        [InlineData("100", 100)]
        public void Saturation_From0To100_IsTaken(string value, int expected)
        {
            var state = new LightState();
            Apply(state, "sat", "10");

            Assert.Equal("Saturation", Apply(state, "sat", value));
            Assert.Equal(expected, state.Saturation);
            Assert.Null(Apply(state, "sat", value));
        }

        [Theory]
        [InlineData("101")]
        [InlineData("-1")]
        [InlineData("full")]
        [InlineData("")]
        public void Saturation_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "sat", "70");

            Assert.Null(Apply(state, "sat", value));
            Assert.Equal(70, state.Saturation);
        }

        [Theory]
        [InlineData("2", ColorMode.ColorTemperature)]
        [InlineData("3", ColorMode.Hsv)]
        public void ColorMode_1To3_IsTaken(string value, ColorMode expected)
        {
            var state = new LightState();

            Assert.Equal("ColorMode", Apply(state, "color_mode", value));
            Assert.Equal(expected, state.ColorMode);
            Assert.Null(Apply(state, "color_mode", value));
        }

        [Fact]
        public void ColorMode_Rgb_IsWhatALightThatHasToldNothingIsIn_SoItIsNoChange()
        {
            var state = new LightState();

            Assert.Null(Apply(state, "color_mode", "1"));
            Apply(state, "color_mode", "3");
            Assert.Equal("ColorMode", Apply(state, "color_mode", "1"));
            Assert.Equal(ColorMode.Rgb, state.ColorMode);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("4")]
        [InlineData("-1")]
        [InlineData("rgb")]
        [InlineData("")]
        public void ColorMode_OfAnythingElse_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "color_mode", "3");

            Assert.Null(Apply(state, "color_mode", value));
            Assert.Equal(ColorMode.Hsv, state.ColorMode);
        }

        [Fact]
        public void Flowing_IsOneOrZero()
        {
            var state = new LightState();

            Assert.Null(Apply(state, "flowing", "0"));
            Assert.Equal("IsFlowing", Apply(state, "flowing", "1"));
            Assert.True(state.IsFlowing);
            Assert.Null(Apply(state, "flowing", "1"));
            Assert.Equal("IsFlowing", Apply(state, "flowing", "0"));
            Assert.False(state.IsFlowing);
        }

        [Theory]
        [InlineData("2")]
        [InlineData("-1")]
        [InlineData("true")]
        [InlineData("on")]
        [InlineData("")]
        public void Flowing_OfAnyOtherText_IsIgnored(string value)
        {
            var state = new LightState();
            Apply(state, "flowing", "1");

            Assert.Null(Apply(state, "flowing", value));
            Assert.True(state.IsFlowing);
        }

        [Fact]
        public void FlowParameters_AreTheTextThatTheLightReports()
        {
            var state = new LightState();

            Assert.Equal("FlowParameters", Apply(state, "flow_params", "0,0,1000,2,2700,100"));
            Assert.Equal("0,0,1000,2,2700,100", state.FlowParameters);
            Assert.Null(Apply(state, "flow_params", "0,0,1000,2,2700,100"));
            Assert.Equal("FlowParameters", Apply(state, "flow_params", "1,1,500,1,255,10"));
        }

        [Fact]
        public void FlowParameters_Empty_IsAChangeFromAFlow_AndNoneFromNothing()
        {
            var state = new LightState();

            Assert.Null(Apply(state, "flow_params", string.Empty));
            Apply(state, "flow_params", "0,0,1000,2,2700,100");
            Assert.Equal("FlowParameters", Apply(state, "flow_params", string.Empty));
            Assert.Equal(string.Empty, state.FlowParameters);
        }

        [Fact]
        public void FlowParameters_KeepWhateverTextThereIs()
        {
            var state = new LightState();

            Assert.Equal("FlowParameters", Apply(state, "flow_params", "not a flow"));
            Assert.Equal("not a flow", state.FlowParameters);
        }

        [Theory]
        [InlineData("foo")]
        [InlineData("")]
        [InlineData("Power")]
        [InlineData("POWER")]
        [InlineData("bg_power")]
        [InlineData("bg_lmode")]
        [InlineData("delayoff")]
        [InlineData("music_on")]
        [InlineData("name")]
        [InlineData("nl_br")]
        [InlineData("active_mode")]
        public void APropertyThatIsNotOneOfTheStateOfALight_IsNotHandled(string key)
        {
            var state = new LightState();

            var changed = state.Apply(key, "1", out var handled);

            Assert.False(handled);
            Assert.Null(changed);
        }

        [Fact]
        public void TheProperties_AreIndependentOfEachOther()
        {
            var state = new LightState();
            Apply(state, "power", "on");
            Apply(state, "bright", "70");
            Apply(state, "ct", "3000");
            Apply(state, "rgb", "255");
            Apply(state, "hue", "200");
            Apply(state, "sat", "50");
            Apply(state, "color_mode", "2");
            Apply(state, "flowing", "1");
            Apply(state, "flow_params", "x");

            Apply(state, "bright", "10");

            Assert.True(state.IsOn);
            Assert.Equal(10, state.Brightness);
            Assert.Equal(3000, state.ColorTemperature);
            Assert.Equal(255, state.Rgb);
            Assert.Equal(200, state.Hue);
            Assert.Equal(50, state.Saturation);
            Assert.Equal(ColorMode.ColorTemperature, state.ColorMode);
            Assert.True(state.IsFlowing);
            Assert.Equal("x", state.FlowParameters);
        }

        [Fact]
        public void TheNamesThatAreReturned_AreThoseOfThePropertiesOfALight()
        {
            // They are what PropertyChanged says, so an application can bind to them: a name that does not match a property is a binding that never updates.
            var state = new LightState();
            string Changed(string key, string value) => Apply(state, key, value) ?? "<no change>";
            var names = new[]
            {
                Changed("power", "on"),
                Changed("bright", "10"),
                Changed("ct", "2000"),
                Changed("rgb", "10"),
                Changed("hue", "10"),
                Changed("sat", "10"),
                Changed("color_mode", "3"),
                Changed("flowing", "1"),
                Changed("flow_params", "x"),
            };

            Assert.All(names, name => Assert.NotNull(typeof(YeelightLight).GetProperty(name)));
            Assert.Equal(["IsOn", "Brightness", "ColorTemperature", "Rgb", "Hue", "Saturation", "ColorMode", "IsFlowing", "FlowParameters"], names);
        }

        [Theory]
        [InlineData("0", 0, 100, true, 0)]
        [InlineData("100", 0, 100, true, 100)]
        [InlineData("101", 0, 100, false, 0)]
        [InlineData("-1", 0, 100, false, 0)]
        [InlineData("-5", -10, 10, true, -5)]
        [InlineData("abc", 0, 100, false, 0)]
        [InlineData("", 0, 100, false, 0)]
        [InlineData("2147483647", 0, int.MaxValue, true, int.MaxValue)]
        [InlineData("2147483648", 0, int.MaxValue, false, 0)]
        public void TryNumber_IsAWholeNumberWithinTheRange(string value, int minimum, int maximum, bool expected, int number)
        {
            Assert.Equal(expected, LightState.TryNumber(value, minimum, maximum, out var parsed));
            if (expected)
            {
                Assert.Equal(number, parsed);
            }
        }
    }
}
