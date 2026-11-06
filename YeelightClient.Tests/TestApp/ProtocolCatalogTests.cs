namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Models;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The reference of the Raw page: the methods and the properties of the protocol, and the examples that fill in the form.</summary>
    public sealed class ProtocolCatalogTests
    {
        [Fact]
        public void TheCatalog_HasTheMethodsOfAFullLight_AndNoOthers()
        {
            var expected = (SimulatedBulbOptions.AllMethods + " " + SimulatedBulbOptions.BackgroundMethods).Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(35, ProtocolCatalog.Methods.Count);
            Assert.Equal(expected.Order(StringComparer.Ordinal), ProtocolCatalog.MethodNames.Order(StringComparer.Ordinal));
            Assert.Equal(ProtocolCatalog.Methods.Count, ProtocolCatalog.MethodNames.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void EveryMethod_ExplainsItself()
        {
            foreach (var method in ProtocolCatalog.Methods)
            {
                Assert.False(string.IsNullOrWhiteSpace(method.Group), method.Name);
                Assert.False(string.IsNullOrWhiteSpace(method.Parameters), method.Name);
                Assert.False(string.IsNullOrWhiteSpace(method.Summary), method.Name);
                Assert.NotEmpty(method.Examples);
                Assert.Equal(method.Examples[0], method.Example);
                Assert.Equal(method.Name.StartsWith("bg_", StringComparison.Ordinal), method.IsBackground);
            }
        }

        [Fact]
        public void EveryExample_IsAListThatTheRawPageCanRead()
        {
            foreach (var method in ProtocolCatalog.Methods)
            {
                foreach (var example in method.Examples)
                {
                    Assert.True(JsonTools.TryParseParameters(example, out var parameters, out var error), $"{method.Name}: {example}: {error}");
                    Assert.Equal(JsonTools.FormatParameters(parameters).Replace(" ", string.Empty), example.Replace(" ", string.Empty));
                }
            }
        }

        [Fact]
        public async Task TheFirstExampleOfEveryMethod_IsAcceptedByALightThatListsIt()
        {
            await using var harness = await Harness.StartAsync(
                new SimulatedBulbOptions { HasBackground = true, CommandsPerMinute = 100_000 },
                port => new YeelightClientOptions { DiscoveryPort = port, CommandsPerMinute = 100_000 });

            foreach (var method in ProtocolCatalog.Methods)
            {
                // Some methods of the catalog switch the light off: it has to be on for the ones that need it.
                await harness.Bulb.ChangeAsync(("power", "on"), ("bg_power", "on"));
                Assert.True(JsonTools.TryParseParameters(method.Example, out var parameters, out var error), error);

                var result = await harness.Device.SendCommandAsync(method.Name, parameters);

                Assert.True(result.Success, $"{method.Name} {method.Example}: {result.Error}");
            }
        }

        [Fact]
        public async Task EveryMethodThatNeedsThePowerOn_IsRefusedByALightThatIsOff()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true, CommandsPerMinute = 100_000 }, port => new YeelightClientOptions { DiscoveryPort = port, CommandsPerMinute = 100_000 });
            await harness.SettleAsync();
            await harness.Device.TurnOffAsync();
            await Wait.UntilAsync(() => !harness.Device.IsOn, "the light to be off");

            foreach (var method in ProtocolCatalog.Methods.Where(method => method.NeedsPower && !method.IsBackground))
            {
                Assert.True(JsonTools.TryParseParameters(method.Example, out var parameters, out var error), error);

                var result = await harness.Device.SendCommandAsync(method.Name, parameters);

                Assert.False(result.Success, $"{method.Name} was accepted by a light that is off");
                Assert.Equal(YeelightFailureKind.DeviceRejected, result.Error!.Kind);
            }

            Assert.Equal(7, ProtocolCatalog.Methods.Count(method => method.NeedsPower && !method.IsBackground));
        }

        [Fact]
        public void TheProperties_AreTheTwentyThreeThatGetPropKnows()
        {
            Assert.Equal(23, ProtocolCatalog.Properties.Count);
            Assert.Equal(ProtocolCatalog.Properties.Select(property => property.Name), ProtocolCatalog.PropertyNames);
            Assert.Equal(ProtocolCatalog.PropertyNames.Count, ProtocolCatalog.PropertyNames.Distinct(StringComparer.Ordinal).Count());
            Assert.All(ProtocolCatalog.Properties, property => Assert.False(string.IsNullOrWhiteSpace(property.Meaning), property.Name));
        }

        [Fact]
        public async Task AllThePropertiesOfTheCatalog_AreReadByTheClientToo()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            var values = await harness.Device.GetPropertiesAsync(ProtocolCatalog.PropertyNames);

            Assert.Equal(ProtocolCatalog.PropertyNames.Count, values.Count);
            foreach (var name in ProtocolCatalog.PropertyNames.Where(name => name is not "flow_params" and not "bg_flow_params"))
            {
                Assert.True(values[name].Length > 0, $"the light does not know {name}");
            }
        }

        [Theory]
        [InlineData("set_bright", "set_bright")]
        [InlineData("  set_bright  ", "set_bright")]
        [InlineData("bg_set_power", "bg_set_power")]
        public void AMethod_IsFoundByItsName(string name, string expected)
        {
            Assert.Equal(expected, ProtocolCatalog.Find(name)?.Name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("set_brightness")]
        [InlineData("SET_BRIGHT")]
        [InlineData(null)]
        public void AnUnknownMethod_IsNotFound(string? name)
        {
            Assert.Null(ProtocolCatalog.Find(name));
        }

        [Fact]
        public void TheMethodsOfTheBackgroundLight_AreTheOnesOfTheMainLight_WithAPrefix()
        {
            var main = ProtocolCatalog.Methods.Where(method => !method.IsBackground).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var method in ProtocolCatalog.Methods.Where(method => method.IsBackground))
            {
                Assert.Contains(method.Name["bg_".Length..], main);
                Assert.Equal("Background light", method.Group);
                Assert.Equal(ProtocolCatalog.Find(method.Name["bg_".Length..])!.Example, method.Example);
            }
        }
    }
}
