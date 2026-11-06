namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// The names of the properties of a light, as the protocol has them (table 4-2 of
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf; <c>ProtocolCoverageTests</c> checks that the list is the table).
    /// </summary>
    public class YeelightPropertiesTests
    {
        [Fact]
        public void All_HasNoNameTwice_AndNoNameThatIsEmptyOrHasWhiteSpace()
        {
            Assert.Equal(YeelightProperties.All.Count, YeelightProperties.All.Distinct(StringComparer.Ordinal).Count());
            Assert.All(YeelightProperties.All, name => Assert.False(string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace), $"'{name}' is not a name."));
        }

        [Fact]
        public void EveryNameOfTheBackgroundLight_StartsWithThePrefix_AndNoOtherNameDoes()
        {
            var background = YeelightProperties.All.Where(name => name.StartsWith(YeelightProperties.BackgroundPrefix, StringComparison.Ordinal)).ToArray();

            Assert.Equal(["bg_power", "bg_flowing", "bg_flow_params", "bg_ct", "bg_lmode", "bg_bright", "bg_rgb", "bg_hue", "bg_sat"], background);
            Assert.Equal("bg_", YeelightProperties.BackgroundPrefix);
            Assert.Equal("bg_lmode", YeelightProperties.BackgroundColorMode);
        }

        [Fact]
        public void TheConstants_AreTheNamesOfTheSpecification_AndEveryOneIsInTheList()
        {
            var constants = typeof(YeelightProperties).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

            Assert.Equal("power", constants[nameof(YeelightProperties.Power)]);
            Assert.Equal("bright", constants[nameof(YeelightProperties.Brightness)]);
            Assert.Equal("ct", constants[nameof(YeelightProperties.ColorTemperature)]);
            Assert.Equal("rgb", constants[nameof(YeelightProperties.Rgb)]);
            Assert.Equal("hue", constants[nameof(YeelightProperties.Hue)]);
            Assert.Equal("sat", constants[nameof(YeelightProperties.Saturation)]);
            Assert.Equal("color_mode", constants[nameof(YeelightProperties.ColorMode)]);
            Assert.Equal("flowing", constants[nameof(YeelightProperties.Flowing)]);
            Assert.Equal("delayoff", constants[nameof(YeelightProperties.DelayOff)]);
            Assert.Equal("flow_params", constants[nameof(YeelightProperties.FlowParams)]);
            Assert.Equal("music_on", constants[nameof(YeelightProperties.MusicOn)]);
            Assert.Equal("name", constants[nameof(YeelightProperties.Name)]);
            Assert.Equal("nl_br", constants[nameof(YeelightProperties.NightLightBrightness)]);
            Assert.Equal("active_mode", constants[nameof(YeelightProperties.ActiveMode)]);
            var names = constants.Where(pair => pair.Key != nameof(YeelightProperties.BackgroundPrefix)).Select(pair => pair.Value).ToArray();
            Assert.All(names, name => Assert.Contains(name, YeelightProperties.All));
        }

        [Theory]
        [InlineData("power")]
        [InlineData("bright")]
        [InlineData("ct")]
        [InlineData("rgb")]
        [InlineData("hue")]
        [InlineData("sat")]
        [InlineData("color_mode")]
        [InlineData("flowing")]
        [InlineData("delayoff")]
        [InlineData("flow_params")]
        [InlineData("music_on")]
        [InlineData("name")]
        [InlineData("bg_power")]
        [InlineData("bg_flowing")]
        [InlineData("bg_flow_params")]
        [InlineData("bg_ct")]
        [InlineData("bg_lmode")]
        [InlineData("bg_bright")]
        [InlineData("bg_rgb")]
        [InlineData("bg_hue")]
        [InlineData("bg_sat")]
        [InlineData("nl_br")]
        [InlineData("active_mode")]
        public void IsKnown_EveryPropertyOfTable42_IsKnown(string name)
        {
            Assert.True(YeelightProperties.IsKnown(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("Power")]
        [InlineData("POWER")]
        [InlineData("power ")]
        [InlineData(" power")]
        [InlineData("bg_")]
        [InlineData("bg_color_mode")]
        [InlineData("bg_delayoff")]
        [InlineData("bg_name")]
        [InlineData("brightness")]
        [InlineData("flow_param")]
        [InlineData("not_exist")]
        [InlineData("support")]
        [InlineData("model")]
        public void IsKnown_ANameThatTable42DoesNotHave_IsNotKnown(string name)
        {
            Assert.False(YeelightProperties.IsKnown(name));
        }
    }
}
