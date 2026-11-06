namespace Yeelight.Tests.Regression
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging.Abstractions;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// What an independent review of the library found, kept from coming back: each of these fails on the code that had the defect.
    /// </summary>
    public class ReviewRegressionTests
    {
        private static YeelightClientOptions Options(int discoveryPort, TimeSpan? commandTimeout = null, int maxDevices = 1000)
        {
            return new YeelightClientOptions
            {
                DiscoveryPort = discoveryPort,
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(3),
                MaxDevices = maxDevices,
            };
        }

        [Theory]
        [InlineData("{\"id\":5}")]
        [InlineData("{\"id\":5,\"error\":\"nope\"}")]
        [InlineData("{\"id\":5,\"error\":null}")]
        [InlineData("{\"id\":5,\"result\":\"ok\"}")]
        [InlineData("{\"id\":5,\"result\":{\"a\":1}}")]
        public void ALineWithAnIdThatHasNeitherAListOfResultsNorAnError_IsNotAnAnswer(string line)
        {
            Assert.False(YeelightProtocol.TryParse(line, out _));
        }

        [Fact]
        public void ALineWithAListOfResultsOrAnErrorObject_IsAnAnswer()
        {
            Assert.True(YeelightProtocol.TryParse("{\"id\":5,\"result\":[\"ok\"]}", out var accepted));
            var success = Assert.IsType<YeelightResponse>(accepted).Result;
            Assert.Equal(5, success.Id);
            Assert.True(success.Success);
            Assert.Equal("ok", success.GetString());

            Assert.True(YeelightProtocol.TryParse("{\"id\":6,\"result\":[]}", out var empty));
            Assert.True(Assert.IsType<YeelightResponse>(empty).Result.Success);

            Assert.True(YeelightProtocol.TryParse("{\"id\":7,\"error\":{\"code\":-1,\"message\":\"unsupported method\"}}", out var refused));
            var error = Assert.IsType<YeelightResponse>(refused).Result.Error;
            Assert.NotNull(error);
            Assert.Equal(-1, error.Code);
            Assert.Equal("unsupported method", error.Message);

            Assert.True(YeelightProtocol.TryParse("{\"id\":8,\"error\":{\"code\":-1,\"message\":\"x\"},\"result\":[\"ok\"]}", out var both));
            Assert.False(Assert.IsType<YeelightResponse>(both).Result.Success);
        }

        [Fact]
        public void TheValuesOfAnAnswer_CannotBeChangedThroughTheList()
        {
            Assert.True(YeelightProtocol.TryParse("{\"id\":5,\"result\":[\"ok\"]}", out var message));
            var values = Assert.IsType<YeelightResponse>(message).Result.Values;

            Assert.Throws<NotSupportedException>(() => ((IList<JsonElement>)values).Add(default));
        }

        [Fact]
        public async Task ALineThatIsNotAnAnswer_DoesNotMakeACommandSucceed()
        {
            await using var harness = await Harness.StartAsync(options: port => Options(port, commandTimeout: TimeSpan.FromMilliseconds(300)));
            harness.Bulb.ReplyOverride = request => request.Method == "set_bright" ? new[] { $"{{\"id\":{request.Id}}}" } : null;

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.SetBrightnessAsync(40));

            Assert.Equal(YeelightFailureKind.Timeout, exception.Kind);
        }

        [Fact]
        public async Task ACommandThatFailsOnAConnectionThatWasReplaced_LeavesTheNewConnectionAlone()
        {
            await using var bulb = new SimulatedBulb();
            var options = new YeelightConnectionOptions
            {
                CommandsPerMinute = 60,
                CommandBurst = 1,
                RateLimitWait = TimeSpan.FromSeconds(3),
                KeepAliveInterval = TimeSpan.Zero,
                ReconnectDelayMin = TimeSpan.FromMilliseconds(50),
                ReconnectDelayMax = TimeSpan.FromMilliseconds(100),
            };
            await using var connection = new YeelightConnection("127.0.0.1", bulb.ControlPort, options, NullLogger.Instance, "test");
            var changes = new List<bool>();
            connection.ConnectedChanged += connected =>
            {
                lock (changes)
                {
                    changes.Add(connected);
                }
            };

            Assert.True(await connection.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
            Assert.True((await connection.SendAsync("get_prop", ["power"])).Success);

            // This command has the first connection, and waits for its turn under the limit on the number of commands.
            var waiting = connection.SendAsync("get_prop", ["power"]);
            await Task.Delay(100);
            bulb.DropConnections();
            await Wait.UntilAsync(() => Changes(changes).SequenceEqual(new[] { true, false, true }), "the connection to be made again");

            var result = await waiting;

            Assert.Equal(YeelightFailureKind.ConnectionLost, result.Error?.Kind);
            await Task.Delay(300);
            Assert.Equal(new[] { true, false, true }, Changes(changes));
            Assert.True(connection.IsConnected);
            Assert.True((await connection.SendAsync("get_prop", ["power"])).Success);
        }

        [Fact]
        public async Task TheNumbersOfCommands_StartAgainAfterTheLargestOne_AndNeverRepeat()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = new YeelightConnection("127.0.0.1", bulb.ControlPort, new YeelightConnectionOptions(), NullLogger.Instance, "test");
            Assert.True(await connection.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
            var next = typeof(YeelightConnection).GetField("_nextId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(next);
            next.SetValue(connection, int.MaxValue - 2);

            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => connection.SendAsync("get_prop", ["power"])));

            Assert.All(results, result => Assert.True(result.Success, result.ToString()));
            var ids = bulb.Requests.Select(request => request.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Contains(int.MaxValue, ids);
            Assert.Contains(1, ids);
            Assert.All(ids, id => Assert.True(id > 0));
        }

        [Fact]
        public async Task AHandlerThatIsSlowerThanTheLight_SeesTheLatestValue_WithoutAQueueOfEveryChange()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            await Wait.UntilAsync(() => device.ActiveMode is not null, "the properties that were read after connecting");
            using var release = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();
            var events = 0;
            var seen = new List<int>();
            device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(YeelightLight.Brightness))
                {
                    return;
                }

                Interlocked.Increment(ref events);
                lock (seen)
                {
                    seen.Add(device.Brightness);
                }

                started.Set();
                release.Wait(TimeSpan.FromSeconds(20));
            };

            await harness.Bulb.ChangeAsync(("bright", "10"));
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "the handler to be called");

            // The only thread that raises events is held in the handler: the changes pile up behind it, for a light that cannot be told to wait.
            for (var i = 0; i < 300; i++)
            {
                await harness.Bulb.ChangeAsync(("bright", (i % 99 + 1).ToString(CultureInfo.InvariantCulture)));
            }

            await harness.Bulb.ChangeAsync(("bright", "77"));
            await Wait.UntilAsync(() => device.Brightness == 77, "the last change to be taken over");
            release.Set();
            await Wait.UntilAsync(() => Last(seen) == 77, "the handler to see the last value");

            await Task.Delay(200);
            Assert.InRange(Volatile.Read(ref events), 2, 4);
        }

        [Fact]
        public async Task DisposingTheClient_EndsADiscoverThatIsWaiting()
        {
            var client = TestClients.Create();
            var discovering = client.DiscoverAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(100);
            var stopwatch = Stopwatch.StartNew();

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"It took {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task DisposingTheClient_EndsAWaitForADeviceThatHasNoEnd()
        {
            var client = TestClients.Create();
            var waiting = client.WaitForDeviceAsync("0x1", Timeout.InfiniteTimeSpan);
            await Task.Delay(100);

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        [Fact]
        public async Task DisposingTheClient_EndsAProbeThatIsWaiting()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            var client = TestClients.Create(Options(bulb.DiscoveryPort));
            var probing = client.ProbeAsync(IPAddress.Loopback);
            await Task.Delay(200);
            var stopwatch = Stopwatch.StartNew();

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"It took {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task MusicModeThatTheLightDoesNotConnectFor_IsSwitchedOffWhenTheWaitIsCancelled()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ConnectsForMusic = false;
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.StartMusicModeAsync(cancellationToken: cancel.Token));

            await Wait.UntilAsync(() => SwitchedOff(harness.Bulb) == 1, "the light to be told to leave music mode");
        }

        [Fact]
        public async Task MusicModeThatTheLightDoesNotConnectFor_IsSwitchedOffWhenTheWaitRunsOut()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ConnectsForMusic = false;

            var exception = await Assert.ThrowsAsync<YeelightException>(() => harness.Device.StartMusicModeAsync());

            Assert.Equal("set_music", exception.Method);
            await Wait.UntilAsync(() => SwitchedOff(harness.Bulb) == 1, "the light to be told to leave music mode");
        }

        [Fact]
        public async Task AMusicModeThatTheLightRefuses_IsNotSwitchedOffAfterwards()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ReplyOverride = request => request.Method == "set_music" ? new[] { $"{{\"id\":{request.Id},\"error\":{{\"code\":-1,\"message\":\"invalid params\"}}}}" } : null;

            await Assert.ThrowsAsync<YeelightException>(() => harness.Device.StartMusicModeAsync());

            await Task.Delay(300);
            Assert.Equal(0, SwitchedOff(harness.Bulb));
        }

        [Fact]
        public void TheMethodsOfAnAnnouncement_MayBeSeparatedByAnyWhitespace()
        {
            const string Message = "HTTP/1.1 200 OK\r\nLocation: yeelight://192.168.1.5:55443\r\nid: 0x1\r\nsupport: get_prop\tset_power  toggle \tset_rgb\r\n";

            Assert.True(DeviceAnnouncement.TryParse(Message, out var announcement));

            Assert.Equal(new[] { "get_prop", "set_power", "toggle", "set_rgb" }, announcement!.Support);
        }

        [Fact]
        public async Task TheListsThatTheApiGives_CannotBeChangedByTheCaller()
        {
            await using var harness = await Harness.StartAsync();

            var devices = Assert.IsAssignableFrom<IList<YeelightDevice>>(harness.Client.Devices);
            var methods = Assert.IsAssignableFrom<IList<string>>(harness.Device.SupportedMethods);
            var flow = new ColorFlow([ColorFlowStep.Sleep(TimeSpan.FromSeconds(1))]);
            var steps = Assert.IsAssignableFrom<IList<ColorFlowStep>>(flow.Steps);

            Assert.True(devices.IsReadOnly);
            Assert.True(methods.IsReadOnly);
            Assert.True(steps.IsReadOnly);
            Assert.IsNotType<YeelightDevice[]>(harness.Client.Devices);
            Assert.IsNotType<string[]>(harness.Device.SupportedMethods);
            Assert.IsNotType<ColorFlowStep[]>(flow.Steps);
            Assert.Throws<NotSupportedException>(() => devices[0] = null!);
        }

        [Fact]
        public async Task TheClient_KeepsNoMoreLightsThanItIsToldTo()
        {
            await using var first = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000001" });
            await using var second = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000002" });
            await using var third = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000003" });
            await using var client = TestClients.Create(Options(first.DiscoveryPort, maxDevices: 2));

            Assert.NotNull(await client.ProbeAsync(IPAddress.Loopback, first.DiscoveryPort));
            Assert.NotNull(await client.ProbeAsync(IPAddress.Loopback, second.DiscoveryPort));
            Assert.Null(await client.ProbeAsync(IPAddress.Loopback, third.DiscoveryPort));

            Assert.Equal(2, client.Devices.Count);
            Assert.Null(client.FindDevice(third.Id));
        }

        [Fact]
        public void TheLimitOnTheNumberOfLights_MustBeAtLeastOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new YeelightClient(new YeelightClientOptions { MaxDevices = 0 }));
        }

        [Fact]
        public async Task ALightThatIsConnected_IsNotMovedByAnAnnouncementFromAnotherAddress_UntilItLosesItsConnection()
        {
            const string Id = "0x00000000000000aa";
            await using var real = new SimulatedBulb(new SimulatedBulbOptions { Id = Id });
            await using var impostor = new SimulatedBulb(new SimulatedBulbOptions { Id = Id });
            await using var client = TestClients.Create(Options(real.DiscoveryPort));

            var device = await client.ProbeAsync(IPAddress.Loopback, real.DiscoveryPort);
            Assert.NotNull(device);
            await device.ConnectAsync();

            var again = await client.ProbeAsync(IPAddress.Loopback, impostor.DiscoveryPort);

            Assert.Same(device, again);
            Assert.Equal(real.ControlPort, device.Port);
            await Task.Delay(300);
            Assert.Equal(0, impostor.ConnectionCount);
            Assert.True(device.IsConnected);

            // A light that really moved has lost its connection: after that the announcement is taken over, and the connection follows it.
            await real.DisposeAsync();
            await Wait.UntilAsync(() => !device.IsConnected, "the connection to be lost");

            Assert.Same(device, await client.ProbeAsync(IPAddress.Loopback, impostor.DiscoveryPort));

            Assert.Equal(impostor.ControlPort, device.Port);
            await Wait.UntilAsync(() => device.IsConnected && impostor.ConnectionCount > 0, "the connection to follow the light");
        }

        [Fact]
        public async Task ADeviceThatWasDisconnected_IsNotConnectedAgainByTheRefreshThatWasWaiting()
        {
            await using var harness = await Harness.StartAsync();

            await harness.Device.DisconnectAsync();
            await Task.Delay(500);

            Assert.False(harness.Device.IsConnected);
            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the light to see that the connection was closed");
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task AMusicModeThatIsCancelledWhileTheLightIsStillAnswering_IsSwitchedOffAsWell()
        {
            await using var harness = await Harness.StartAsync();
            harness.Bulb.ConnectsForMusic = false;
            harness.Bulb.Delay = request => request.Method == "set_music" && request.Parameters.Length == 3 ? TimeSpan.FromSeconds(2) : TimeSpan.Zero;
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Device.StartMusicModeAsync(cancellationToken: cancel.Token));

            await Wait.UntilAsync(() => SwitchedOff(harness.Bulb) == 1, "the light to be told to leave music mode, although its answer never came");
        }

        [Fact]
        public async Task AConnectionThatIsUp_IsNotMovedToAnotherEndpoint_AndOneThatIsNotUpIs()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = new YeelightConnection("127.0.0.1", bulb.ControlPort, new YeelightConnectionOptions(), NullLogger.Instance, "test");

            Assert.True(connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort));
            Assert.True(await connection.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
            var epoch = connection.Epoch;

            Assert.False(connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort + 1));
            Assert.True(connection.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort));

            await Task.Delay(200);
            Assert.True(connection.IsConnected);
            Assert.Equal(epoch, connection.Epoch);
            Assert.Equal($"127.0.0.1:{bulb.ControlPort}", connection.Endpoint);

            await using var idle = new YeelightConnection("127.0.0.1", bulb.ControlPort, new YeelightConnectionOptions(), NullLogger.Instance, "idle");
            Assert.True(idle.TryUpdateEndpoint("127.0.0.1", bulb.ControlPort + 1));
            Assert.Equal($"127.0.0.1:{bulb.ControlPort + 1}", idle.Endpoint);
        }

        [Fact]
        public async Task DisposingTheClientTwice_AtOnce_EndsBothWhenTheConnectionsAreClosed()
        {
            var harness = await Harness.StartAsync();
            try
            {
                var first = harness.Client.DisposeAsync().AsTask();
                var second = harness.Client.DisposeAsync().AsTask();

                await Task.WhenAll(first, second);

                Assert.False(harness.Device.IsConnected);
                await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the light to see the connection closed");
            }
            finally
            {
                await harness.DisposeAsync();
            }
        }

        [Fact]
        public void TheNumbersThatSendCommandAsyncTakes_AreAllTheNumbersOfTheFramework()
        {
            (object Value, string Json)[] cases =
            [
                ((Half)1.5, "1.5"), ((nint)5, "5"), ((nuint)5, "5"), ((Int128)5, "5"), ((UInt128)5, "5"), ('x', "\"x\""), (LongEnum.Big, "5000000000"),
            ];

            foreach (var (value, json) in cases)
            {
                Assert.Equal($"{{\"id\":1,\"method\":\"x\",\"params\":[{json}]}}\r\n", System.Text.Encoding.UTF8.GetString(YeelightProtocol.BuildRequest(1, "x", [value])));
            }

            Assert.Throws<ArgumentException>(() => YeelightProtocol.BuildRequest(1, "x", [(Half)double.NaN]));
        }

        private enum LongEnum : long
        {
            Big = 5_000_000_000,
        }

        private static int SwitchedOff(SimulatedBulb bulb)
        {
            return bulb.RequestsOf("set_music").Count(request => request.Parameters.Length == 1 && request.Parameters[0].GetInt32() == 0);
        }

        private static bool[] Changes(List<bool> changes)
        {
            lock (changes)
            {
                return [.. changes];
            }
        }

        private static int? Last(List<int> values)
        {
            lock (values)
            {
                return values.Count == 0 ? null : values[^1];
            }
        }
    }
}
