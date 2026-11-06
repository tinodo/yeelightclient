namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The background light of a device (bg_power, bg_bright, ..., bg_lmode in table 4-2 of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the same state as the main light, under other names (and
    /// with <c>bg_lmode</c> where the main light has <c>color_mode</c>).
    /// </summary>
    public class BackgroundLightStateTests
    {
        [Fact]
        public void ABackgroundLight_BelongsToItsDevice_AndIsAlwaysThere()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            Assert.NotNull(device.Background);
            Assert.Same(device.Background, device.Background);
            Assert.Same(device, device.Background.Owner);
            Assert.Same(device, device.Owner);
        }

        [Theory]
        [InlineData("bg_power", "on", "IsOn")]
        [InlineData("bg_bright", "55", "Brightness")]
        [InlineData("bg_ct", "3200", "ColorTemperature")]
        [InlineData("bg_rgb", "255", "Rgb")]
        [InlineData("bg_hue", "240", "Hue")]
        [InlineData("bg_sat", "80", "Saturation")]
        [InlineData("bg_lmode", "3", "ColorMode")]
        [InlineData("bg_flowing", "1", "IsFlowing")]
        [InlineData("bg_flow_params", "0,0,1000,2,2700,100", "FlowParameters")]
        public void EveryPropertyOfTheBackgroundLight_IsTakenByTheBackgroundLight(string key, string value, string property)
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response());
            var background = device.Background;
            var before = Snapshot(device);

            var handled = background.ApplyBackgroundProperty(key, value);

            Assert.True(handled);
            Assert.NotEqual(Snapshot(background)[property], Default(property));
            Assert.Equal(before, Snapshot(device));
        }

        [Fact]
        public void TheModeOfTheBackgroundLight_IsBgLmode_AndItIsTheColorMode()
        {
            using var client = UnitDevices.Client();
            var background = UnitDevices.Add(client).Background;

            Assert.True(background.ApplyBackgroundProperty("bg_lmode", "2"));
            Assert.Equal(ColorMode.ColorTemperature, background.ColorMode);
            Assert.True(background.ApplyBackgroundProperty("bg_lmode", "3"));
            Assert.Equal(ColorMode.Hsv, background.ColorMode);
            Assert.True(background.ApplyBackgroundProperty("bg_lmode", "1"));
            Assert.Equal(ColorMode.Rgb, background.ColorMode);
        }

        [Fact]
        public void TheValuesOfTheBackgroundLight_AreWhatTheDeviceReported()
        {
            using var client = UnitDevices.Client();
            var background = UnitDevices.Add(client).Background;

            background.ApplyBackgroundProperty("bg_power", "on");
            background.ApplyBackgroundProperty("bg_bright", "55");
            background.ApplyBackgroundProperty("bg_ct", "3200");
            background.ApplyBackgroundProperty("bg_rgb", "16711680");
            background.ApplyBackgroundProperty("bg_hue", "240");
            background.ApplyBackgroundProperty("bg_sat", "80");
            background.ApplyBackgroundProperty("bg_flowing", "1");
            background.ApplyBackgroundProperty("bg_flow_params", "0,0,1000,2,2700,100");

            Assert.True(background.IsOn);
            Assert.Equal(55, background.Brightness);
            Assert.Equal(3200, background.ColorTemperature);
            Assert.Equal(16711680, background.Rgb);
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), background.Color);
            Assert.Equal(240, background.Hue);
            Assert.Equal(80, background.Saturation);
            Assert.True(background.IsFlowing);
            Assert.Equal("0,0,1000,2,2700,100", background.FlowParameters);
        }

        [Theory]
        [InlineData("power", "on")]
        [InlineData("bright", "50")]
        [InlineData("color_mode", "2")]
        [InlineData("name", "x")]
        [InlineData("foo", "1")]
        [InlineData("", "1")]
        [InlineData("BG_power", "on")]
        [InlineData("xbg_power", "on")]
        public void APropertyWithoutThePrefixOfTheBackgroundLight_IsNotItsOwn(string key, string value)
        {
            using var client = UnitDevices.Client();
            var background = UnitDevices.Add(client).Background;
            var before = Snapshot(background);

            Assert.False(background.ApplyBackgroundProperty(key, value));
            Assert.Equal(before, Snapshot(background));
        }

        [Theory]
        [InlineData("bg_foo")]
        [InlineData("bg_")]
        [InlineData("bg_delayoff")]
        [InlineData("bg_music_on")]
        [InlineData("bg_name")]
        [InlineData("bg_nl_br")]
        [InlineData("bg_active_mode")]
        public void APrefixedNameThatIsNoPropertyOfALight_IsNotHandled(string key)
        {
            using var client = UnitDevices.Client();
            var background = UnitDevices.Add(client).Background;
            var before = Snapshot(background);

            Assert.False(background.ApplyBackgroundProperty(key, "1"));
            Assert.Equal(before, Snapshot(background));
        }

        [Theory]
        [InlineData("bg_power", "ON")]
        [InlineData("bg_bright", "101")]
        [InlineData("bg_bright", "abc")]
        [InlineData("bg_ct", "-1")]
        [InlineData("bg_rgb", "16777216")]
        [InlineData("bg_hue", "360")]
        [InlineData("bg_sat", "101")]
        [InlineData("bg_lmode", "4")]
        [InlineData("bg_lmode", "0")]
        [InlineData("bg_flowing", "2")]
        public void AValueThatIsNotOne_IsIgnored_ButTheNameIsStillTheirs(string key, string value)
        {
            using var client = UnitDevices.Client();
            var background = UnitDevices.Add(client).Background;
            var before = Snapshot(background);

            Assert.True(background.ApplyBackgroundProperty(key, value));
            Assert.Equal(before, Snapshot(background));
        }

        [Fact]
        public void TheDevice_SendsThePropertiesOfTheBackgroundLightToIt_AndTheOthersToItself()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            device.ApplyProperties(
            [
                KeyValuePair.Create("power", "on"),
                KeyValuePair.Create("bright", "70"),
                KeyValuePair.Create("bg_power", "off"),
                KeyValuePair.Create("bg_bright", "20"),
                KeyValuePair.Create("bg_lmode", "3"),
                KeyValuePair.Create("color_mode", "2"),
            ]);

            Assert.True(device.IsOn);
            Assert.Equal(70, device.Brightness);
            Assert.Equal(ColorMode.ColorTemperature, device.ColorMode);
            Assert.False(device.Background.IsOn);
            Assert.Equal(20, device.Background.Brightness);
            Assert.Equal(ColorMode.Hsv, device.Background.ColorMode);
        }

        [Fact]
        public async Task AChangeOfTheBackgroundLight_RaisesItsOwnPropertyChanged_AndTheEventOfTheClientNamesIt()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            var main = new PropertyLog(device);
            var background = new PropertyLog(device.Background);
            var seen = new List<(YeelightDevice Device, YeelightLight Light, string Name)>();
            client.DeviceStateChanged += (_, args) =>
            {
                lock (seen)
                {
                    seen.Add((args.Device, args.Light, args.PropertyName));
                }
            };

            device.ApplyProperties([KeyValuePair.Create("bg_power", "on"), KeyValuePair.Create("bg_rgb", "255"), KeyValuePair.Create("power", "off")]);
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["IsOn"], main.Names);
            Assert.Equal(["IsOn", "Rgb", "Color"], background.Names);
            Assert.Equal(
                [(device, (YeelightLight)device.Background, "IsOn"), (device, device.Background, "Rgb"), (device, device.Background, "Color"), (device, device, "IsOn")],
                seen.ToArray());
        }

        [Fact]
        public async Task WhatTheAnnouncementSaysAboutTheBackgroundLight_IsTheStartAndNoNews()
        {
            using var client = UnitDevices.Client();
            var message = UnitMessages.Response().Replace("name: my_bulb", "name: my_bulb\r\nbg_power: on\r\nbg_bright: 33", StringComparison.Ordinal);

            var device = UnitDevices.Add(client, message);
            var background = new PropertyLog(device.Background);
            await UnitDevices.FlushAsync(device);

            Assert.True(device.Background.IsOn);
            Assert.Equal(33, device.Background.Brightness);
            Assert.Empty(background.Names);
        }

        private static Dictionary<string, object> Snapshot(YeelightLight light)
        {
            return new Dictionary<string, object>
            {
                ["IsOn"] = light.IsOn,
                ["Brightness"] = light.Brightness,
                ["ColorTemperature"] = light.ColorTemperature,
                ["Rgb"] = light.Rgb,
                ["Hue"] = light.Hue,
                ["Saturation"] = light.Saturation,
                ["ColorMode"] = light.ColorMode,
                ["IsFlowing"] = light.IsFlowing,
                ["FlowParameters"] = light.FlowParameters,
            };
        }

        private static object Default(string property)
        {
            return property switch
            {
                "IsOn" => false,
                "ColorMode" => ColorMode.Rgb,
                "FlowParameters" => string.Empty,
                "IsFlowing" => false,
                _ => 0,
            };
        }
    }
}
