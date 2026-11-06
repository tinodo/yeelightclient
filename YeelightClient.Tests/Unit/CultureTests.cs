namespace Yeelight.Tests.Unit
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What goes over the wire, and what comes in from it, does not depend on the culture of the computer: a negative number is written with
    /// a hyphen and not with the minus sign of the language (Swedish has U+2212), a decimal point is a point and not a comma, an uppercase
    /// letter is the one of the protocol and not the one of the language (the Turkish dotted I), and the digits are ASCII.
    /// </summary>
    public class CultureTests
    {
        public static TheoryData<string> Cultures => new("sv-SE", "tr-TR", "ar-SA", "fa-IR", "de-DE", "fr-FR", "ru-RU", "ja-JP", "th-TH", "he-IL", "hi-IN", "nb-NO", "en-US");

        private static void Under(string name, Action test)
        {
            CultureInfo culture;
            try
            {
                culture = new CultureInfo(name, useUserOverride: false);
            }
            catch (CultureNotFoundException)
            {
                // A computer that does without the data of the languages (invariant globalization) has nothing to try.
                return;
            }

            var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                test();
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
            }
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void ACommand_IsWrittenTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                var line = Encoding.UTF8.GetString(YeelightProtocol.BuildRequest(-7, "x", [-20, 1.5, -2.5, 1000000, 0.5f, 12.75m, -3L, "né", true, null]));

                Assert.Equal("{\"id\":-7,\"method\":\"x\",\"params\":[-20,1.5,-2.5,1000000,0.5,12.75,-3,\"né\",true,null]}\r\n", line);
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void ATypedCommand_IsWrittenTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                var scene = YeelightScene.Flow(new ColorFlow([ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(500)), ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMilliseconds(1000), 40)], count: 4, action: ColorFlowAction.TurnOff));

                var parameters = scene.GetParameters();

                Assert.Equal(["cf", 4, 2, "500,1,16711680,-1,1000,2,2700,40"], parameters);
                Assert.Equal("500,1,16711680,-1", ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(500)).ToExpression());
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void AFlowThatTheLightReports_IsReadTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                Assert.True(ColorFlow.TryParse("4,2,500,1,16711680,-1,1000,2,2700,40", out var flow));

                Assert.Equal(4, flow!.Count);
                Assert.Equal(ColorFlowAction.TurnOff, flow.Action);
                Assert.Equal([null, 40], flow.Steps.Select(step => step.Brightness).ToArray());
                Assert.Equal("4,2,500,1,16711680,-1,1000,2,2700,40", flow.ToString());
                Assert.False(ColorFlow.TryParse("4,2,500,1,16711680,\u22121,1000,2,2700,40", out _), "A minus sign of a language is not the minus sign of the protocol.");
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void TheStateOfALight_IsReadTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                using var client = UnitDevices.Client();
                var device = UnitDevices.Add(client);

                device.ApplyProperties(
                [
                    new("power", "on"),
                    new("bright", "42"),
                    new("ct", "4000"),
                    new("rgb", "16711680"),
                    new("hue", "300"),
                    new("sat", "70"),
                    new("color_mode", "3"),
                    new("delayoff", "15"),
                    new("nl_br", "20"),
                    new("active_mode", "1"),
                    new("name", "Küche"),
                ]);

                Assert.True(device.IsOn);
                Assert.Equal(42, device.Brightness);
                Assert.Equal(4000, device.ColorTemperature);
                Assert.Equal(16711680, device.Rgb);
                Assert.Equal(300, device.Hue);
                Assert.Equal(70, device.Saturation);
                Assert.Equal(ColorMode.Hsv, device.ColorMode);
                Assert.Equal(TimeSpan.FromMinutes(15), device.SleepTimerRemaining);
                Assert.Equal(20, device.NightLightBrightness);
                Assert.Equal(ActiveMode.Moonlight, device.ActiveMode);
                Assert.Equal("Küche", device.Name);
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void AnAnswerOfTheLight_IsReadTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                Assert.True(YeelightProtocol.TryParse("{\"id\":\"12\",\"result\":[\"on\",\"-5\",100,\" 7 \"]}", out var message));

                var result = Assert.IsType<YeelightResponse>(message).Result;
                Assert.Equal(12, result.Id);
                Assert.Equal(["on", "-5", "100", " 7 "], result.Values.Select(value => YeelightProtocol.ToText(value)!).ToArray());
                Assert.True(YeelightProtocol.TryGetInt32(result.Values[1], out var minusFive));
                Assert.Equal(-5, minusFive);
                Assert.True(YeelightProtocol.TryGetInt32(result.Values[3], out var seven));
                Assert.Equal(7, seven);
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void AnAnnouncement_IsReadTheSameInEveryCulture(string culture)
        {
            Under(culture, () =>
            {
                var announcement = UnitMessages.Parse(UnitMessages.Response(location: "YEELIGHT://192.168.1.239:55443/", id: "0xABCDEF0123456789"));

                Assert.Equal("192.168.1.239", announcement.Host);
                Assert.Equal(55443, announcement.Port);
                Assert.Equal("YEELIGHT://192.168.1.239:55443/", announcement.Location);
                Assert.Equal("0xABCDEF0123456789", announcement.Id);
                Assert.Equal("on", announcement.Properties["power"]);
                Assert.Equal("100", announcement.Properties["bright"]);
                Assert.True(DeviceAnnouncement.TryParse(UnitMessages.Advertisement().Replace("NTS: ssdp:alive", "nts: SSDP:ALIVE", StringComparison.Ordinal), out _), "The names of the headers and the kind of notice do not depend on the case, in any language.");
            });
        }

        [Theory]
        [MemberData(nameof(Cultures))]
        public void TheIdOfALight_IsFoundInEveryCulture_WhateverItsCase(string culture)
        {
            Under(culture, () =>
            {
                using var client = UnitDevices.Client();
                var device = UnitDevices.Add(client, UnitMessages.Response(id: "0x00000000000ABCdef"));

                Assert.Same(device, client.FindDevice("0X00000000000abcDEF"));
                Assert.Same(device, client.Apply(UnitMessages.Parse(UnitMessages.Response(id: "0x00000000000abcdef", name: "again"))));
                Assert.Single(client.Devices);
            });
        }

        [Fact]
        public void TheMessagesForPeople_DoNotThrowInAnyCulture_AndAreNotEmpty()
        {
            foreach (var name in new[] { "sv-SE", "tr-TR", "ar-SA", "th-TH" })
            {
                Under(name, () =>
                {
                    Assert.NotEmpty(ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(500), 40).ToString());
                    Assert.NotEmpty(YeelightScene.Hsv(300, 70, 100).ToString());
                    Assert.NotEmpty(new YeelightException(YeelightError.Create(YeelightFailureKind.Timeout, "No answer."), "toggle").Message);
                    Assert.NotEmpty(YeelightResult.Failed(YeelightFailureKind.NotConnected, "Gone.").ToString());
                });
            }
        }
    }
}
