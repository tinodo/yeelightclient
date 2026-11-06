namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A device as the client keeps it: who it is (its id, its address, its model and the methods that it supports), what it does with
    /// what an announcement tells it again, and what its commands do once it was forgotten. None of these needs a light: a device that a client
    /// takes over from an announcement does not connect.
    /// </summary>
    public class YeelightDeviceTests
    {
        [Fact]
        public void TheIdentity_IsWhatTheAnnouncementSays()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, UnitMessages.Response(model: "ceiling", firmware: "2.0.6_0058"));

            Assert.Equal("0x000000000015243f", device.Id);
            Assert.Equal("yeelight://192.168.1.239:55443", device.Location);
            Assert.Equal(IPAddress.Parse("192.168.1.239"), device.Address);
            Assert.Equal(55443, device.Port);
            Assert.Equal("ceiling", device.Model);
            Assert.Equal("2.0.6_0058", device.FirmwareVersion);
            Assert.Equal("my_bulb", device.Name);
            Assert.False(device.IsConnected);
        }

        [Fact]
        public void TheAddress_IsNull_WhenTheLocationHoldsAName()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://bulb.local:55443"));

            Assert.Null(device.Address);
            Assert.Equal("yeelight://bulb.local:55443", device.Location);
            Assert.Equal(55443, device.Port);
        }

        [Fact]
        public void TheSupportedMethods_AreTheOnesThatTheLightListed_AndCannotBeChangedByWhoReadsThem()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            Assert.Equal(UnitMessages.Support.Split(' '), device.SupportedMethods);
            Assert.IsType<ReadOnlyCollection<string>>(device.SupportedMethods);
            Assert.False(device.SupportedMethods is string[]);
        }

        [Fact]
        public void TheSupportedMethods_OfALightThatDidNotSayWhatItSupports_AreNone()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, UnitMessages.Response(support: string.Empty));

            Assert.Empty(device.SupportedMethods);
            Assert.False(device.Supports("set_power"));
            Assert.False(device.SupportsColor);
            Assert.False(device.SupportsColorTemperature);
            Assert.False(device.SupportsBackgroundLight);
            Assert.False(device.SupportsMusicMode);
        }

        [Theory]
        [InlineData("set_power", true)]
        [InlineData("set_rgb", true)]
        [InlineData("get_prop", true)]
        [InlineData("set_music", false)]
        [InlineData("bg_set_power", false)]
        [InlineData("SET_POWER", false)]
        [InlineData("set_powe", false)]
        [InlineData("set_power ", false)]
        [InlineData("", false)]
        public void Supports_IsWhetherTheLightListsTheMethod_WithTheExactName(string method, bool expected)
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            Assert.Equal(expected, device.Supports(method));
        }

        [Fact]
        public void Supports_ANullMethod_IsRefused()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            var refusal = Assert.Throws<ArgumentNullException>(() => device.Supports(null!));

            Assert.Equal("method", refusal.ParamName);
        }

        [Theory]
        [InlineData("set_rgb", true, false, false, false)]
        [InlineData("set_hsv", true, false, false, false)]
        [InlineData("set_ct_abx", false, true, false, false)]
        [InlineData("set_rgb set_ct_abx", true, true, false, false)]
        [InlineData("bg_set_power", false, false, true, false)]
        [InlineData("set_music", false, false, false, true)]
        [InlineData("get_prop set_power toggle", false, false, false, false)]
        [InlineData("set_rgb set_hsv set_ct_abx bg_set_power set_music", true, true, true, true)]
        public void TheCapabilities_AreWhatTheListOfMethodsSays(string support, bool color, bool colorTemperature, bool background, bool music)
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, UnitMessages.Response(support: support));

            Assert.Equal(color, device.SupportsColor);
            Assert.Equal(colorTemperature, device.SupportsColorTemperature);
            Assert.Equal(background, device.SupportsBackgroundLight);
            Assert.Equal(music, device.SupportsMusicMode);
        }

        [Fact]
        public void ToString_IsTheNameTheIdAndTheLocation()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client);

            Assert.Equal("my_bulb (0x000000000015243f) at yeelight://192.168.1.239:55443", device.ToString());
        }

        [Fact]
        public void ToString_OfALightWithoutAName_SaysTheModel()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, UnitMessages.Response(name: string.Empty, model: "bslamp"));

            Assert.Equal("bslamp (0x000000000015243f) at yeelight://192.168.1.239:55443", device.ToString());
        }

        [Fact]
        public async Task LastSeen_IsWhenTheClientLastHeardFromTheLight_AndItIsNoNews()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);
            var device = UnitDevices.Add(client);
            var log = new PropertyLog(device);
            var first = device.LastSeen;

            clock.Advance(TimeSpan.FromMinutes(5));
            client.Apply(UnitMessages.Parse(UnitMessages.Advertisement()));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(clock.GetUtcNow() - TimeSpan.FromMinutes(5), first);
            Assert.Equal(clock.GetUtcNow(), device.LastSeen);
            Assert.Empty(log.Names);
        }

        [Fact]
        public void AnAnnouncementThatSaysTheSame_ChangesNothingButLastSeen()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            var again = client.Apply(UnitMessages.Parse(UnitMessages.Advertisement()));

            Assert.Same(device, again);
            Assert.Equal("0x000000000015243f", device.Id);
            Assert.Equal(UnitMessages.Support.Split(' '), device.SupportedMethods);
            Assert.Equal("my_bulb", device.Name);
        }

        [Fact]
        public async Task AnAnnouncementThatSaysMore_IsTakenOver_AndReportedInTheOrderOfTheMessage()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(model: "color", firmware: "18", support: "get_prop set_power"));
            var log = new PropertyLog(device);

            client.Apply(UnitMessages.Parse(UnitMessages.Advertisement(model: "ceiling", firmware: "20", support: "get_prop set_power set_rgb", name: "Desk")));
            await UnitDevices.FlushAsync(device);

            Assert.Equal("ceiling", device.Model);
            Assert.Equal("20", device.FirmwareVersion);
            Assert.Equal(["get_prop", "set_power", "set_rgb"], device.SupportedMethods);
            Assert.Equal("Desk", device.Name);
            Assert.Equal(["Model", "FirmwareVersion", "SupportedMethods", "Name"], log.Names);
        }

        [Fact]
        public async Task AnAnnouncementThatLeavesOutTheModelTheFirmwareAndTheMethods_KeepsWhatTheDeviceHas()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            var log = new PropertyLog(device);

            client.Apply(UnitMessages.Parse(UnitMessages.Advertisement(model: string.Empty, firmware: string.Empty, support: string.Empty)));
            await UnitDevices.FlushAsync(device);

            Assert.Equal("color", device.Model);
            Assert.Equal("18", device.FirmwareVersion);
            Assert.Equal(UnitMessages.Support.Split(' '), device.SupportedMethods);
            Assert.Empty(log.Names);
        }

        [Fact]
        public async Task AnAnnouncementThatPutsTheLightAtAnotherAddress_MovesIt_WhenItIsNotConnected()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            var log = new PropertyLog(device);

            client.Apply(UnitMessages.Parse(UnitMessages.Advertisement(location: "yeelight://192.168.1.77:55444")));
            await UnitDevices.FlushAsync(device);

            Assert.Equal("yeelight://192.168.1.77:55444", device.Location);
            Assert.Equal(IPAddress.Parse("192.168.1.77"), device.Address);
            Assert.Equal(55444, device.Port);
            Assert.Equal(["Location", "Address", "Port"], log.Names);
        }

        [Fact]
        public async Task AnAnnouncementThatPutsTheLightAtAnotherPort_MovesIt_WhenItIsNotConnected()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            client.Apply(UnitMessages.Parse(UnitMessages.Advertisement(location: "yeelight://192.168.1.239:55555")));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(55555, device.Port);
            Assert.Equal(IPAddress.Parse("192.168.1.239"), device.Address);
        }

        [Fact]
        public async Task AnAnnouncementThatMovesTheLight_IsIgnoredCompletely_WhileItIsConnected_AndTakenOverWhenItIsNot()
        {
            await using var other = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x00000000000000ff" });
            await using var harness = await Harness.StartAsync(options: port => new YeelightClientOptions { DiscoveryPort = port, RefreshOnConnect = false });
            var device = harness.Device;
            var location = device.Location;
            var port = device.Port;
            await UnitDevices.FlushAsync(device);
            var log = new PropertyLog(device);
            var intruder = UnitMessages.Parse(UnitMessages.Response(location: other.Location, id: harness.Bulb.Id, model: "evil", firmware: "99", support: "get_prop", name: "intruder"));

            // Anyone on the network can announce the id of a light from an address of its own.
            Assert.Same(device, harness.Client.Apply(intruder));
            await UnitDevices.FlushAsync(device);

            Assert.True(device.IsConnected);
            Assert.Equal(location, device.Location);
            Assert.Equal(port, device.Port);
            Assert.Equal("my_bulb", device.Name);
            Assert.Equal("color", device.Model);
            Assert.Equal("18", device.FirmwareVersion);
            Assert.Contains("set_power", device.SupportedMethods);
            Assert.Empty(log.Names);
            await Wait.NotHappensAsync(() => other.ConnectionCount > 0, "a connection to the address of the intruder", TimeSpan.FromMilliseconds(200));

            await device.DisconnectAsync();
            Assert.Same(device, harness.Client.Apply(intruder));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(other.ControlPort, device.Port);
            Assert.Equal("intruder", device.Name);
            Assert.Equal("evil", device.Model);
            Assert.Equal("99", device.FirmwareVersion);
            await device.ConnectAsync();
            await Wait.UntilAsync(() => other.ConnectionCount == 1, "the connection to the new address of the light");
            Assert.Equal(0, harness.Bulb.ClientCount);
        }

        [Fact]
        public async Task ACommandToALightThatWasForgotten_FailsAsNotConnected_AndNothingTriesToConnect()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            Assert.True(await client.RemoveDeviceAsync(device));

            var refusal = await Assert.ThrowsAsync<YeelightException>(() => device.ToggleAsync());
            var connecting = await Assert.ThrowsAsync<YeelightException>(() => device.ConnectAsync());

            Assert.Equal(YeelightFailureKind.NotConnected, refusal.Kind);
            Assert.Equal("toggle", refusal.Method);
            Assert.Equal(YeelightFailureKind.NotConnected, connecting.Kind);
            Assert.False(device.IsConnected);
        }

        [Fact]
        public async Task ACommandToALightOfAClientThatWasDisposed_FailsAsNotConnected()
        {
            var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            await client.DisposeAsync();

            var refusal = await Assert.ThrowsAsync<YeelightException>(() => device.SetBrightnessAsync(50));

            Assert.Equal(YeelightFailureKind.NotConnected, refusal.Kind);
            Assert.Equal("set_bright", refusal.Method);
        }

        [Fact]
        public async Task DisconnectingALightThatWasNeverConnected_IsNothing()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            await device.DisconnectAsync();
            await device.DisconnectAsync();

            Assert.False(device.IsConnected);
        }

        [Fact]
        public async Task ACommandWithAnArgumentThatIsRefused_NeverGetsToWhetherTheLightWasForgotten()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            await client.RemoveDeviceAsync(device);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetBrightnessAsync(101));
        }

        [Fact]
        public async Task ACommandThatTheLightDoesNotList_IsNotSupported_EvenWhenTheLightWasForgotten()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(support: "get_prop"));
            await client.RemoveDeviceAsync(device);

            var refusal = await Assert.ThrowsAsync<NotSupportedException>(() => device.SetBrightnessAsync(50));

            Assert.Contains("set_bright", refusal.Message);
        }

        [Fact]
        public async Task TheBackgroundLightOfAForgottenDevice_FailsAsNotConnectedToo()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(support: UnitMessages.Support + " bg_set_power"));
            await client.RemoveDeviceAsync(device);

            var refusal = await Assert.ThrowsAsync<YeelightException>(() => device.Background.TurnOnAsync());

            Assert.Equal(YeelightFailureKind.NotConnected, refusal.Kind);
            Assert.Equal("bg_set_power", refusal.Method);
        }

        [Fact]
        public async Task SendCommand_ToALightThatWasForgotten_ReturnsTheFailureAndDoesNotThrow()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            await client.RemoveDeviceAsync(device);

            var result = await device.SendCommandAsync("toggle");

            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.NotConnected, result.Error!.Kind);
            Assert.Equal(0, result.Id);
        }

        [Fact]
        public void ADeviceWithAnIdThatDiffersOnlyInCase_IsTheSameDevice()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);

            var again = client.Apply(UnitMessages.Parse(UnitMessages.Response(id: device.Id.ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal))));

            Assert.Same(device, again);
            Assert.Single(client.Devices);
        }
    }
}
