namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The lights that the test app simulates for people who have none at hand: they exist only on the loopback.</summary>
    public sealed class SimulatedLightsTests
    {
        [Fact]
        public void EveryKind_HasAnIdAndANameOfItsOwn()
        {
            var kinds = Enum.GetValues<SimulatedKind>();

            var options = kinds.Select(SimulatedLights.OptionsFor).ToArray();

            Assert.Equal(3, kinds.Length);
            Assert.Equal(3, options.Select(option => option.Id).Distinct().Count());
            Assert.Equal(3, options.Select(option => option.Name).Distinct().Count());
            Assert.Equal(3, options.Select(option => option.Model).Distinct().Count());
            Assert.All(options, option => Assert.StartsWith("0x", option.Id, StringComparison.Ordinal));
            Assert.All(kinds, kind => Assert.Equal(SimulatedLights.NameOf(kind), SimulatedLights.OptionsFor(kind).Name));
        }

        [Fact]
        public void OnlyTheCeilingLight_HasABackgroundLight()
        {
            Assert.False(SimulatedLights.OptionsFor(SimulatedKind.Color).HasBackground);
            Assert.False(SimulatedLights.OptionsFor(SimulatedKind.White).HasBackground);
            Assert.True(SimulatedLights.OptionsFor(SimulatedKind.Ceiling).HasBackground);
        }

        [Fact]
        public void TheWhiteLight_ListsNoMethodsForColours()
        {
            var support = SimulatedLights.OptionsFor(SimulatedKind.White).Support.Split(' ');

            Assert.DoesNotContain("set_rgb", support);
            Assert.DoesNotContain("set_hsv", support);
            Assert.DoesNotContain("adjust_color", support);
            Assert.Contains("set_bright", support);
            Assert.Contains("set_power", support);
            Assert.Contains("get_prop", support);
        }

        [Fact]
        public async Task Start_ReturnsTheLightThatRunsAlready_AndStopAllStopsThem()
        {
            await using var lights = new SimulatedLights();

            var first = lights.Start(SimulatedKind.Color);
            var again = lights.Start(SimulatedKind.Color);
            var other = lights.Start(SimulatedKind.Ceiling);

            Assert.Same(first, again);
            Assert.NotSame(first, other);
            Assert.Equal(2, lights.Running.Count);
            Assert.Same(first, lights.Find(first.Id.ToUpperInvariant().Replace("0X", "0x")));
            Assert.Null(lights.Find("0x0000000000000000"));
            Assert.Contains("127.0.0.1", first.Description, StringComparison.Ordinal);
            Assert.NotEqual(first.DiscoveryPort, other.DiscoveryPort);

            var stopped = await lights.StopAllAsync();

            Assert.Equal(2, stopped.Count);
            Assert.Empty(lights.Running);
            Assert.NotSame(first, lights.Start(SimulatedKind.Color));
        }

        [Fact]
        public async Task AClient_FindsEveryKindWithAProbeOnTheLoopback_AndKnowsWhatItCanDo()
        {
            await using var lights = new SimulatedLights();
            var colour = lights.Start(SimulatedKind.Color);
            var white = lights.Start(SimulatedKind.White);
            var ceiling = lights.Start(SimulatedKind.Ceiling);
            var client = TestClients.Create(searchTarget: colour.Bulb);
            try
            {
                var foundColour = await client.ProbeAsync(IPAddress.Loopback, colour.DiscoveryPort);
                var foundWhite = await client.ProbeAsync(IPAddress.Loopback, white.DiscoveryPort);
                var foundCeiling = await client.ProbeAsync(IPAddress.Loopback, ceiling.DiscoveryPort);

                Assert.NotNull(foundColour);
                Assert.NotNull(foundWhite);
                Assert.NotNull(foundCeiling);
                Assert.Equal(3, client.Devices.Count);
                Assert.True(foundColour!.SupportsColor);
                Assert.True(foundColour.SupportsColorTemperature);
                Assert.False(foundColour.SupportsBackgroundLight);
                Assert.False(foundWhite!.SupportsColor);
                Assert.False(foundWhite.SupportsColorTemperature);
                Assert.False(foundWhite.SupportsBackgroundLight);
                Assert.True(foundWhite.Supports("set_bright"));
                Assert.True(foundCeiling!.SupportsBackgroundLight);
                Assert.True(foundCeiling.SupportsColor);
                Assert.Equal(SimulatedLights.NameOf(SimulatedKind.Ceiling), foundCeiling.Name);
                Assert.Equal("mono", foundWhite.Model);
                await foundWhite.SetBrightnessAsync(40);
                await Assert.ThrowsAsync<NotSupportedException>(() => foundWhite.SetRgbAsync(1, 2, 3));
                await foundCeiling.Background.TurnOnAsync();
                Assert.Equal("on", ceiling.Bulb.Get("bg_power"));
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task TheSimulatedLights_AnswerOnTheLoopbackAndNowhereElse()
        {
            await using var lights = new SimulatedLights();

            var light = lights.Start(SimulatedKind.Color);

            Assert.Equal(IPAddress.Loopback, light.Bulb.DiscoveryEndPoint.Address);
            Assert.StartsWith("yeelight://127.0.0.1:", light.Bulb.Location, StringComparison.Ordinal);
            await Task.CompletedTask;
        }
    }
}
