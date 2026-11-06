namespace Yeelight.Tests.Integration
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// Music mode: the light connects to this computer, and takes the commands that it is sent over that connection without a limit on their
    /// number and without answering them.
    /// </summary>
    public class MusicModeTests
    {
        private static Task<Harness> StartAsync(SimulatedBulbOptions? bulb = null)
        {
            return Harness.StartAsync(bulb, port => new YeelightClientOptions { DiscoveryPort = port, RefreshOnConnect = false });
        }

        private static SimulatedRequest[] OverMusic(SimulatedBulb bulb) => [.. bulb.Requests.Where(request => request.OverMusic)];

        // What the light was told in the command that starts music mode: where to connect to.
        private static (string Host, int Port) Told(SimulatedBulb bulb)
        {
            var start = bulb.RequestsOf("set_music").First(request => request.Parameters.Length == 3);
            return (start.Parameters[1].GetString()!, start.Parameters[2].GetInt32());
        }

        // The listener of a session is closed when the start ends, but a port that was freed can be taken by a test that runs beside this one, for a moment:
        // the port counts as closed when a connection to it is refused, which it is a moment later at the latest.
        private static async Task<bool> ConnectionIsRefusedAsync(int port)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);
                }
                catch (SocketException)
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return false;
        }

        [Fact]
        public async Task AMusicSession_IsMade_WhenTheLightConnectsBack_AndItsCommandsGoOverThatConnection()
        {
            await using var harness = await StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync();

            Assert.True(session.IsConnected);
            Assert.False(session.Completion.IsCompleted);
            await Wait.UntilAsync(() => harness.Bulb.InMusicMode, "the light to be in music mode");
            await session.SetBrightnessAsync(30);
            await session.SetPowerAsync(true, TimeSpan.FromMilliseconds(100));

            await Wait.UntilAsync(() => OverMusic(harness.Bulb).Length == 2, "the commands to arrive over the music connection");
            var commands = OverMusic(harness.Bulb);
            Assert.Equal(["set_bright", "set_power"], commands.Select(command => command.Method));
            Assert.Equal(30, commands[0].Parameters[0].GetInt32());
            Assert.Equal("smooth", commands[1].Parameters[1].GetString());
            Assert.True(commands[1].Id > commands[0].Id);
            Assert.Equal("30", harness.Bulb.Get("bright"));
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task TheCommandsOfASession_AreNotRateLimited_AndTheControlConnectionKeepsItsOwnBudget()
        {
            await using var harness = await StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync();
            var watch = Stopwatch.StartNew();

            for (var index = 0; index < 500; index++)
            {
                await session.SetBrightnessAsync((index % 100) + 1);
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"500 commands took {watch.Elapsed.TotalSeconds:0.0} seconds.");
            await Wait.UntilAsync(() => OverMusic(harness.Bulb).Length == 500, "all the commands to arrive");
            await harness.Device.SetNameAsync("the control connection still works");
            Assert.Equal(500, OverMusic(harness.Bulb).Select(command => command.Id).Distinct().Count());
            Assert.DoesNotContain(harness.Bulb.Requests, request => request.OverMusic && request.Method == "get_prop");
        }

        [Fact]
        public async Task TheDevice_KnowsThatItIsInMusicMode_WhenItReadsItsProperties_AndThatItLeftIt_AfterTheSessionEnds()
        {
            await using var harness = await StartAsync();
            var session = await harness.Device.StartMusicModeAsync();
            await Wait.UntilAsync(() => harness.Bulb.InMusicMode, "the light to be in music mode");

            await harness.Device.RefreshAsync();
            Assert.True(harness.Device.IsMusicModeOn);

            await session.DisposeAsync();
            await Wait.UntilAsync(() => !harness.Device.IsMusicModeOn, "the report that the light left music mode");
            await Wait.UntilAsync(() => !harness.Bulb.InMusicMode, "the light to leave music mode");
            Assert.False(session.IsConnected);
            Assert.True(session.Completion.IsCompleted);
        }

        [Fact]
        public async Task InMusicMode_TheLightReportsNothing_SoTheDeviceReadsItsPropertiesAfterwards()
        {
            await using var harness = await StartAsync();
            var session = await harness.Device.StartMusicModeAsync();
            await session.SetBrightnessAsync(20);
            await Wait.UntilAsync(() => harness.Bulb.Get("bright") == "20", "the light to carry out the command");

            await Wait.NotHappensAsync(() => harness.Device.Brightness != 100, "a report of the change while the light is in music mode");
            await session.DisposeAsync();
            await harness.Device.RefreshAsync();

            Assert.Equal(20, harness.Device.Brightness);
        }

        [FactIfLoopbackAlias]
        public async Task OnlyTheConnectionOfTheLight_IsAccepted_NotThatOfAnotherComputer()
        {
            await using var harness = await StartAsync(new SimulatedBulbOptions());
            harness.Bulb.ConnectsForMusic = false;
            var starting = harness.Device.StartMusicModeAsync();
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("set_music").Count == 1, "the light to be told where to connect");
            var (host, port) = Told(harness.Bulb);

            // Another computer on the network finds the port first: its connection is closed, and the wait for the light goes on.
            using var neighbour = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            neighbour.Bind(new IPEndPoint(LoopbackAlias.Address, 0));
            await neighbour.ConnectAsync(IPAddress.Parse(host), port);
            var buffer = new byte[16];
            var closed = await Task.Run(() =>
            {
                try
                {
                    return neighbour.Receive(buffer) == 0;
                }
                catch (SocketException)
                {
                    return true;
                }
            }).WithTimeoutAsync("the connection of the other computer to be closed");
            Assert.True(closed);
            Assert.False(starting.IsCompleted, "The session was made with the connection of a computer that is not the light.");

            // The light connects: its connection is the one that is taken.
            using var light = new TcpClient();
            await light.ConnectAsync(IPAddress.Loopback, port);
            await using var session = await starting.WithTimeoutAsync("the session to be made when the light connected");
            await session.SetBrightnessAsync(55);

            using var reader = new System.IO.StreamReader(light.GetStream(), Encoding.UTF8);
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var document = JsonDocument.Parse(line!);
            Assert.Equal("set_bright", document.RootElement.GetProperty("method").GetString());
            Assert.Equal(55, document.RootElement.GetProperty("params")[0].GetInt32());
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task ALightThatNeverConnects_FailsTheStart_AfterAboutFiveSeconds_ClosesTheListener_AndIsToldToStayOutOfMusicMode()
        {
            await using var harness = await StartAsync();
            harness.Bulb.ConnectsForMusic = false;
            var watch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.StartMusicModeAsync());

            watch.Stop();
            Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind);
            Assert.Contains("firewall", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9));
            var (_, port) = Told(harness.Bulb);
            Assert.True(await ConnectionIsRefusedAsync(port), "The port that the light was told is still open.");
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("set_music").Any(request => request.Parameters.Length == 1 && request.Parameters[0].GetInt32() == 0), "the light to be told that there is no music mode after all");
            Assert.True(harness.Device.IsConnected);
            await harness.Device.SetNameAsync("still works");
        }

        [Fact]
        public async Task AStartThatIsCancelled_TellsTheLightToStayOutOfMusicMode_AndClosesTheListener()
        {
            await using var harness = await StartAsync();
            harness.Bulb.ConnectsForMusic = false;
            using var cancellation = new System.Threading.CancellationTokenSource();
            var starting = harness.Device.StartMusicModeAsync(cancellationToken: cancellation.Token);
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("set_music").Count == 1, "the light to be told where to connect");
            var (_, port) = Told(harness.Bulb);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WithTimeoutAsync("the start to end when it was cancelled", TimeSpan.FromSeconds(5)));
            Assert.True(await ConnectionIsRefusedAsync(port), "The port that the light was told is still open.");
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("set_music").Any(request => request.Parameters.Length == 1 && request.Parameters[0].GetInt32() == 0), "the light to be told that there is no music mode after all");
        }

        [Fact]
        public async Task ARefusedStart_ThrowsAtOnce_AndClosesTheListener()
        {
            await using var harness = await StartAsync();
            harness.Bulb.ReplyOverride = request => request.Method == "set_music" ? [$"{{\"id\":{request.Id},\"error\":{{\"code\":-1,\"message\":\"too many connections\"}}}}"] : null;
            var watch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.StartMusicModeAsync());

            Assert.Equal(YeelightFailureKind.DeviceRejected, exception.Kind);
            Assert.Contains("too many connections", exception.Message, StringComparison.Ordinal);
            Assert.Equal("set_music", exception.Method);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"The refusal took {watch.Elapsed.TotalSeconds:0.0} seconds.");
            var (_, port) = Told(harness.Bulb);
            Assert.True(await ConnectionIsRefusedAsync(port), "The port that the light was told is still open.");
            Assert.True(harness.Device.IsConnected);
        }

        [Fact]
        public async Task TheSession_EndsWhenTheLightClosesTheMusicConnection_AndWritingAfterThatThrowsConnectionLost()
        {
            await using var harness = await StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync();
            await Wait.UntilAsync(() => harness.Bulb.InMusicMode, "the light to be in music mode");

            var result = await harness.Device.SendCommandAsync("set_music", [0]);

            Assert.True(result.Success);
            await session.Completion.WithTimeoutAsync("the session to end when the light closed the connection");
            Assert.False(session.IsConnected);
            var exception = await Assert.ThrowsAsync<YeelightException>(() => session.ToggleAsync());
            Assert.Equal(YeelightFailureKind.ConnectionLost, exception.Kind);
            Assert.Equal("toggle", exception.Method);
            Assert.True(exception.Error!.IsTransportFailure);
        }

        [Fact]
        public async Task TheSession_EndsWhenTheConnectionOfTheLightIsDropped_Entirely()
        {
            await using var harness = await StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync();
            await Wait.UntilAsync(() => harness.Bulb.InMusicMode, "the light to be in music mode");

            harness.Bulb.DropConnections();

            await session.Completion.WithTimeoutAsync("the session to end when the light dropped its connections");
            await Assert.ThrowsAsync<YeelightException>(() => session.SetBrightnessAsync(10));
        }

        [Fact]
        public async Task DisposeAsync_EndsTheSession_TellsTheLightOnTheControlConnection_AndCanBeCalledAgain()
        {
            await using var harness = await StartAsync();
            var session = await harness.Device.StartMusicModeAsync();

            await session.DisposeAsync();
            await session.DisposeAsync();

            Assert.False(session.IsConnected);
            Assert.True(session.Completion.IsCompleted);
            var stops = harness.Bulb.RequestsOf("set_music").Where(request => request.Parameters.Length == 1 && !request.OverMusic).ToArray();
            Assert.Single(stops);
            Assert.Equal(0, stops[0].Parameters[0].GetInt32());
            await Wait.UntilAsync(() => !harness.Bulb.InMusicMode, "the light to leave music mode");
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ToggleAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SetBrightnessAsync(10));
        }

        [Fact]
        public async Task TheLocalAddressAndThePort_CanBeChosen_AndAreTheOnesThatTheLightIsTold()
        {
            await using var harness = await StartAsync();
            var port = FreePort.Tcp();

            await using var session = await harness.Device.StartMusicModeAsync(IPAddress.Loopback, port);

            var (host, told) = Told(harness.Bulb);
            Assert.Equal("127.0.0.1", host);
            Assert.Equal(port, told);
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task WithoutALocalAddress_TheLightIsToldTheAddressThatThisComputerUsesToReachIt()
        {
            await using var harness = await StartAsync();

            await using var session = await harness.Device.StartMusicModeAsync();

            var (host, port) = Told(harness.Bulb);
            Assert.Equal("127.0.0.1", host);
            Assert.InRange(port, 1, 65535);
        }

        [Fact]
        public async Task APortThatIsNotAPort_ALightWithoutMusicMode_AreRefusedBeforeAnythingIsSent()
        {
            await using var harness = await StartAsync();
            var before = harness.Bulb.Requests.Count;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.StartMusicModeAsync(null, -1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Device.StartMusicModeAsync(null, 65536));

            Assert.Equal(before, harness.Bulb.Requests.Count);
            await using var without = await StartAsync(new SimulatedBulbOptions { Support = "get_prop set_power" });
            await Assert.ThrowsAsync<NotSupportedException>(() => without.Device.StartMusicModeAsync());
            Assert.Empty(without.Bulb.RequestsOf("set_music"));
        }

        [Fact]
        public async Task EveryCommandOfASession_IsSentAsTheLightReadsIt_AndAnArgumentThatItWouldRefuseIsRefusedFirst()
        {
            await using var harness = await StartAsync(new SimulatedBulbOptions { HasBackground = true });
            await using var session = await harness.Device.StartMusicModeAsync();

            await session.SetPowerAsync(true);
            await session.ToggleAsync();
            await session.SetBrightnessAsync(40, TimeSpan.FromMilliseconds(200));
            await session.SetColorTemperatureAsync(2700);
            await session.SetRgbAsync(1, 2, 3);
            await session.SetRgbAsync(Color.FromArgb(9, 8, 7));
            await session.SetHsvAsync(10, 20);
            await session.SendCommandAsync("bg_set_rgb", [255, "sudden", 0]);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.SetBrightnessAsync(0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.SetColorTemperatureAsync(100));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.SetRgbAsync(256, 0, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.SetHsvAsync(360, 0));
            await Assert.ThrowsAsync<ArgumentException>(() => session.SendCommandAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => session.SendCommandAsync("toggle", [new object()]));

            await Wait.UntilAsync(() => OverMusic(harness.Bulb).Length == 8, "the commands to arrive");
            await Wait.NotHappensAsync(() => OverMusic(harness.Bulb).Length > 8, "a command that should have been refused");
            var commands = OverMusic(harness.Bulb);
            Assert.Equal(["set_power", "toggle", "set_bright", "set_ct_abx", "set_rgb", "set_rgb", "set_hsv", "bg_set_rgb"], commands.Select(command => command.Method));
            Assert.Equal(["on", "sudden", "0"], commands[0].Parameters.Select(value => value.ToString()));
            Assert.Empty(commands[1].Parameters);
            Assert.Equal(40, commands[2].Parameters[0].GetInt32());
            Assert.Equal(200, commands[2].Parameters[2].GetInt32());
            Assert.Equal(2700, commands[3].Parameters[0].GetInt32());
            Assert.Equal(0x010203, commands[4].Parameters[0].GetInt32());
            Assert.Equal(0x090807, commands[5].Parameters[0].GetInt32());
            Assert.Equal(10, commands[6].Parameters[0].GetInt32());
            Assert.Equal(20, commands[6].Parameters[1].GetInt32());
        }

        [Fact]
        public async Task TheSession_EndsWhenTheClientIsDisposed()
        {
            // The documentation of the session: it ends when it is disposed, when the device closes the connection, or when the client is disposed.
            await using var harness = await StartAsync();
            await using var session = await harness.Device.StartMusicModeAsync();
            await Wait.UntilAsync(() => harness.Bulb.InMusicMode, "the light to be in music mode");

            await harness.Client.DisposeAsync();

            await session.Completion.WithTimeoutAsync("the session to end when the client was disposed", TimeSpan.FromSeconds(8));
            Assert.False(session.IsConnected);
            await Wait.UntilAsync(() => !harness.Bulb.InMusicMode, "the light to leave music mode");
        }
    }
}
