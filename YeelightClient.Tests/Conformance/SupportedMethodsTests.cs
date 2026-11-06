namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// "Any control request that invokes method that is not included in this field will be rejected by smart LED" (section 3.1 of
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): a light says which methods it takes in the <c>support</c> header
    /// of its announcement, and a command whose method is not among them is not sent, and throws <see cref="NotSupportedException"/>. A light
    /// that has not said what it takes is sent everything.
    /// </summary>
    public class SupportedMethodsTests
    {
        private static readonly string[] MainMethods = SimulatedBulbOptions.AllMethods.Split(' ');

        private static readonly string[] EveryMethod = [.. MainMethods, .. SimulatedBulbOptions.BackgroundMethods.Split(' ')];

        private static readonly (string Name, string Method, Func<YeelightDevice, Task> Invoke)[] DeviceCases =
        [
            ("GetPropertiesAsync", "get_prop", device => device.GetPropertiesAsync(["power"])),
            ("RefreshAsync", "get_prop", device => device.RefreshAsync()),
            ("SetNameAsync", "set_name", device => device.SetNameAsync("name")),
            ("ToggleAllAsync", "dev_toggle", device => device.ToggleAllAsync()),
            ("SetSleepTimerAsync", "cron_add", device => device.SetSleepTimerAsync(TimeSpan.FromMinutes(5))),
            ("GetSleepTimerAsync", "cron_get", device => device.GetSleepTimerAsync()),
            ("CancelSleepTimerAsync", "cron_del", device => device.CancelSleepTimerAsync()),
            ("StartMusicModeAsync", "set_music", device => device.StartMusicModeAsync()),
        ];

        public static TheoryData<string> LightCases => new(WireCases.Light.Select(wire => wire.Name));

        public static TheoryData<string> DeviceNames => new(DeviceCases.Select(device => device.Name));

        private static string Without(string[] methods, string method) => string.Join(' ', methods.Where(candidate => candidate != method));

        /// <summary>A command that is not the one that a test is about, to see that nothing came before it (<c>SendCommandAsync</c> does not look at the list).</summary>
        private static string AnotherMethod(string method) => method == "toggle" ? "set_default" : "toggle";

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheMainLight_RefusesACommandWhoseMethodTheLightDoesNotList_AndSendsNothing(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = Without(EveryMethod, wire.Method) });
            var next = AnotherMethod(wire.Method);

            var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => wire.Invoke(h.Device));

            Assert.Contains($"'{wire.Method}'", refusal.Message);
            await h.Device.SendCommandAsync(next);
            Assert.Equal([next], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheMainLight_NeedsItsOwnMethodInTheList_AndNoOther(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = wire.Method });
            Assert.Equal([wire.Method], h.Device.SupportedMethods);

            await wire.Invoke(h.Device);

            Wire.Sent(h.Bulb, wire.Method, wire.Parameters);
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheBackgroundLight_RefusesACommandWhoseBackgroundMethodTheLightDoesNotList_AndSendsNothing(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            var method = "bg_" + wire.Method;
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = Without(EveryMethod, method) });
            var next = AnotherMethod(method);

            var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => wire.Invoke(h.Device.Background));

            Assert.Contains($"'{method}'", refusal.Message);
            await h.Device.SendCommandAsync(next);
            Assert.Equal([next], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheBackgroundLight_NeedsTheBackgroundMethodInTheList_AndNotThatOfTheMainLight(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true, Support = Without(MainMethods, wire.Method) });
            Assert.DoesNotContain(wire.Method, h.Device.SupportedMethods);
            Assert.Contains("bg_" + wire.Method, h.Device.SupportedMethods);

            await wire.Invoke(h.Device.Background);

            Wire.Sent(h.Bulb, "bg_" + wire.Method, wire.Parameters);
        }

        [Fact]
        public async Task ALightWithoutABackgroundLight_ListsNoBackgroundMethod_AndTheBackgroundLightRefusesEveryCommand()
        {
            await using var h = await Wire.StartAsync();
            Assert.False(h.Device.SupportsBackgroundLight);

            foreach (var wire in WireCases.Light)
            {
                var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => wire.Invoke(h.Device.Background));
                Assert.Contains($"'bg_{wire.Method}'", refusal.Message);
            }

            await h.Device.SendCommandAsync("get_prop", ["power"]);
            Assert.Equal(["get_prop"], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Theory]
        [MemberData(nameof(DeviceNames))]
        public async Task TheDevice_RefusesACommandWhoseMethodTheLightDoesNotList_AndSendsNothing(string name)
        {
            var refused = DeviceCases.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = Without(EveryMethod, refused.Method) });
            var next = AnotherMethod(refused.Method);

            var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => refused.Invoke(h.Device));

            Assert.Contains($"'{refused.Method}'", refusal.Message);
            await h.Device.SendCommandAsync(next);
            Assert.Equal([next], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task ALightThatHasNotSaidWhatItSupports_IsSentEveryCommand_OfTheMainLight(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = string.Empty });
            Assert.Empty(h.Device.SupportedMethods);

            var failure = await Assert.ThrowsAsync<YeelightException>(() => wire.Invoke(h.Device));

            // This light takes nothing, as it said: the refusal is the answer of the light, which means that the command was sent.
            Assert.Equal(YeelightFailureKind.DeviceRejected, failure.Kind);
            Wire.Sent(h.Bulb, wire.Method, wire.Parameters);
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task ALightThatHasNotSaidWhatItSupports_IsSentEveryCommand_OfTheBackgroundLight(string name)
        {
            var wire = WireCases.Light.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = string.Empty });

            var failure = await Assert.ThrowsAsync<YeelightException>(() => wire.Invoke(h.Device.Background));

            Assert.Equal(YeelightFailureKind.DeviceRejected, failure.Kind);
            Wire.Sent(h.Bulb, "bg_" + wire.Method, wire.Parameters);
        }

        [Theory]
        [MemberData(nameof(DeviceNames))]
        public async Task ALightThatHasNotSaidWhatItSupports_IsSentEveryCommand_OfTheDevice(string name)
        {
            var refused = DeviceCases.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = string.Empty });

            var failure = await Assert.ThrowsAsync<YeelightException>(() => refused.Invoke(h.Device));

            Assert.Equal(YeelightFailureKind.DeviceRejected, failure.Kind);
            Assert.Contains(refused.Method, h.Bulb.Requests.Select(request => request.Method));
        }

        [Fact]
        public async Task SendCommandAsync_IsNeverHeldBackByTheList_OfAMethodThatTheLightDoesNotList()
        {
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { Support = "get_prop" });

            var result = await h.Device.SendCommandAsync("set_bright", [50, "sudden", 0]);

            Wire.Sent(h.Bulb, "set_bright", """[50,"sudden",0]""");
            Assert.False(result.Success);
            Assert.Equal("unsupported method", result.Error!.Message);
        }
    }
}
