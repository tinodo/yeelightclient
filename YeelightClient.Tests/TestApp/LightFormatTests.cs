namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Models;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>How the test app shows the state of a light: the swatch of the colour that it shows, the words, and the rows of its details.</summary>
    public sealed class LightFormatTests
    {
        [Theory]
        [InlineData(0, 100, 100, 255, 0, 0)]
        [InlineData(60, 100, 100, 255, 255, 0)]
        [InlineData(120, 100, 100, 0, 255, 0)]
        [InlineData(180, 100, 100, 0, 255, 255)]
        [InlineData(240, 100, 100, 0, 0, 255)]
        [InlineData(300, 100, 100, 255, 0, 255)]
        [InlineData(0, 0, 100, 255, 255, 255)]
        [InlineData(0, 100, 0, 0, 0, 0)]
        [InlineData(0, 50, 100, 255, 128, 128)]
        [InlineData(360, 100, 100, 255, 0, 0)]
        [InlineData(-120, 100, 100, 0, 0, 255)]
        public void HsvToRgb_MakesTheColourOfAHue(int hue, int saturation, int value, int red, int green, int blue)
        {
            Assert.Equal(new Rgb24((byte)red, (byte)green, (byte)blue), LightFormat.HsvToRgb(hue, saturation, value));
        }

        [Theory]
        [InlineData(255, 0, 0, 0, 100, 100)]
        [InlineData(0, 255, 0, 120, 100, 100)]
        [InlineData(0, 0, 255, 240, 100, 100)]
        [InlineData(255, 255, 255, 0, 0, 100)]
        [InlineData(0, 0, 0, 0, 0, 0)]
        [InlineData(255, 140, 0, 33, 100, 100)]
        public void RgbToHsv_ReadsTheHueOfAColour(int red, int green, int blue, int hue, int saturation, int value)
        {
            Assert.Equal((hue, saturation, value), LightFormat.RgbToHsv(new Rgb24((byte)red, (byte)green, (byte)blue)));
        }

        [Fact]
        public void AColourThatGoesToHsvAndBack_IsTheSameColourWithinARoundingError()
        {
            foreach (var color in new[] { new Rgb24(200, 30, 60), new Rgb24(12, 140, 220), new Rgb24(250, 250, 10), new Rgb24(90, 90, 90) })
            {
                var (hue, saturation, value) = LightFormat.RgbToHsv(color);
                var back = LightFormat.HsvToRgb(hue, saturation, value);

                Assert.InRange(Math.Abs(back.R - color.R), 0, 4);
                Assert.InRange(Math.Abs(back.G - color.G), 0, 4);
                Assert.InRange(Math.Abs(back.B - color.B), 0, 4);
            }
        }

        [Fact]
        public void WarmWhite_HasMoreRedThanBlue_AndColdWhiteTheOther()
        {
            var warm = LightFormat.KelvinToRgb(1700);
            var neutral = LightFormat.KelvinToRgb(4000);
            var cold = LightFormat.KelvinToRgb(6500);

            Assert.Equal(255, warm.R);
            Assert.True(warm.B < 100, "a candle has hardly any blue in it");
            Assert.True(cold.R > 240 && cold.G > 240 && cold.B > 240, "6500 K is nearly white");
            Assert.True(neutral.B > warm.B && neutral.B < cold.B + 1);
            Assert.True(neutral.G > warm.G);
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(1_000_000)]
        public void AColourTemperatureOutOfTheRange_IsBroughtIntoIt(int kelvin)
        {
            var color = LightFormat.KelvinToRgb(kelvin);

            Assert.InRange(color.R, 0, 255);
        }

        [Fact]
        public void TheNumberOfTheProtocol_IsReadAndWrittenAsRedGreenBlue()
        {
            var color = Rgb24.FromValue(16747520);

            Assert.Equal(new Rgb24(255, 140, 0), color);
            Assert.Equal(16747520, color.Value);
            Assert.Equal("#FF8C00", color.Hex);
            Assert.Equal("#FF8C00 (16747520)", LightFormat.RgbText(16747520));
            Assert.Equal(new Rgb24(0, 0, 0), Rgb24.FromValue(0));
        }

        [Fact]
        public async Task TheSwatch_ShowsWhatTheLightShowsAccordingToItsColourMode()
        {
            await using var harness = await Harness.StartAsync();
            await harness.SettleAsync();
            var device = harness.Device;

            await harness.Bulb.ChangeAsync(("color_mode", "1"), ("rgb", "16747520"));
            await Wait.UntilAsync(() => device.ColorMode == ColorMode.Rgb && device.Rgb == 16747520, "the colour");
            Assert.Equal(new Rgb24(255, 140, 0), LightFormat.Swatch(device));

            await harness.Bulb.ChangeAsync(("color_mode", "2"), ("ct", "2700"));
            await Wait.UntilAsync(() => device.ColorMode == ColorMode.ColorTemperature && device.ColorTemperature == 2700, "the colour temperature");
            Assert.Equal(LightFormat.KelvinToRgb(2700), LightFormat.Swatch(device));

            await harness.Bulb.ChangeAsync(("color_mode", "3"), ("hue", "120"), ("sat", "100"));
            await Wait.UntilAsync(() => device.ColorMode == ColorMode.Hsv && device.Hue == 120 && device.Saturation == 100, "the hue");
            Assert.Equal(new Rgb24(0, 255, 0), LightFormat.Swatch(device));
        }

        [Theory]
        [InlineData(0, "just now")]
        [InlineData(1, "just now")]
        [InlineData(2, "2 s ago")]
        [InlineData(59, "59 s ago")]
        [InlineData(60, "1 min ago")]
        [InlineData(125, "2 min ago")]
        [InlineData(3600, "1 h ago")]
        [InlineData(7300, "2 h ago")]
        [InlineData(-30, "just now")]
        public void TheTimeSinceALightWasHeard_IsSaidTheWayAPersonSaysIt(int seconds, string expected)
        {
            var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

            Assert.Equal(expected, LightFormat.LastSeen(now.AddSeconds(-seconds), now));
        }

        [Fact]
        public void ALightThatWasHeardOverADayAgo_ShowsTheDate()
        {
            var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

            var text = LightFormat.LastSeen(now.AddDays(-3), now);

            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", text);
        }

        [Fact]
        public void TheTimer_IsWrittenInMinutes()
        {
            Assert.Equal("no timer", LightFormat.TimerText(TimeSpan.Zero));
            Assert.Equal("15 min left", LightFormat.TimerText(TimeSpan.FromMinutes(15)));
        }

        [Fact]
        public async Task TheDetailsOfADevice_HoldItsIdentityItsConnectionAndItsState()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Name = "Desk lamp", Model = "color", FirmwareVersion = "21" });
            var now = DateTimeOffset.Now;

            var rows = LightFormat.Properties(harness.Device, now).ToDictionary(row => row.Name, row => row.Value);

            Assert.Equal("Desk lamp", rows["Name"]);
            Assert.Equal("color", rows["Model"]);
            Assert.Equal("0x000000000015243f", rows["Id"]);
            Assert.Equal("21", rows["Firmware version"]);
            Assert.StartsWith("127.0.0.1:", rows["Address"], StringComparison.Ordinal);
            Assert.Equal("connected", rows["Connection"]);
            Assert.Equal("on", rows["Power"]);
            Assert.Equal("100%", rows["Brightness"]);
            Assert.Equal("Colour temperature", rows["Colour mode"]);
            Assert.Equal("4000 K", rows["Colour temperature"]);
            Assert.Equal("no timer", rows["Sleep timer"]);
            Assert.DoesNotContain("Background Power", rows.Keys);
        }

        [Fact]
        public async Task TheDetailsOfACeilingLight_AlsoHoldItsBackgroundLight()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true, Model = "ceiling" });
            await harness.SettleAsync();

            var rows = LightFormat.Properties(harness.Device, DateTimeOffset.Now).ToDictionary(row => row.Name, row => row.Value);

            Assert.Equal("off", rows["Background Power"]);
            Assert.Equal("50%", rows["Background Brightness"]);
            Assert.Equal("Colour temperature", rows["Background Colour mode"]);
        }

        [Fact]
        public async Task StateRows_ListTheStateOfOneLight_WithAPrefixForTheBackgroundLight()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true, Model = "ceiling" });
            await harness.SettleAsync();

            var main = LightFormat.StateRows(harness.Device).ToDictionary(row => row.Name, row => row.Value);
            var background = LightFormat.StateRows(harness.Device.Background, "Background ").ToDictionary(row => row.Name, row => row.Value);

            Assert.Equal(["Power", "Brightness", "Colour mode", "RGB", "Colour temperature", "Hue and saturation", "Flowing"], main.Keys);
            Assert.Equal("on", main["Power"]);
            Assert.Equal("100%", main["Brightness"]);
            Assert.Equal("Background Power", background.Keys.First());
            Assert.Equal("off", background["Background Power"]);
            Assert.Equal("50%", background["Background Brightness"]);
            Assert.All(background.Keys, name => Assert.StartsWith("Background ", name, StringComparison.Ordinal));
            Assert.Throws<ArgumentNullException>(() => LightFormat.StateRows(null!));
        }

        [Fact]
        public async Task ValueOf_KnowsEveryPropertyOfALightAndOfADevice()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            // The events of the library are named after the properties: when a property is added to the library, the Events page has to be
            // taught to show it. These are not events, or not a state: they are shown elsewhere.
            var notShown = new HashSet<string> { "Background", "SupportsColor", "SupportsColorTemperature", "SupportsBackgroundLight", "SupportsMusicMode", "Id", "LastSeen" };
            foreach (var property in typeof(YeelightDevice).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => !notShown.Contains(property.Name)))
            {
                Assert.NotEqual("(not shown)", LightFormat.ValueOf(harness.Device, property.Name));
            }

            foreach (var property in typeof(YeelightLight).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.NotEqual("(not shown)", LightFormat.ValueOf(harness.Device.Background, property.Name));
            }

            Assert.Equal("(not shown)", LightFormat.ValueOf(harness.Device, "NoSuchProperty"));
            Assert.Equal("(not shown)", LightFormat.ValueOf(harness.Device.Background, "IsConnected"));
        }

        [Fact]
        public async Task EveryEventOfAChangingLight_CanBeShownWithItsValue()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            await harness.SettleAsync();
            var seen = new ConcurrentDictionary<string, string>();
            harness.Client.DeviceStateChanged += (_, e) =>
            {
                var name = (e.Light is YeelightBackgroundLight ? "bg:" : string.Empty) + e.PropertyName;
                seen[name] = LightFormat.ValueOf(e.Light, e.PropertyName);
            };

            await harness.Bulb.ChangeAsync(
                ("power", "off"), ("bright", "42"), ("ct", "3500"), ("rgb", "255"), ("hue", "120"), ("sat", "50"), ("color_mode", "3"), ("flowing", "1"),
                ("flow_params", "1,0,1000,2,2700,100"), ("delayoff", "12"), ("music_on", "1"), ("name", "renamed"),
                ("bg_power", "on"), ("bg_bright", "11"), ("bg_ct", "2500"), ("bg_lmode", "1"), ("bg_rgb", "65280"), ("bg_hue", "10"), ("bg_sat", "20"),
                ("bg_flowing", "1"), ("bg_flow_params", "1,0,500,1,255,1"), ("nl_br", "20"), ("active_mode", "1"));

            await Wait.UntilAsync(() => seen.ContainsKey("bg:IsFlowing") && seen.ContainsKey("ActiveMode") && seen.ContainsKey("NightLightBrightness"), "the events");
            Assert.All(seen, pair => Assert.NotEqual("(not shown)", pair.Value));
            Assert.Equal("off", seen["IsOn"]);
            Assert.Equal("renamed", seen["Name"]);
            Assert.Equal("on", seen["bg:IsOn"]);
            Assert.Equal("11%", seen["bg:Brightness"]);
            Assert.Equal("12 min left", seen["SleepTimerRemaining"]);
        }

        [Fact]
        public void ColourModes_HaveWordsOfTheirOwn()
        {
            Assert.Equal("Colour (RGB)", LightFormat.ColorModeText(ColorMode.Rgb));
            Assert.Equal("Colour temperature", LightFormat.ColorModeText(ColorMode.ColorTemperature));
            Assert.Equal("Colour (HSV)", LightFormat.ColorModeText(ColorMode.Hsv));
        }
    }
}
