namespace Yeelight.Tests.Unit
{
    using System;
    using Xunit;

    /// <summary>
    /// The limits of the protocol, as numbers that an application compiles in (a constant is copied into the code that uses it, so changing
    /// one is a break of the API, like renumbering an enum). The values are those of the Yeelight Inter-Operation Specification
    /// (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the ranges in the notes of set_bright, set_ct_abx, set_rgb,
    /// set_hsv, start_cf and adjust_*, the four connections and the 60 commands a minute of section 4, and the port of section 3.
    /// </summary>
    public class YeelightLimitsTests
    {
        [Fact]
        public void TheRangesOfTheValuesOfALight_AreThoseOfTheSpecification()
        {
            Assert.Equal(1, YeelightLimits.MinBrightness);
            Assert.Equal(100, YeelightLimits.MaxBrightness);
            Assert.Equal(1700, YeelightLimits.MinColorTemperature);
            Assert.Equal(6500, YeelightLimits.MaxColorTemperature);
            Assert.Equal(359, YeelightLimits.MaxHue);
            Assert.Equal(100, YeelightLimits.MaxSaturation);
            Assert.Equal(16777215, YeelightLimits.MaxRgb);
            Assert.Equal(0xFFFFFF, YeelightLimits.MaxRgb);
            Assert.Equal(100, YeelightLimits.MaxAdjustPercentage);
        }

        [Fact]
        public void TheShortestTransitionAndTheShortestStepOfAFlow_AreThoseOfTheSpecification()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(30), YeelightLimits.MinTransition);
            Assert.Equal(TimeSpan.FromMilliseconds(50), YeelightLimits.MinFlowStep);
        }

        [Fact]
        public void TheLimitsOfTheConnection_AreThoseOfTheSpecification()
        {
            Assert.Equal(4, YeelightLimits.ConnectionsPerLight);
            Assert.Equal(60, YeelightLimits.CommandsPerMinute);
            Assert.Equal(1982, YeelightLimits.DiscoveryPort);
        }

        [Fact]
        public void TheLimitsAreEdgesOfWhatIsAccepted_AndOneStepFurtherIsRefused()
        {
            Assert.Equal(YeelightLimits.MinBrightness, Validate.Brightness(YeelightLimits.MinBrightness, "x"));
            Assert.Equal(YeelightLimits.MaxBrightness, Validate.Brightness(YeelightLimits.MaxBrightness, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Brightness(YeelightLimits.MinBrightness - 1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Brightness(YeelightLimits.MaxBrightness + 1, "x"));

            Assert.Equal(YeelightLimits.MinColorTemperature, Validate.ColorTemperature(YeelightLimits.MinColorTemperature, "x"));
            Assert.Equal(YeelightLimits.MaxColorTemperature, Validate.ColorTemperature(YeelightLimits.MaxColorTemperature, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.ColorTemperature(YeelightLimits.MinColorTemperature - 1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.ColorTemperature(YeelightLimits.MaxColorTemperature + 1, "x"));

            Assert.Equal(YeelightLimits.MaxHue, Validate.Hue(YeelightLimits.MaxHue, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Hue(YeelightLimits.MaxHue + 1, "x"));

            Assert.Equal(YeelightLimits.MaxSaturation, Validate.Saturation(YeelightLimits.MaxSaturation, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Saturation(YeelightLimits.MaxSaturation + 1, "x"));

            Assert.Equal(YeelightLimits.MaxAdjustPercentage, Validate.Percentage(YeelightLimits.MaxAdjustPercentage, "x"));
            Assert.Equal(-YeelightLimits.MaxAdjustPercentage, Validate.Percentage(-YeelightLimits.MaxAdjustPercentage, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Percentage(YeelightLimits.MaxAdjustPercentage + 1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Percentage(-YeelightLimits.MaxAdjustPercentage - 1, "x"));

            Assert.Equal(YeelightLimits.MaxRgb, Validate.Rgb(255, 255, 255));
        }

        [Fact]
        public void TheShortestTransition_IsTheEdgeOfAWholeTransition()
        {
            Assert.Equal(("smooth", 30), Validate.Transition(YeelightLimits.MinTransition, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Validate.Transition(YeelightLimits.MinTransition - TimeSpan.FromMilliseconds(1), "x"));
        }

        [Fact]
        public void TheShortestStep_IsTheEdgeOfAStepOfAFlow()
        {
            Assert.Equal(YeelightLimits.MinFlowStep, ColorFlowStep.Sleep(YeelightLimits.MinFlowStep).Duration);
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorFlowStep.Sleep(YeelightLimits.MinFlowStep - TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public void TheDefaultsOfTheOptions_FollowTheLimitsOfTheProtocol()
        {
            var options = new YeelightClientOptions();

            Assert.Equal(YeelightLimits.CommandsPerMinute, options.CommandsPerMinute);
            Assert.Equal(YeelightLimits.DiscoveryPort, options.DiscoveryPort);
        }
    }
}
