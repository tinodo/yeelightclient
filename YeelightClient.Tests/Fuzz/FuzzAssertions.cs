namespace Yeelight.Tests.Fuzz
{
    using System;
    using Xunit.Sdk;

    /// <summary>
    /// What is true of the state of a light whatever it was told: the numbers are within the ranges that the protocol defines (or are what
    /// they were before a number that is not one was ignored), and the text is within bounds.
    /// </summary>
    internal static class FuzzAssertions
    {
        /// <summary>
        /// Checks the state of a light, of its background light, or of a device (which has more).
        /// </summary>
        /// <param name="light">The light.</param>
        /// <param name="described">Tells what came before, for the message of a failure.</param>
        public static void AssertSound(YeelightLight light, Func<string> described)
        {
            try
            {
                Assert.InRange(light.Brightness, 0, 100);
                Assert.InRange(light.ColorTemperature, 0, 100_000);
                Assert.InRange(light.Rgb, 0, YeelightLimits.MaxRgb);
                Assert.InRange(light.Hue, 0, YeelightLimits.MaxHue);
                Assert.InRange(light.Saturation, 0, YeelightLimits.MaxSaturation);
                Assert.True(Enum.IsDefined(light.ColorMode), "The colour mode is not one.");
                Assert.NotNull(light.FlowParameters);
                if (light is YeelightDevice device)
                {
                    Assert.NotNull(device.Name);
                    Assert.InRange(device.Name.Length, 0, 256);
                    Assert.InRange(device.NightLightBrightness, 0, 100);
                    Assert.True(device.ActiveMode is not { } mode || Enum.IsDefined(mode), "The active mode is not one.");
                    Assert.NotNull(device.Location);
                    Assert.NotNull(device.Model);
                }
            }
            catch (Exception exception)
            {
                throw new XunitException($"The state of the light is not sound after {described()}\n{exception.GetType().Name}: {exception.Message}", exception);
            }
        }
    }
}
