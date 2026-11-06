namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// Music mode (<c>set_music</c>, https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the client listens, tells the light
    /// where with <c>[1, host, port]</c>, the light connects, and the commands that go over that connection are the commands of the control
    /// connection, without a limit on their number and without an answer. <c>[0]</c> ends it.
    /// </summary>
    public class MusicSessionWireTests
    {
        public static TheoryData<string> Cases => new(WireCases.Music.Select(wire => wire.Name));

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task ACommandOfASession_IsSentOverTheMusicConnection_AsTheSpecificationSaysItForTheControlConnection(string name)
        {
            var wire = WireCases.Music.Single(candidate => candidate.Name == name);
            await using var h = await Wire.StartAsync();
            await using var session = await h.Device.StartMusicModeAsync();
            var before = h.Bulb.Requests.Count;

            await wire.Invoke(session);

            await Wait.UntilAsync(() => h.Bulb.Requests.Count > before, "the command over the music connection");
            var request = Assert.Single(h.Bulb.Requests.Skip(before));
            Assert.True(request.OverMusic, "A command of a session goes over the music connection.");
            Wire.Matches(request, wire.Method, wire.Parameters);
        }

        [Fact]
        public async Task StartMusicModeAsync_TellsTheLightWhereToConnect_ByTheAddressOfThisComputerAndThePortOfTheListener()
        {
            await using var h = await Wire.StartAsync();

            await using var session = await h.Device.StartMusicModeAsync();

            var request = Assert.Single(h.Bulb.RequestsOf("set_music"));
            Assert.Equal(3, request.Parameters.Length);
            Assert.Equal(JsonValueKind.Number, request.Parameters[0].ValueKind);
            Assert.Equal("1", request.Parameters[0].GetRawText());
            Assert.Equal(JsonValueKind.String, request.Parameters[1].ValueKind);
            Assert.Equal("127.0.0.1", request.Parameters[1].GetString());
            Assert.Equal(JsonValueKind.Number, request.Parameters[2].ValueKind);
            var port = request.Parameters[2].GetInt32();
            Assert.InRange(port, 1, 65535);
            Wire.Matches(request, "set_music", $"[1,\"127.0.0.1\",{port}]");
            Assert.False(request.OverMusic);
            await Wait.UntilAsync(() => h.Bulb.InMusicMode, "the light in music mode");
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task StartMusicModeAsync_WithAnAddressAndAPort_TellsTheLightThose()
        {
            var port = FreePort.Tcp();
            await using var h = await Wire.StartAsync();

            await using var session = await h.Device.StartMusicModeAsync(IPAddress.Loopback, port);

            Wire.Sent(h.Bulb, "set_music", $"[1,\"127.0.0.1\",{port}]");
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task ASession_LeavesMusicModeWithSetMusic0_OnTheControlConnection()
        {
            await using var h = await Wire.StartAsync();
            var session = await h.Device.StartMusicModeAsync();

            await session.DisposeAsync();

            var requests = h.Bulb.RequestsOf("set_music");
            Assert.Equal(2, requests.Count);
            Wire.Matches(requests[1], "set_music", "[0]");
            Assert.False(requests[1].OverMusic, "The light is told on the control connection.");
            Assert.False(session.IsConnected);
        }

        [Fact]
        public async Task StartMusicModeAsync_AnAddressThatThisComputerCannotListenOn_SendsNothingToTheLight()
        {
            await using var h = await Wire.StartAsync();

            // The documentation does not say which exception: what counts is that the light is not told to connect to nobody.
            await Assert.ThrowsAnyAsync<Exception>(() => h.Device.StartMusicModeAsync(IPAddress.Parse("203.0.113.77")));

            Assert.Empty(h.Bulb.Requests);
            await h.Device.SetDefaultAsync();
            Assert.Equal(["set_default"], h.Bulb.Requests.Select(request => request.Method).ToArray());
        }

        [Fact]
        public async Task StartMusicModeAsync_APortThatIsInUse_SendsNothingToTheLight_AndALaterStartWorks()
        {
            using var busy = new TcpListener(IPAddress.Loopback, 0);
            busy.Start();
            var port = ((IPEndPoint)busy.LocalEndpoint).Port;
            await using var h = await Wire.StartAsync();

            await Assert.ThrowsAnyAsync<Exception>(() => h.Device.StartMusicModeAsync(IPAddress.Loopback, port));

            Assert.Empty(h.Bulb.Requests);
            await using var session = await h.Device.StartMusicModeAsync();
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task TheNumbersOfTheCommandsOfASession_AreUnique_AndGrow()
        {
            await using var h = await Wire.StartAsync();
            await using var session = await h.Device.StartMusicModeAsync();

            for (var index = 0; index < 20; index++)
            {
                await session.SetBrightnessAsync((index % 100) + 1);
            }

            await Wait.UntilAsync(() => h.Bulb.Requests.Count(request => request.OverMusic) == 20, "20 commands over the music connection");
            var ids = h.Bulb.Requests.Where(request => request.OverMusic).Select(request => request.Id).ToArray();
            Assert.Equal(ids.Distinct().ToArray(), ids);
            Assert.Equal(ids.Order().ToArray(), ids);
            Assert.All(ids, id => Assert.True(id > 0));
        }
    }
}
