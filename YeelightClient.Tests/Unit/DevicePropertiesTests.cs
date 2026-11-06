namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What a device does with the properties that it is told (in an announcement, in <c>props</c>, in the answer to <c>get_prop</c>):
    /// the ones of its main light, and those that concern the device as a whole (<c>delayoff</c>, <c>music_on</c>, <c>nl_br</c>,
    /// <c>active_mode</c> and <c>name</c>). A value that is not one is ignored, and a value that is the same as before is no news.
    /// </summary>
    public class DevicePropertiesTests
    {
        private static void Apply(YeelightDevice device, params (string Key, string Value)[] values)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var (key, value) in values)
            {
                list.Add(KeyValuePair.Create(key, value));
            }

            device.ApplyProperties(list);
        }

        private static YeelightDevice Device(YeelightClient client)
        {
            return UnitDevices.Add(client, UnitMessages.Response(name: "start"));
        }

        [Fact]
        public void TheStateInTheAnnouncement_IsTheStateOfTheDevice()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client);

            Assert.True(device.IsOn);
            Assert.Equal(100, device.Brightness);
            Assert.Equal(ColorMode.ColorTemperature, device.ColorMode);
            Assert.Equal(4000, device.ColorTemperature);
            Assert.Equal(16711680, device.Rgb);
            Assert.Equal(100, device.Hue);
            Assert.Equal(35, device.Saturation);
            Assert.Equal("my_bulb", device.Name);
            Assert.False(device.IsFlowing);
            Assert.Equal(string.Empty, device.FlowParameters);
        }

        [Fact]
        public void ADeviceThatHasToldNothingAboutItsOwnProperties_HasTheDefaults()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client);

            Assert.Equal(TimeSpan.Zero, device.SleepTimerRemaining);
            Assert.False(device.IsMusicModeOn);
            Assert.Equal(0, device.NightLightBrightness);
            Assert.Null(device.ActiveMode);
            Assert.False(device.IsConnected);
        }

        [Theory]
        [InlineData("15", 15)]
        [InlineData("1", 1)]
        [InlineData("60", 60)]
        [InlineData("0", 0)]
        [InlineData("1073741823", 1_073_741_823)]
        public void DelayOff_IsTheRemainingMinutesOfTheSleepTimer(string value, int minutes)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("delayoff", "7"));

            Apply(device, ("delayoff", value));

            Assert.Equal(TimeSpan.FromMinutes(minutes), device.SleepTimerRemaining);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("1073741824")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1.5")]
        [InlineData("99999999999")]
        public void DelayOff_OfAnythingElse_IsIgnored(string value)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("delayoff", "7"));

            Apply(device, ("delayoff", value));

            Assert.Equal(TimeSpan.FromMinutes(7), device.SleepTimerRemaining);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void MusicOn_IsOneOrZero(string value, bool expected)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("music_on", expected ? "0" : "1"));

            Apply(device, ("music_on", value));

            Assert.Equal(expected, device.IsMusicModeOn);
        }

        [Theory]
        [InlineData("2")]
        [InlineData("-1")]
        [InlineData("on")]
        [InlineData("true")]
        [InlineData("")]
        [InlineData("01")]
        public void MusicOn_OfAnythingElse_IsIgnored(string value)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("music_on", "1"));

            Apply(device, ("music_on", value));

            Assert.True(device.IsMusicModeOn);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("50", 50)]
        [InlineData("100", 100)]
        public void NightLightBrightness_IsAPercentage(string value, int expected)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("nl_br", "30"));

            Apply(device, ("nl_br", value));

            Assert.Equal(expected, device.NightLightBrightness);
        }

        [Theory]
        [InlineData("101")]
        [InlineData("-1")]
        [InlineData("dim")]
        [InlineData("")]
        public void NightLightBrightness_OfAnythingElse_IsIgnored(string value)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("nl_br", "30"));

            Apply(device, ("nl_br", value));

            Assert.Equal(30, device.NightLightBrightness);
        }

        [Theory]
        [InlineData("0", ActiveMode.Daylight)]
        [InlineData("1", ActiveMode.Moonlight)]
        public void ActiveMode_IsDaylightOrMoonlight(string value, ActiveMode expected)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("active_mode", value));

            Assert.Equal(expected, device.ActiveMode);
        }

        [Theory]
        [InlineData("2")]
        [InlineData("-1")]
        [InlineData("night")]
        [InlineData("")]
        public void ActiveMode_OfAnythingElse_IsIgnored(string value)
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("active_mode", value));
            Assert.Null(device.ActiveMode);

            Apply(device, ("active_mode", "1"));
            Apply(device, ("active_mode", value));
            Assert.Equal(ActiveMode.Moonlight, device.ActiveMode);
        }

        [Fact]
        public void Name_IsTheNameThatTheDeviceReports()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("name", "Living room"));

            Assert.Equal("Living room", device.Name);
        }

        [Fact]
        public void Name_CanBeEmpty()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("name", string.Empty));

            Assert.Equal(string.Empty, device.Name);
        }

        [Fact]
        public void Name_OfMoreThan256Characters_IsCutOff()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("name", new string('n', 300)));

            Assert.Equal(new string('n', 256), device.Name);
        }

        [Fact]
        public void Name_ThatIsCutOff_DoesNotEndInHalfOfASurrogatePair()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("name", new string('x', 255) + "\U0001F4A1"));

            Assert.False(char.IsHighSurrogate(device.Name[^1]), "The name ends in half of a surrogate pair.");
        }

        [Fact]
        public void APropertyThatNoLightHas_IsIgnored()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("foo", "1"), ("", "1"), ("Power", "off"), ("bg_foo", "1"));

            Assert.True(device.IsOn);
            Assert.Equal("start", device.Name);
        }

        [Fact]
        public void TheDeviceAndItsMainLight_AreTheSameObject_ForEveryPropertyOfTheMainLight()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);

            Apply(device, ("power", "off"), ("bright", "5"), ("ct", "6000"), ("rgb", "123"), ("hue", "9"), ("sat", "8"), ("color_mode", "3"), ("flowing", "1"), ("flow_params", "f"));

            Assert.False(device.IsOn);
            Assert.Equal(5, device.Brightness);
            Assert.Equal(6000, device.ColorTemperature);
            Assert.Equal(123, device.Rgb);
            Assert.Equal(9, device.Hue);
            Assert.Equal(8, device.Saturation);
            Assert.Equal(ColorMode.Hsv, device.ColorMode);
            Assert.True(device.IsFlowing);
            Assert.Equal("f", device.FlowParameters);
        }

        [Fact]
        public async Task EveryChange_IsReportedWithTheNameOfItsProperty_InTheOrderInWhichItHappened()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            var log = new PropertyLog(device);

            Apply(
                device,
                ("power", "off"),
                ("bright", "5"),
                ("ct", "6000"),
                ("hue", "9"),
                ("sat", "8"),
                ("color_mode", "3"),
                ("flowing", "1"),
                ("flow_params", "f"),
                ("delayoff", "10"),
                ("music_on", "1"),
                ("nl_br", "20"),
                ("active_mode", "1"),
                ("name", "other"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(
                ["IsOn", "Brightness", "ColorTemperature", "Hue", "Saturation", "ColorMode", "IsFlowing", "FlowParameters", "SleepTimerRemaining", "IsMusicModeOn", "NightLightBrightness", "ActiveMode", "Name"],
                log.Names);
        }

        [Fact]
        public async Task ANewColor_IsReportedAsRgbAndAsColor()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            var log = new PropertyLog(device);

            Apply(device, ("rgb", "255"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["Rgb", "Color"], log.Names);
            Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 0, 255), device.Color);
        }

        [Fact]
        public async Task TheSameValueAgain_IsNoNews()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            Apply(device, ("bright", "5"), ("delayoff", "10"), ("music_on", "1"), ("nl_br", "20"), ("active_mode", "1"), ("name", "other"));
            await UnitDevices.FlushAsync(device);
            var log = new PropertyLog(device);

            Apply(device, ("bright", "5"), ("delayoff", "10"), ("music_on", "1"), ("nl_br", "20"), ("active_mode", "1"), ("name", "other"), ("power", "on"));
            await UnitDevices.FlushAsync(device);

            Assert.Empty(log.Names);
        }

        [Fact]
        public async Task AValueThatIsNotOne_IsNoNews()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            var log = new PropertyLog(device);

            Apply(device, ("bright", "500"), ("delayoff", "-3"), ("music_on", "2"), ("nl_br", "x"), ("active_mode", "7"), ("power", "maybe"), ("foo", "bar"));
            await UnitDevices.FlushAsync(device);

            Assert.Empty(log.Names);
        }

        [Fact]
        public async Task TheClient_IsToldOfEveryChangeToo_WithTheDeviceTheLightAndTheName()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            var seen = new List<(YeelightDevice Device, YeelightLight Light, string Name)>();
            client.DeviceStateChanged += (_, args) =>
            {
                lock (seen)
                {
                    seen.Add((args.Device, args.Light, args.PropertyName));
                }
            };

            Apply(device, ("bright", "5"), ("delayoff", "10"), ("name", "other"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["Brightness", "SleepTimerRemaining", "Name"], seen.Select(item => item.Name));
            Assert.All(seen, item =>
            {
                Assert.Same(device, item.Device);
                Assert.Same(device, item.Light);
            });
        }

        [Fact]
        public async Task ChangesThatAreMadeWhileTheDeviceIsMuted_AreNotReported()
        {
            using var client = UnitDevices.Client();
            var device = Device(client);
            var log = new PropertyLog(device);

            device.Muted = true;
            Apply(device, ("bright", "5"), ("name", "other"));
            device.Muted = false;
            Apply(device, ("bright", "6"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(6, device.Brightness);
            Assert.Equal(["Brightness"], log.Names);
            Assert.Equal("other", device.Name);
        }

        [Fact]
        public async Task AHandlerThatThrows_DoesNotKeepTheOthersFromBeingCalled_OrLaterEventsFromComing()
        {
            var logger = new ListLogger();
            using var client = UnitDevices.Client(logger: logger);
            var device = Device(client);
            var second = new PropertyLog(device);
            device.PropertyChanged += (_, _) => throw new InvalidOperationException("The handler of the application is broken.");
            var third = new PropertyLog(device);

            Apply(device, ("bright", "5"));
            await UnitDevices.FlushAsync(device);
            Apply(device, ("bright", "6"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["Brightness", "Brightness"], second.Names);
            Assert.Equal(["Brightness", "Brightness"], third.Names);
            Assert.Contains(logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Exception is InvalidOperationException);
        }
    }
}
