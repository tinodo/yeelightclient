namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What happens to a device when the client forgets it, and when the address that its location holds is not one.
    /// </summary>
    public class DeviceLifecycleTests
    {
        [Fact]
        public async Task ADeviceThatTheClientForgot_FailsItsCommandsAsNotConnected_WithoutTouchingTheNetwork()
        {
            await using var client = UnitDevices.Client();

            // On the loopback, at a port where nothing listens: a client that did try to connect would find nobody, and nobody else.
            var location = $"yeelight://127.0.0.1:{FreePort.Tcp()}";
            var device = UnitDevices.Add(client, UnitMessages.Response(location: location, support: SimulatedBulbOptions.AllMethods + " " + SimulatedBulbOptions.BackgroundMethods));
            Assert.True(await client.RemoveDeviceAsync(device));

            var exceptions = new[]
            {
                await Assert.ThrowsAsync<YeelightException>(() => device.ToggleAsync()),
                await Assert.ThrowsAsync<YeelightException>(() => device.SetBrightnessAsync(50)),
                await Assert.ThrowsAsync<YeelightException>(() => device.Background.ToggleAsync()),
                await Assert.ThrowsAsync<YeelightException>(() => device.GetPropertiesAsync(["power"])),
                await Assert.ThrowsAsync<YeelightException>(() => device.RefreshAsync()),
                await Assert.ThrowsAsync<YeelightException>(() => device.GetSleepTimerAsync()),
                await Assert.ThrowsAsync<YeelightException>(() => device.ConnectAsync()),
                await Assert.ThrowsAsync<YeelightException>(() => device.StartMusicModeAsync()),
            };
            var raw = await device.SendCommandAsync("toggle");

            Assert.All(exceptions, exception => Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind));
            Assert.False(raw.Success);
            Assert.Equal(YeelightFailureKind.NotConnected, raw.Error!.Kind);
            Assert.False(device.IsConnected);
        }

        [Fact]
        public async Task ADeviceThatTheClientForgot_CanBeAskedToDisconnect_AndToBeForgottenAgain()
        {
            await using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client);
            await client.RemoveDeviceAsync(device);

            await device.DisconnectAsync();

            Assert.False(await client.RemoveDeviceAsync(device));
            Assert.False(device.IsConnected);
        }

        [Fact]
        public async Task ADeviceWhoseLocationIsAName_HasNoAddress_UntilALocationWithOneComesIn()
        {
            await using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://bulb.local:55443"));
            var log = new PropertyLog(device);
            Assert.Null(device.Address);
            Assert.Equal("yeelight://bulb.local:55443", device.Location);
            Assert.Equal(55443, device.Port);

            UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55443"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(IPAddress.Parse("192.168.1.50"), device.Address);
            Assert.Equal("yeelight://192.168.1.50:55443", device.Location);
            Assert.Equal(["Location", "Address"], log.Names.ToArray());
        }

        [Fact]
        public async Task ADeviceThatMovesToAnotherPort_OnTheSameAddress_TellsOfTheLocationAndThePortOnly()
        {
            await using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55443"));
            var log = new PropertyLog(device);

            UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55444"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(55444, device.Port);
            Assert.Equal(IPAddress.Parse("192.168.1.50"), device.Address);
            Assert.Equal(["Location", "Port"], log.Names.ToArray());
        }

        [Fact]
        public async Task ADeviceThatMovesToAnotherAddress_AndAnotherPort_TellsOfAllThree()
        {
            await using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55443"));
            var log = new PropertyLog(device);

            UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.51:55444"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["Location", "Address", "Port"], log.Names.ToArray());
        }

        [Fact]
        public async Task AnAnnouncementWithTheSameLocation_TellsOfNoMove()
        {
            await using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55443"));
            var log = new PropertyLog(device);

            UnitDevices.Add(client, UnitMessages.Response(location: "yeelight://192.168.1.50:55443"));
            await UnitDevices.FlushAsync(device);

            Assert.DoesNotContain("Address", log.Names);
            Assert.DoesNotContain("Port", log.Names);
        }

        [Fact]
        public async Task TheMusicSessionsOfADevice_EndWhenTheClientForgetsTheDevice()
        {
            await using var h = await Harness.StartAsync();
            var session = await h.Device.StartMusicModeAsync();
            Assert.True(session.IsConnected);

            Assert.True(await h.Client.RemoveDeviceAsync(h.Device));

            Assert.False(session.IsConnected);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(h.Device.IsConnected);
        }
    }
}
