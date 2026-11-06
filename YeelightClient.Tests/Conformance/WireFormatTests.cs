namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What every typed command puts on the wire, against the Yeelight Inter-Operation Specification
    /// (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the methods and the parameters of table 4-1 and the example of
    /// every method. A simulated light receives the commands, and the test looks at the line that came in, down to the last character.
    /// </summary>
    public class WireFormatTests
    {
        public static TheoryData<string> LightCases => new(WireCases.Light.Select(wire => wire.Name));

        private static WireCase Find(string name) => WireCases.Light.Single(wire => wire.Name == name);

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheMainLight_SendsTheMethodAndTheParametersOfTheSpecification(string name)
        {
            var wire = Find(name);
            await using var h = await Wire.StartAsync();

            await wire.Invoke(h.Device);

            Wire.Sent(h.Bulb, wire.Method, wire.Parameters);
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheBackgroundLight_SendsTheSameCommands_WithTheMethodsOfTheBackgroundLight(string name)
        {
            var wire = Find(name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            await wire.Invoke(h.Device.Background);

            Wire.Sent(h.Bulb, "bg_" + wire.Method, wire.Parameters);
        }

        [Fact]
        public async Task TheMainLightAndTheBackgroundLight_ShareTheConnectionOfTheDevice_AndNeverTheNumberOfACommand()
        {
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            await h.Device.SetBrightnessAsync(10);
            await h.Device.Background.SetBrightnessAsync(20);
            await h.Device.ToggleAllAsync();
            await h.Device.Background.ToggleAsync();

            var requests = h.Bulb.Requests;
            Assert.Equal(["set_bright", "bg_set_bright", "dev_toggle", "bg_toggle"], requests.Select(request => request.Method).ToArray());
            Assert.Equal(1, h.Bulb.ConnectionCount);
            var ids = requests.Select(request => request.Id).ToArray();
            Assert.Equal(ids.Distinct().ToArray(), ids);
            Assert.Equal(ids.Order().ToArray(), ids);
            Assert.All(ids, id => Assert.True(id > 0, "The number of a command is a positive number."));
        }

        [Fact]
        public async Task TheNumberOfACommand_IsUniqueOnTheConnection_AndTheAnswerCarriesIt()
        {
            await using var h = await Wire.StartAsync();

            var results = new List<YeelightResult>();
            for (var index = 0; index < 25; index++)
            {
                results.Add(await h.Device.SendCommandAsync("get_prop", ["power"]));
            }

            var requests = h.Bulb.Requests;
            Assert.Equal(25, requests.Count);
            Assert.Equal(requests.Select(request => request.Id).ToArray(), results.Select(result => result.Id).ToArray());
            Assert.Equal(25, requests.Select(request => request.Id).Distinct().Count());
        }

        [Fact]
        public async Task GetPropertiesAsync_SendsTheNamesOfTheExample_AndGivesTheValuesByName()
        {
            await using var h = await Wire.StartAsync();

            var values = await h.Device.GetPropertiesAsync(["power", "not_exist", "bright"]);

            Wire.Sent(h.Bulb, "get_prop", """["power","not_exist","bright"]""");
            Assert.Equal(3, values.Count);
            Assert.Equal("on", values["power"]);
            Assert.Equal(string.Empty, values["not_exist"]);
            Assert.Equal("100", values["bright"]);
        }

        [Fact]
        public async Task GetPropertiesAsync_TakesOneName_AsTheSpecificationSaysOneToN()
        {
            await using var h = await Wire.StartAsync();

            var values = await h.Device.GetPropertiesAsync(["name"]);

            Wire.Sent(h.Bulb, "get_prop", """["name"]""");
            Assert.Equal("my_bulb", Assert.Single(values).Value);
        }

        [Fact]
        public async Task RefreshAsync_AsksForEveryPropertyOfTable42_InTheOrderOfTheTable()
        {
            await using var h = await Wire.StartAsync();

            await h.Device.RefreshAsync();

            var request = Assert.Single(h.Bulb.Requests);
            Assert.Equal("get_prop", request.Method);
            Assert.Equal(WireCases.Table42, request.Parameters.Select(parameter => parameter.GetString()!).ToArray());
            Assert.All(request.Parameters, parameter => Assert.Equal(JsonValueKind.String, parameter.ValueKind));
        }

        [Fact]
        public async Task ADeviceThatConnects_ReadsEveryPropertyOfTable42_WithOneCommand()
        {
            await using var h = await Wire.StartAsync(refreshOnConnect: true);

            await Wait.UntilAsync(() => h.Bulb.RequestsOf("get_prop").Count == 1, "the read of the properties after connecting");

            var request = Assert.Single(h.Bulb.Requests);
            Assert.Equal(WireCases.Table42, request.Parameters.Select(parameter => parameter.GetString()!).ToArray());
        }

        [Fact]
        public async Task SetNameAsync_SendsTheNameOfTheExample_AndTheLightKeepsIt()
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SetNameAsync("my_bulb");

            Wire.Sent(h.Bulb, "set_name", """["my_bulb"]""");
            Assert.Equal("my_bulb", h.Bulb.Name);
        }

        [Theory]
        [InlineData("Living room", """["Living room"]""")]
        [InlineData("say \"hi\"", """["say \"hi\""]""")]
        [InlineData("a\\b", """["a\\b"]""")]
        [InlineData("tab\there", """["tab\there"]""")]
        [InlineData("line\nbreak", """["line\nbreak"]""")]
        [InlineData("+<>&'", """["+<>&'"]""")]
        [InlineData("Café ☕ 日本語", """["Café ☕ 日本語"]""")]
        public async Task SetNameAsync_SendsTheNameAsJsonText_WithNothingEscapedThatDoesNotHaveToBe(string name, string parameters)
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SetNameAsync(name);

            var request = Wire.Sent(h.Bulb, "set_name", parameters);
            Assert.Equal(name, request.Parameters[0].GetString());
        }

        [Fact]
        public async Task SetNameAsync_ANameWithACharacterOutsideTheBasicPlane_ArrivesAsItWas()
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SetNameAsync("Lamp 🌟");

            var request = Assert.Single(h.Bulb.Requests);
            Assert.Equal("Lamp 🌟", request.Parameters[0].GetString());
            Assert.Equal("Lamp 🌟", h.Bulb.Name);
        }

        [Theory]
        [InlineData("a", 64)]
        [InlineData("€", 21)]
        [InlineData("🌟", 16)]
        public async Task SetNameAsync_ANameOf64BytesInUtf8_IsSent_WhateverItsCharacters(string character, int count)
        {
            var name = string.Concat(Enumerable.Repeat(character, count));
            if (character == "€")
            {
                name += "a";
            }

            Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(name));
            await using var h = await Wire.StartAsync();

            await h.Device.SetNameAsync(name);

            Assert.Equal(name, Assert.Single(h.Bulb.Requests).Parameters[0].GetString());
        }

        [Fact]
        public async Task ToggleAllAsync_SendsDevToggle_WithoutParameters()
        {
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            await h.Device.ToggleAllAsync();

            Wire.Sent(h.Bulb, "dev_toggle", "[]");
        }

        [Theory]
        [InlineData(900_000, 15)]
        [InlineData(1_200_000, 20)]
        [InlineData(60_000, 1)]
        [InlineData(60_001, 2)]
        [InlineData(90_000, 2)]
        [InlineData(120_000, 2)]
        [InlineData(120_001, 3)]
        [InlineData(3_600_000, 60)]
        public async Task SetSleepTimerAsync_SendsTheMinutes_RoundedUp(int milliseconds, int minutes)
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SetSleepTimerAsync(TimeSpan.FromMilliseconds(milliseconds));

            Wire.Sent(h.Bulb, "cron_add", $"[0,{minutes}]");
            Assert.Equal(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture), h.Bulb.Get("delayoff"));
        }

        [Fact]
        public async Task GetSleepTimerAsync_AsksForTheTimerOfType0_AndReadsTheDelayOfTheAnswer()
        {
            await using var h = await Wire.StartAsync();
            await h.Device.SetSleepTimerAsync(TimeSpan.FromMinutes(15));
            Assert.Single(h.Bulb.Requests);

            var timer = await h.Device.GetSleepTimerAsync();

            Assert.Equal(TimeSpan.FromMinutes(15), timer);
            Wire.Matches(h.Bulb.RequestsOf("cron_get").Single(), "cron_get", "[0]");
        }

        [Fact]
        public async Task GetSleepTimerAsync_ALightWithoutATimer_AnswersWithAnEmptyList_ThatIsNoTimer()
        {
            await using var h = await Wire.StartAsync();

            var timer = await h.Device.GetSleepTimerAsync();

            Assert.Null(timer);
            Wire.Sent(h.Bulb, "cron_get", "[0]");
        }

        [Fact]
        public async Task CancelSleepTimerAsync_SendsCronDelForTheTimerOfType0()
        {
            await using var h = await Wire.StartAsync();
            await h.Device.SetSleepTimerAsync(TimeSpan.FromMinutes(5));

            await h.Device.CancelSleepTimerAsync();

            Wire.Matches(h.Bulb.RequestsOf("cron_del").Single(), "cron_del", "[0]");
            Assert.Equal("0", h.Bulb.Get("delayoff"));
            Assert.Null(await h.Device.GetSleepTimerAsync());
        }

        [Fact]
        public async Task SendCommandAsync_SendsTheMethodAsGiven_WithoutThePrefixOfTheBackgroundLight()
        {
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            var result = await h.Device.Background.SendCommandAsync("set_power", ["on", "smooth", 500]);

            var request = Wire.Sent(h.Bulb, "set_power", """["on","smooth",500]""");
            Assert.True(result.Success);
            Assert.Equal(request.Id, result.Id);
            Assert.Equal("ok", result.GetString());
        }

        [Fact]
        public async Task SendCommandAsync_SendsAMethodThatTheLightDoesNotList_AndGivesTheRefusalAsTheResult()
        {
            await using var h = await Wire.StartAsync();

            var result = await h.Device.SendCommandAsync("no_such_method", [1, "two"]);

            Wire.Sent(h.Bulb, "no_such_method", """[1,"two"]""");
            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.DeviceRejected, result.Error!.Kind);
            Assert.Equal(-1, result.Error.Code);
            Assert.Equal("unsupported method", result.Error.Message);
            Assert.Throws<YeelightException>(() => result.EnsureSuccess("no_such_method"));
        }

        [Fact]
        public async Task SendCommandAsync_ACommandThatTheLightRefuses_DoesNotThrow()
        {
            await using var h = await Wire.StartAsync();
            await h.Device.TurnOffAsync();
            h.Bulb.RequireOnForChanges = true;

            var result = await h.Device.SendCommandAsync("set_bright", [50, "sudden", 0]);

            Assert.False(result.Success);
            Assert.Equal("general error", result.Error!.Message);
            Assert.Equal(["set_power", "set_bright"], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Fact]
        public async Task SendCommandAsync_TakesNumbersTextAndTruthValues_AndNull()
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SendCommandAsync("x", [1, -2, 3000000000L, 3.5, "text", true, false, null]);

            Wire.Sent(h.Bulb, "x", """[1,-2,3000000000,3.5,"text",true,false,null]""");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        public async Task SendCommandAsync_WithoutParameters_SendsAnEmptyList(int? count)
        {
            await using var h = await Wire.StartAsync();

            await h.Device.SendCommandAsync("toggle", count is null ? null : new object?[count.Value]);

            Wire.Sent(h.Bulb, "toggle", "[]");
        }

        [Fact]
        public async Task SendCommandAsync_GivesTheValuesOfTheAnswerAsTheyCame()
        {
            await using var h = await Wire.StartAsync();

            var result = await h.Device.SendCommandAsync("get_prop", ["power", "bright", "not_exist"]);

            Assert.True(result.Success);
            Assert.Equal(["on", "100", string.Empty], result.Values.Select(value => value.GetString()!).ToArray());
            Assert.Equal("on", result.GetString());
            Assert.Equal("100", result.GetString(1));
            Assert.Null(result.GetString(3));
        }

        [Fact]
        public async Task AnArgumentThatIsNotNumberTextOrTruthValue_IsRefused_AndNothingIsSent()
        {
            await using var h = await Wire.StartAsync();

            await Wire.RefusedAsync(
                h.Bulb,
                typeof(ArgumentException),
                "value",
                () => h.Device.SendCommandAsync("toggle", [new object()]),
                () => h.Device.SetDefaultAsync(),
                "set_default");
        }
    }
}
