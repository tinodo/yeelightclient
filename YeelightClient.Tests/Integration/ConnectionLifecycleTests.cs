namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// The life of the connection to a light: it is made, it is lost, it is made again, it follows the light to a new address, it finds out
    /// that the light stopped answering, and it ends cleanly when the device or the client is disposed.
    /// </summary>
    public class ConnectionLifecycleTests
    {
        private static Func<int, YeelightClientOptions> Settings(
            bool refresh = true,
            bool autoConnect = true,
            TimeSpan? keepAlive = null,
            TimeSpan? commandTimeout = null,
            TimeSpan? connectTimeout = null)
        {
            return port => new YeelightClientOptions
            {
                DiscoveryPort = port,
                RefreshOnConnect = refresh,
                AutoConnect = autoConnect,
                KeepAliveInterval = keepAlive ?? TimeSpan.FromSeconds(60),
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(3),
                ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3),
            };
        }

        private static int Changes(EventHarness harness) => harness.Log.StatesOf(harness.Device).Count(name => name == nameof(YeelightDevice.IsConnected));

        [Fact]
        public async Task TheConnectionIsMadeAgain_AfterTheLightClosesIt_AndThePropertiesAreReadAgain()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: true));
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("get_prop").Count == 1, "the properties to be read after the first connection");
            Assert.Equal(1, Changes(harness));

            harness.Bulb.DropConnections();

            await harness.Log.WaitUntilAsync(_ => Changes(harness) == 3, "the events about the loss and about the new connection");
            await Wait.UntilAsync(() => harness.Device.IsConnected && harness.Bulb.RequestsOf("get_prop").Count == 2, "the properties to be read after the second connection");
            Assert.Equal(2, harness.Bulb.ConnectionCount);
            Assert.True(harness.Device.IsConnected);
            await harness.Device.SetNameAsync("after the reconnect");
        }

        [Fact]
        public async Task TheConnectionIsReportedWhenItIsLost_AndWhenItIsMade_AndNothingElseIsReportedAboutIt()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false));
            var connected = new EventRecorder<bool>();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightDevice.IsConnected))
                {
                    connected.Add(harness.Device.IsConnected);
                }
            };

            harness.Bulb.DropConnections();

            await connected.WaitForCountAsync(2, "the events about the loss and about the new connection");
            await Wait.UntilAsync(() => harness.Device.IsConnected, "the device to be connected");
            Assert.Equal([false, true], connected.Items.Take(2));
            Assert.Empty(harness.Bulb.RequestsOf("get_prop"));
        }

        [Fact]
        public async Task WithoutRefreshOnConnect_NoPropertiesAreReadAfterConnecting_NotEvenAfterAReconnect()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false));
            Assert.Empty(harness.Bulb.RequestsOf("get_prop"));

            harness.Bulb.DropConnections();
            await harness.Log.WaitUntilAsync(_ => Changes(harness) == 3, "the loss and the new connection");

            await Wait.NotHappensAsync(() => harness.Bulb.RequestsOf("get_prop").Count > 0, "a read of the properties");
            Assert.Equal(2, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task WithoutAutoConnect_TheLightIsListed_ButNotConnected_UntilAConnectIsAsked()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, AutoConnect = false, RefreshOnConnect = false }, searchTarget: bulb);

            var device = await client.ProbeAsync(IPAddress.Loopback);

            Assert.NotNull(device);
            Assert.Single(client.Devices, device);
            Assert.False(device.IsConnected);
            await Wait.NotHappensAsync(() => bulb.ConnectionCount > 0, "a connection to a light that was not asked for");
            await device.ConnectAsync();
            Assert.True(device.IsConnected);
            await Wait.UntilAsync(() => bulb.ClientCount == 1, "the light to have accepted the connection");
            Assert.Equal(1, bulb.ConnectionCount);
        }

        [Fact]
        public async Task WithoutAutoConnect_ACommandMakesTheConnection()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, AutoConnect = false }, searchTarget: bulb);
            var device = await client.ProbeAsync(IPAddress.Loopback) ?? throw new InvalidOperationException("The light was not found.");
            Assert.False(device.IsConnected);

            await device.SetNameAsync("by a command");

            Assert.True(device.IsConnected);
            Assert.Equal("by a command", bulb.Name);
            Assert.Equal(1, bulb.ConnectionCount);
            await Wait.UntilAsync(() => bulb.RequestsOf("get_prop").Count == 1, "the properties to be read after the connection that the command made");
        }

        [Fact]
        public async Task DisconnectAsync_ClosesTheConnection_AndTheNextCommandMakesANewOne()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false));
            Assert.Equal(1, Changes(harness));

            await harness.Device.DisconnectAsync();
            await harness.Device.DisconnectAsync();

            Assert.False(harness.Device.IsConnected);
            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the light to have seen the connection close");
            await harness.Log.WaitUntilAsync(_ => Changes(harness) == 2, "the event about the connection that was closed");
            await Wait.NotHappensAsync(() => harness.Bulb.ConnectionCount > 1, "a connection that nobody asked for", TimeSpan.FromMilliseconds(800));

            await harness.Device.SetNameAsync("again");

            Assert.True(harness.Device.IsConnected);
            Assert.Equal(2, harness.Bulb.ConnectionCount);
            Assert.Equal("again", harness.Bulb.Name);
        }

        [Fact]
        public async Task ConnectAsync_ToALightThatIsNotThere_FailsWithAMessageThatMentionsLanControl()
        {
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, ConnectTimeout = TimeSpan.FromMilliseconds(400) });
            var port = FreePort.Tcp();
            var device = client.Apply(Announcements.Of("0x1", Announcements.LocationOf(port))) ?? throw new InvalidOperationException("The light was not taken over.");

            var exception = await Assert.ThrowsAsync<YeelightException>(() => device.ConnectAsync());

            Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind);
            Assert.Contains("LAN Control", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"127.0.0.1:{port}", exception.Message, StringComparison.Ordinal);
            Assert.False(device.IsConnected);
        }

        [Fact]
        public async Task TheConnectionOfALightThatComesBackLater_IsMadeBySelf()
        {
            await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false });
            var port = FreePort.Tcp();
            var device = client.Apply(Announcements.Of("0x1", Announcements.LocationOf(port))) ?? throw new InvalidOperationException("The light was not taken over.");
            await Wait.NotHappensAsync(() => device.IsConnected, "a connection to a light that is not there");

            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { ControlPort = port });

            await Wait.UntilAsync(() => device.IsConnected, "the device to find the light that came up", TimeSpan.FromSeconds(20));
            await device.SetNameAsync("found");
            Assert.Equal("found", bulb.Name);
        }

        [Fact]
        public async Task ALightThatGotAnotherAddress_IsFollowedByTheSameDevice_OnceTheOldConnectionIsGone()
        {
            await using var harness = await Harness.StartAsync();
            await using var moved = new SimulatedBulb();
            var device = harness.Device;
            await Wait.UntilAsync(() => harness.Bulb.ClientCount >= 1, "the light to have registered the connection");
            await harness.Bulb.DisposeAsync();
            await Wait.UntilAsync(() => !device.IsConnected, "the loss of the connection to be noticed");

            var again = harness.Client.Apply(Announcements.From(moved));

            Assert.Same(device, again);
            Assert.Same(device, harness.Client.FindDevice(moved.Id));
            Assert.Equal(moved.Location, device.Location);
            Assert.Equal(moved.ControlPort, device.Port);
            await Wait.UntilAsync(() => device.IsConnected && moved.ClientCount == 1, "the device to connect to the new address", TimeSpan.FromSeconds(10));
            await device.SetNameAsync("at the new address");
            Assert.Equal("at the new address", moved.Name);
            await Wait.UntilAsync(() => moved.RequestsOf("get_prop").Count == 1, "the properties to be read after the move");
            Assert.Single(harness.Client.Devices);
        }

        [Fact]
        public async Task AnAnnouncementThatMovesALightThatIsConnected_IsIgnored_AndLoggedOnce()
        {
            // The id of a light is public: anyone on the network can announce it from its own address, and would take the light over this way.
            await using var harness = await Harness.StartAsync();
            await using var neighbour = new SimulatedBulb();
            var device = harness.Device;
            var spoof = Announcements.Of(harness.Bulb.Id, neighbour.Location, ("name", "hijacked"), ("model", "evil"), ("bright", "1"));

            for (var times = 0; times < 4; times++)
            {
                harness.Client.Apply(spoof);
            }

            Assert.Equal(harness.Bulb.Location, device.Location);
            Assert.Equal(harness.Bulb.ControlPort, device.Port);
            Assert.Equal("color", device.Model);
            Assert.Equal("my_bulb", device.Name);
            Assert.Equal(100, device.Brightness);
            await Wait.NotHappensAsync(() => neighbour.ConnectionCount > 0, "a connection to the address of the neighbour");
            Assert.True(device.IsConnected);
            Assert.Equal(1, harness.Bulb.ClientCount);
            Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("ignored", StringComparison.Ordinal));

            harness.Client.Apply(Announcements.Of(harness.Bulb.Id, "yeelight://127.0.0.1:1"));

            Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("ignored", StringComparison.Ordinal)));
            Assert.Equal(harness.Bulb.Location, device.Location);
        }

        [Fact]
        public async Task AnAnnouncementOfTheSameLight_AtTheSameAddress_IsTakenOverWhileItIsConnected()
        {
            await using var harness = await Harness.StartAsync();

            harness.Client.Apply(Announcements.Of(harness.Bulb.Id, harness.Bulb.Location, ("name", "renamed"), ("fw_ver", "20")));

            Assert.Equal("renamed", harness.Device.Name);
            Assert.Equal("20", harness.Device.FirmwareVersion);
            Assert.True(harness.Device.IsConnected);
        }

        [Fact]
        public async Task ALightThatStopsAnswering_IsFoundOutByTheKeepAlive_AndTheConnectionIsMadeAgain()
        {
            // A light that lost power does not close its connection: only a question that is not answered shows that it is gone.
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false, keepAlive: TimeSpan.FromMilliseconds(250), commandTimeout: TimeSpan.FromMilliseconds(250)));
            harness.Bulb.ReplyOverride = request => request.Method == "get_prop" ? [] : null;

            await Wait.UntilAsync(() => harness.Bulb.ConnectionCount >= 2, "the connection to be made again after the question went unanswered", TimeSpan.FromSeconds(20));

            Assert.NotEmpty(harness.Bulb.RequestsOf("get_prop"));
            await harness.Log.WaitUntilAsync(_ => Changes(harness) >= 2, "the event about the connection that was given up");
            Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("does not answer", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheKeepAlive_LeavesAConnectionAlone_WhileTheLightAnswers()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false, keepAlive: TimeSpan.FromMilliseconds(150)));

            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("get_prop").Count >= 3, "the light to be asked a few times");

            Assert.Equal(1, harness.Bulb.ConnectionCount);
            Assert.True(harness.Device.IsConnected);
            Assert.Equal(1, Changes(harness));
        }

        [Fact]
        public async Task MessagesThatArriveOneByteAtATime_AreStillMessages()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false));
            harness.Bulb.ChunkSize = 1;

            var values = await harness.Device.GetPropertiesAsync(["power"]);
            await harness.Bulb.NotifyAsync("{\"method\":\"props\",\"params\":{\"ct\":\"3333\"}}");

            Assert.Equal("on", values["power"]);
            await Wait.UntilAsync(() => harness.Device.ColorTemperature == 3333, "the report that arrived in pieces");
            Assert.Equal(1, harness.Bulb.ConnectionCount);
        }

        [Fact]
        public async Task AFloodOfNotifications_IsTakenOver_WithoutHurtingTheConnection_OrTheCommands()
        {
            await using var harness = await EventHarness.StartAsync(options: Settings(refresh: false));
            var flood = new StringBuilder();
            for (var number = 1; number <= 5000; number++)
            {
                flood.Append("{\"method\":\"props\",\"params\":{\"bright\":\"").Append((number % 100) + 1).Append("\",\"ct\":\"").Append(1700 + (number % 4800)).Append("\"}}");
                if (number < 5000)
                {
                    flood.Append("\r\n");
                }
            }

            await harness.Bulb.NotifyAsync(flood.ToString());
            var answer = await harness.Device.GetPropertiesAsync(["power"]);

            await Wait.UntilAsync(() => harness.Device.Brightness == 1 && harness.Device.ColorTemperature == 1700 + (5000 % 4800), "the last notification to be taken over");
            Assert.Equal("on", answer["power"]);
            Assert.Equal(1, harness.Bulb.ConnectionCount);
            Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level >= LogLevel.Warning);
        }

        [Fact]
        public async Task ALightThatClosesEveryConnectionAtOnce_DoesNotMakeTheClientSpin_AndIsConnectedWhenItStops()
        {
            await using var bulb = new SimulatedBulb { CloseImmediately = true };
            await using var connection = Connections.To(bulb.ControlPort);
            connection.Start();

            await Task.Delay(1000);
            var attempts = bulb.ConnectionCount;

            Assert.InRange(attempts, 3, 60);
            bulb.CloseImmediately = false;
            await Wait.UntilAsync(() => connection.IsConnected && bulb.ClientCount == 1, "the connection to be made once the light stops closing it");
            Assert.True((await connection.SendAsync("get_prop", ["power"])).Success);
        }

        [Fact]
        public async Task TheLightsLimitOfFourConnections_IsMetByTheFifth_WhichKeepsTryingUntilThereIsRoom()
        {
            await using var bulb = new SimulatedBulb();
            var first = new List<YeelightConnection>();
            try
            {
                for (var number = 0; number < 4; number++)
                {
                    first.Add(await Connections.ConnectedAsync(bulb.ControlPort));
                }

                await Wait.UntilAsync(() => bulb.ClientCount == 4, "the light to have four connections");
                await using var fifth = Connections.To(bulb.ControlPort);
                fifth.Start();

                await Wait.UntilAsync(() => bulb.ConnectionCount >= 4 + 5, "the fifth connection to try again and again");
                Assert.Equal(4, bulb.ClientCount);
                var losses = new HitCounter();
                fifth.ConnectedChanged += connected =>
                {
                    if (!connected)
                    {
                        losses.Increment();
                    }
                };
                await Wait.UntilAsync(() => losses.Value >= 1, "the fifth connection to be closed by the light again");

                await first[0].DisposeAsync();
                await Wait.UntilAsync(() => fifth.IsConnected && bulb.ClientCount == 4, "the fifth connection to take the place of the first");
                Assert.True((await fifth.SendAsync("get_prop", ["power"])).Success);
            }
            finally
            {
                foreach (var connection in first)
                {
                    await connection.DisposeAsync();
                }
            }
        }

        [Fact]
        public async Task ADisposedClient_ClosesEveryConnection_AndFailsTheCommandsThatWait_AtOnce()
        {
            await using var harness = await Harness.StartAsync(options: port => new YeelightClientOptions { DiscoveryPort = port, RefreshOnConnect = false, CommandTimeout = TimeSpan.FromSeconds(30) });
            harness.Bulb.Delay = request => request.Method == "toggle" ? TimeSpan.FromSeconds(20) : TimeSpan.Zero;
            var waiting = harness.Device.SendCommandAsync("toggle");
            await Wait.UntilAsync(() => harness.Bulb.RequestsOf("toggle").Count == 1, "the command to arrive at the light");
            var watch = Stopwatch.StartNew();

            await harness.Client.DisposeAsync();
            var result = await waiting.WithTimeoutAsync("the command that waited to end with the client", TimeSpan.FromSeconds(8));

            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.NotConnected, result.Error!.Kind);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8));
            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the light to have seen the connection close");
            Assert.False(harness.Device.IsConnected);
            Assert.Empty(harness.Client.Devices);
        }

        [Fact]
        public async Task ADisposedClient_CanBeDisposedAgain_AndEverythingThatNeedsItRefusesInTheWayThatItsDocumentationSays()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            var client = harness.Client;

            client.Dispose();
            client.Dispose();
            await client.DisposeAsync();
            await client.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => client.StartDiscovery());
            Assert.Throws<ObjectDisposedException>(() => client.StartListening());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DiscoverAsync(TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProbeAsync(IPAddress.Loopback));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromSeconds(1)));
            client.StopDiscovery();
            client.StopListening();
            client.SearchNow();
            Assert.Null(client.FindDevice(device.Id));
            Assert.Empty(client.Devices);
            Assert.Null(client.Apply(Announcements.From(harness.Bulb)));
            Assert.False(await client.RemoveDeviceAsync(device));
            var exception = await Assert.ThrowsAsync<YeelightException>(() => device.ToggleAsync());
            Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind);
            await Assert.ThrowsAsync<YeelightException>(() => device.ConnectAsync());
            Assert.False(device.IsConnected);
            await device.DisconnectAsync();
        }

        [Fact]
        public async Task ADisposeThatRacesTheConnect_NeverLeavesAConnectionOpen()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { MaxConnections = 1000 });
            var random = new Random(5);

            for (var round = 0; round < 120; round++)
            {
                var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = round % 2 == 0 });
                client.Apply(Announcements.From(bulb));
                var pause = random.Next(0, 4);
                if (pause > 0)
                {
                    await Task.Delay(pause);
                }

                await client.DisposeAsync();
            }

            await Wait.UntilAsync(() => bulb.ClientCount == 0, "the light to see every connection closed", TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task ADeviceThatIsRemovedWhileItConnects_NeverLeavesAConnectionOpen()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { MaxConnections = 1000 });
            await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false });

            for (var round = 0; round < 120; round++)
            {
                var device = client.Apply(Announcements.From(bulb)) ?? throw new InvalidOperationException("The light was not taken over.");
                if (round % 3 == 0)
                {
                    await Task.Delay(2);
                }

                Assert.True(await client.RemoveDeviceAsync(device));
            }

            await Wait.UntilAsync(() => bulb.ClientCount == 0, "the light to see every connection closed", TimeSpan.FromSeconds(20));
            Assert.Empty(client.Devices);
        }

        [Fact]
        public async Task TheConnectionOfAConnection_ReportsItsChanges_StrictlyOneAfterTheOther()
        {
            await using var bulb = new SimulatedBulb();
            var changes = new List<bool>();
            var connection = Connections.To(bulb.ControlPort);
            connection.ConnectedChanged += connected =>
            {
                lock (changes)
                {
                    changes.Add(connected);
                }
            };
            await using (connection)
            {
                connection.Start();
                for (var drop = 0; drop < 6; drop++)
                {
                    var expected = (2 * drop) + 1;
                    await Wait.UntilAsync(() => Snapshot(changes).Count >= expected && connection.IsConnected && bulb.ClientCount == 1, $"connection {drop + 1}");
                    bulb.DropConnections();
                }

                await Wait.UntilAsync(() => Snapshot(changes).Count >= 13 && connection.IsConnected, "the last connection");
            }

            var seen = Snapshot(changes);
            for (var index = 0; index < seen.Count; index++)
            {
                Assert.Equal(index % 2 == 0, seen[index]);
            }
        }

        [Fact]
        public async Task TheNumbersOfTheCommands_NeverRepeat_OnAConnectionThatIsMadeAgainAndAgain()
        {
            await using var server = new ScriptedServer();
            await using var connection = Connections.To(server.Port);
            var changes = new List<bool>();
            connection.ConnectedChanged += connected =>
            {
                lock (changes)
                {
                    changes.Add(connected);
                }
            };
            connection.Start();

            for (var round = 0; round < 8; round++)
            {
                await Wait.UntilAsync(() => Snapshot(changes).Count >= (2 * round) + 1 && connection.IsConnected && server.Open == 1, $"connection {round + 1}");
                for (var command = 0; command < 10; command++)
                {
                    Assert.True((await connection.SendAsync("get_prop", ["power"])).Success);
                }

                server.Drop();

                // The events are counted: with the short wait of these settings a poll can miss the moment that the connection is down.
                await Wait.UntilAsync(() => Snapshot(changes).Count >= (2 * round) + 2, "the loss to be noticed");
            }

            var ids = server.Requests.Select(request => request.Id).ToArray();
            Assert.Equal(80, ids.Length);
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }

        [Fact]
        public async Task TheAddressOfThisComputer_IsAnIPv4AddressWhenTheLightIs()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort);

            Assert.Equal(IPAddress.Loopback, connection.LocalAddress);
            Assert.False(connection.LocalAddress!.IsIPv4MappedToIPv6);
        }

        [Fact]
        public async Task UpdateEndpoint_MovesAConnectionToAnotherLight_AndToTheSameAddressChangesNothing()
        {
            await using var first = new SimulatedBulb();
            await using var second = new SimulatedBulb();
            await using var connection = await Connections.ConnectedAsync(first.ControlPort);
            await Wait.UntilAsync(() => first.ClientCount == 1, "the first light to have accepted the connection");

            connection.UpdateEndpoint("127.0.0.1", first.ControlPort);
            await Wait.NotHappensAsync(() => first.ConnectionCount > 1 || !connection.IsConnected, "a new connection for the same address");
            connection.UpdateEndpoint("127.0.0.1", second.ControlPort);

            await Wait.UntilAsync(() => second.ClientCount == 1 && connection.IsConnected && first.ClientCount == 0, "the connection to move to the second light");
            Assert.True((await connection.SendAsync("toggle", null)).Success);
            Assert.Single(second.RequestsOf("toggle"));
            Assert.Empty(first.RequestsOf("toggle"));
            Assert.Equal($"127.0.0.1:{second.ControlPort}", connection.Endpoint);
        }

        [Fact]
        public async Task AConnection_StartsByItselfWithTheFirstCommand_AndRefusesEverythingOnceItIsDisposed()
        {
            await using var bulb = new SimulatedBulb();
            var connection = Connections.To(bulb.ControlPort);

            var first = await connection.SendAsync("toggle", null);
            await connection.DisposeAsync();
            await connection.DisposeAsync();
            var after = await connection.SendAsync("toggle", null);
            var waited = await connection.WaitConnectedAsync(TimeSpan.FromMilliseconds(200));

            Assert.True(first.Success);
            Assert.False(after.Success);
            Assert.Equal(YeelightFailureKind.NotConnected, after.Error!.Kind);
            Assert.False(waited);
            Assert.False(connection.IsConnected);
            await Wait.UntilAsync(() => bulb.ClientCount == 0, "the light to have seen the connection close");
            Assert.Single(bulb.RequestsOf("toggle"));
        }

        [Fact]
        public async Task AConnectionThatCannotBeMade_FailsItsCommandsAsNotConnected_AfterTheWait()
        {
            await using var connection = Connections.To(FreePort.Tcp(), Connections.Fast(connectionWait: TimeSpan.FromMilliseconds(300)));

            var watch = Stopwatch.StartNew();
            var result = await connection.SendAsync("toggle", null);

            Assert.Equal(YeelightFailureKind.NotConnected, result.Error!.Kind);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250));
            Assert.False(await connection.WaitConnectedAsync(TimeSpan.FromMilliseconds(100)));
        }

        [Fact]
        public async Task ACommandThatWaitsForAConnection_GetsItWhenTheLightComesUp()
        {
            var port = FreePort.Tcp();
            await using var connection = Connections.To(port, Connections.Fast(connectionWait: TimeSpan.FromSeconds(20)));
            connection.Start();
            var sent = connection.SendAsync("toggle", null);
            await Wait.NotHappensAsync(() => sent.IsCompleted, "an answer from a light that is not there");

            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { ControlPort = port });
            var result = await sent.WithTimeoutAsync("the command to be sent when the light came up");

            Assert.True(result.Success);
        }

        private static List<bool> Snapshot(List<bool> changes)
        {
            lock (changes)
            {
                return [.. changes];
            }
        }
    }
}
