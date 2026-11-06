namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// An argument that the light would refuse (a brightness of 101, a transition of 29 milliseconds, the 65th byte of a name) is refused by
    /// the client first: nothing is sent, and the connection is not even made for it. The ranges are those of table 4-1 of the specification
    /// (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) and of the notes of the methods.
    /// </summary>
    public class RefusedCommandsTests
    {
        private static readonly (string Name, Type Exception, string ParamName, Func<YeelightDevice, Task> Invoke)[] DeviceCases =
        [
            ("GetPropertiesAsync(null)", typeof(ArgumentNullException), "names", device => device.GetPropertiesAsync(null!)),
            ("GetPropertiesAsync(no names)", typeof(ArgumentException), "names", device => device.GetPropertiesAsync([])),
            ("GetPropertiesAsync(an empty name)", typeof(ArgumentException), "names", device => device.GetPropertiesAsync(["power", string.Empty])),
            ("GetPropertiesAsync(a name of spaces)", typeof(ArgumentException), "names", device => device.GetPropertiesAsync(["power", "  "])),
            ("GetPropertiesAsync(a name that is null)", typeof(ArgumentException), "names", device => device.GetPropertiesAsync(["power", null!])),
            ("SetNameAsync(null)", typeof(ArgumentNullException), "name", device => device.SetNameAsync(null!)),
            ("SetNameAsync(65 bytes)", typeof(ArgumentException), "name", device => device.SetNameAsync(new string('a', 65))),
            ("SetNameAsync(66 bytes of 3 bytes characters)", typeof(ArgumentException), "name", device => device.SetNameAsync(new string('€', 22))),
            ("SetNameAsync(68 bytes of 4 bytes characters)", typeof(ArgumentException), "name", device => device.SetNameAsync(string.Concat(Enumerable.Repeat("🌟", 17)))),
            ("SetSleepTimerAsync(a minute less a millisecond)", typeof(ArgumentOutOfRangeException), "duration", device => device.SetSleepTimerAsync(TimeSpan.FromMilliseconds(59_999))),
            ("SetSleepTimerAsync(zero)", typeof(ArgumentOutOfRangeException), "duration", device => device.SetSleepTimerAsync(TimeSpan.Zero)),
            ("SetSleepTimerAsync(minus a minute)", typeof(ArgumentOutOfRangeException), "duration", device => device.SetSleepTimerAsync(TimeSpan.FromMinutes(-1))),
            ("SetSleepTimerAsync(the longest time)", typeof(ArgumentOutOfRangeException), "duration", device => device.SetSleepTimerAsync(TimeSpan.MaxValue)),
            ("SetSleepTimerAsync(the shortest time)", typeof(ArgumentOutOfRangeException), "duration", device => device.SetSleepTimerAsync(TimeSpan.MinValue)),
            ("StartMusicModeAsync(port -1)", typeof(ArgumentOutOfRangeException), "port", device => device.StartMusicModeAsync(null, -1)),
            ("StartMusicModeAsync(port 65536)", typeof(ArgumentOutOfRangeException), "port", device => device.StartMusicModeAsync(IPAddress.Loopback, 65536)),
        ];

        public static TheoryData<string> LightCases => new(WireCases.Refused.Select(refused => refused.Name));

        public static TheoryData<string> DeviceNames => new(DeviceCases.Select(refused => refused.Name));

        public static TheoryData<string> MusicCases => new(WireCases.MusicRefused.Select(refused => refused.Name));

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheMainLight_RefusesWhatTheLightWouldRefuse_AndSendsNothing(string name)
        {
            var refused = WireCases.Refused.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync();

            await Wire.RefusedAsync(h.Bulb, refused.Exception, refused.ParamName, () => refused.Invoke(h.Device), () => h.Device.SetDefaultAsync(), "set_default");
        }

        [Theory]
        [MemberData(nameof(LightCases))]
        public async Task TheBackgroundLight_RefusesTheSame_AndSendsNothing(string name)
        {
            var refused = WireCases.Refused.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });

            await Wire.RefusedAsync(h.Bulb, refused.Exception, refused.ParamName, () => refused.Invoke(h.Device.Background), () => h.Device.Background.SetDefaultAsync(), "bg_set_default");
        }

        [Theory]
        [MemberData(nameof(DeviceNames))]
        public async Task TheDevice_RefusesWhatTheLightWouldRefuse_AndSendsNothing(string name)
        {
            var refused = DeviceCases.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync();

            await Wire.RefusedAsync(h.Bulb, refused.Exception, refused.ParamName, () => refused.Invoke(h.Device), () => h.Device.SetDefaultAsync(), "set_default");
            Assert.Empty(h.Bulb.RequestsOf("set_music"));
        }

        [Theory]
        [MemberData(nameof(MusicCases))]
        public async Task ASessionOfMusicMode_RefusesWhatTheLightWouldRefuse_AndSendsNothing(string name)
        {
            var refused = WireCases.MusicRefused.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync();
            await using var session = await h.Device.StartMusicModeAsync();

            await Wire.RefusedOverMusicAsync(h.Bulb, session, refused.Exception, refused.ParamName, () => refused.Invoke(session));
        }

        [Fact]
        public async Task ARefusedCommand_DoesNotEvenConnectToTheLight()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, AutoConnect = false, RefreshOnConnect = false }, searchTarget: bulb);
            var device = await client.ProbeAsync(IPAddress.Loopback);
            Assert.NotNull(device);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetBrightnessAsync(101));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetPowerAsync(true, TimeSpan.FromMilliseconds(29)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.Background.SetColorTemperatureAsync(1699));
            await Assert.ThrowsAsync<ArgumentException>(() => device.SetNameAsync(new string('a', 65)));
            await Assert.ThrowsAsync<ArgumentException>(() => device.AdjustAsync(AdjustAction.Increase, AdjustProperty.Color));
            await Assert.ThrowsAsync<ArgumentNullException>(() => device.StartColorFlowAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => device.SetSceneAsync(null!));
            await Wait.NotHappensAsync(() => bulb.ConnectionCount > 0, "a connection for a command that is refused");
            Assert.Empty(bulb.Requests);

            await device.SetDefaultAsync();

            Assert.Equal(1, bulb.ConnectionCount);
            Assert.Equal(["set_default"], bulb.Requests.Select(request => request.Method).ToArray());
        }
    }
}
