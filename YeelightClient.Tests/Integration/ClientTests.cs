namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// The list of lights that a client keeps: one light for every id, however and however often it is announced, the lights that it finds and
    /// forgets, the waits for a light, the clock that says when a light was last heard, and what the client writes to its log.
    /// </summary>
    public class ClientTests
    {
        private static YeelightClient NewClient(int maxDevices = 1000, ILogger? logger = null, TimeProvider? time = null, bool autoConnect = false)
        {
            return TestClients.Create(new YeelightClientOptions { AutoConnect = autoConnect, RefreshOnConnect = false, MaxDevices = maxDevices }, logger, time: time);
        }

        private static DeviceAnnouncement Announce(string id, params (string Name, string Value)[] headers) => Announcements.Of(id, "yeelight://127.0.0.1:55443", headers);

        [Fact]
        public async Task AProbe_FindsTheLightAtAnAddress_AndTheLightIsInTheList()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, RefreshOnConnect = false }, searchTarget: bulb);

            var device = await client.ProbeAsync(IPAddress.Loopback);

            Assert.NotNull(device);
            Assert.Equal(bulb.Id, device.Id);
            Assert.Same(device, Assert.Single(client.Devices));
            Assert.Same(device, client.FindDevice(bulb.Id));
            Assert.Same(device, await client.ProbeAsync(" 127.0.0.1 "));
            Assert.Single(client.Devices);
            Assert.True(bulb.SearchCount >= 2);
        }

        [Fact]
        public async Task AProbe_RefusesWhatIsNotAnAddressOfTheLocalNetwork_WithoutSendingAnything()
        {
            await using var client = TestClients.Create(new YeelightClientOptions());

            await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProbeAsync((IPAddress)null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProbeAsync((string)null!));
            await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync("not an address"));
            await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync(IPAddress.Parse("203.0.113.9")));
            await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync("198.51.100.1"));
            await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync(IPAddress.IPv6Loopback));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ProbeAsync(IPAddress.Loopback, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ProbeAsync(IPAddress.Loopback, 65536));
            Assert.Empty(client.Devices);
        }

        [Fact]
        public async Task AProbe_ThatIsNotAnswered_GivesNull_AfterAboutThreeSeconds()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            var watch = Stopwatch.StartNew();

            var device = await client.ProbeAsync(IPAddress.Loopback);

            Assert.Null(device);
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(12));
            Assert.True(bulb.SearchCount >= 3, $"The search was sent {bulb.SearchCount} times in three seconds: it is sent again while the client waits, because UDP loses packets.");
            Assert.Empty(client.Devices);
        }

        [Fact]
        public async Task AProbe_SendsTheSearchAgainWhileItWaits_AndTakesTheAnswerThatComesLate()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, RefreshOnConnect = false }, searchTarget: bulb);

            var probing = client.ProbeAsync(IPAddress.Loopback);
            await Wait.UntilAsync(() => bulb.SearchCount >= 3, "the search to be sent three times");
            Assert.False(probing.IsCompleted);
            bulb.AnswersSearches = true;
            var device = await probing.WithTimeoutAsync("the probe to take the answer");

            Assert.NotNull(device);
            Assert.Equal(bulb.Id, device.Id);
        }

        [Fact]
        public async Task AProbe_IgnoresAnAnswerThatNamesAnotherAddress_AndTakesTheRightOne()
        {
            await using var responder = new ScriptedResponder();
            var answers = 0;
            responder.Reply = (_, _) =>
            {
                // The first answer is from a light that says that it is somewhere else; the second is right.
                var location = Interlocked.Increment(ref answers) == 1 ? "yeelight://10.9.8.7:55443" : "yeelight://127.0.0.1:55443";
                return Announcements.Message("0xABC", location, advertisement: false, ("model", "color"), ("support", Announcements.Support));
            };
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = responder.Port, AutoConnect = false });

            var device = await client.ProbeAsync(IPAddress.Loopback);

            Assert.NotNull(device);
            Assert.Equal("yeelight://127.0.0.1:55443", device.Location);
            Assert.True(responder.SearchCount >= 2);
            Assert.Single(client.Devices);
        }

        [Fact]
        public async Task ALightIsOneDevice_WhateverTheCaseOfItsId_AndAnotherAnnouncementUpdatesIt()
        {
            await using var client = NewClient();
            var log = new ClientEventLog(client);

            var first = client.Apply(Announce("0xABC", ("model", "color"), ("name", "Lamp"), ("bright", "10"), ("power", "on")));
            var again = client.Apply(Announce("0xabc", ("model", "stripe"), ("fw_ver", "99"), ("support", "get_prop toggle"), ("name", "Reading lamp"), ("bright", "70"), ("power", "off")));

            Assert.NotNull(first);
            Assert.Same(first, again);
            Assert.Single(client.Devices);
            Assert.Same(first, client.FindDevice("0xAbC"));
            Assert.Same(first, client.FindDevice("0xabc"));
            Assert.Equal("0xABC", first.Id);
            Assert.Equal("stripe", first.Model);
            Assert.Equal("99", first.FirmwareVersion);
            Assert.Equal(["get_prop", "toggle"], first.SupportedMethods);
            Assert.Equal("Reading lamp", first.Name);
            Assert.Equal(70, first.Brightness);
            Assert.False(first.IsOn);
            await log.WaitUntilAsync(events => events.Any(e => e.Kind == "State" && e.Property == nameof(YeelightLight.IsOn)), "the events about the update");
            Assert.Equal(1, log.CountOf("Discovered"));
        }

        [Fact]
        public async Task AnAnnouncementThatLeavesOutWhatTheLightSaidBefore_KeepsIt()
        {
            await using var client = NewClient();
            var first = client.Apply(Announce("0x1", ("name", "Lamp"), ("bright", "10"), ("model", "color"), ("fw_ver", "18")))!;

            client.Apply(Announcements.Parse(Announcements.Message("0x1", "yeelight://127.0.0.1:55443")));

            Assert.Equal("Lamp", first.Name);
            Assert.Equal(10, first.Brightness);
            Assert.Equal("color", first.Model);
            Assert.Equal("18", first.FirmwareVersion);
            Assert.NotEmpty(first.SupportedMethods);
        }

        [Fact]
        public async Task Devices_IsACopyThatNeverChanges_AndCannotBeChangedThroughACast()
        {
            await using var client = NewClient();
            client.Apply(Announce("0x1"));
            var before = client.Devices;

            client.Apply(Announce("0x2"));

            Assert.Single(before);
            Assert.Equal(2, client.Devices.Count);
            Assert.IsNotType<YeelightDevice[]>(client.Devices);
            Assert.Throws<NotSupportedException>(() => ((IList<YeelightDevice>)client.Devices).Add(client.Devices[0]));
            Assert.Throws<NotSupportedException>(() => ((IList<YeelightDevice>)before).Clear());
        }

        [Fact]
        public async Task TheListsThatAreGivenOut_CannotBeChangedThroughACast()
        {
            await using var client = NewClient();
            var device = client.Apply(Announce("0x1"))!;
            var flow = new ColorFlow([ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(100))]);

            Assert.IsNotType<string[]>(device.SupportedMethods);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)device.SupportedMethods)[0] = "hacked");
            Assert.Contains("get_prop", device.SupportedMethods);
            Assert.IsNotType<ColorFlowStep[]>(flow.Steps);
            Assert.Throws<NotSupportedException>(() => ((IList<ColorFlowStep>)flow.Steps).Clear());
            Assert.Single(flow.Steps);
        }

        [Fact]
        public async Task FindDevice_FindsALightByItsId_AndGivesNullForOneThatIsNotKnown()
        {
            await using var client = NewClient();
            var device = client.Apply(Announce("0x00000000AB"))!;

            Assert.Same(device, client.FindDevice("0x00000000ab"));
            Assert.Null(client.FindDevice("0x00000000ac"));
            Assert.Null(client.FindDevice(string.Empty));
            Assert.Throws<ArgumentNullException>(() => client.FindDevice(null!));
        }

        [Fact]
        public async Task TheNumberOfLights_IsBounded_AndWhatIsKnownIsStillUpdatedWhenTheListIsFull()
        {
            var logger = new ListLogger();
            await using var client = NewClient(maxDevices: 5, logger: logger);
            var log = new ClientEventLog(client);

            var results = Enumerable.Range(1, 9).Select(number => client.Apply(Announce($"0x{number}"))).ToArray();

            Assert.Equal(5, client.Devices.Count);
            Assert.All(results.Take(5), device => Assert.NotNull(device));
            Assert.All(results.Skip(5), device => Assert.Null(device));
            Assert.Null(client.FindDevice("0x6"));
            var known = client.Apply(Announce("0x3", ("name", "still updated")))!;
            Assert.Equal("still updated", known.Name);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("5 lights", StringComparison.Ordinal));
            await log.WaitUntilAsync(events => events.Count(e => e.Kind == "Discovered") >= 5, "the five discoveries");
            await Wait.NotHappensAsync(() => log.CountOf("Discovered") > 5, "a sixth discovery");

            Assert.True(await client.RemoveDeviceAsync(results[0]!));
            Assert.NotNull(client.Apply(Announce("0x6")));
            Assert.Equal(5, client.Devices.Count);
        }

        [Fact]
        public void TheLimitOnTheNumberOfLights_IsAThousandByDefault_AndAtLeastOne()
        {
            Assert.Equal(1000, new YeelightClientOptions().MaxDevices);
            Assert.Throws<ArgumentOutOfRangeException>(() => TestClients.Create(new YeelightClientOptions { MaxDevices = 0 }));
            using var one = TestClients.Create(new YeelightClientOptions { MaxDevices = 1, AutoConnect = false });
            Assert.NotNull(one.Apply(Announce("0x1")));
            Assert.Null(one.Apply(Announce("0x2")));
        }

        [Fact]
        public async Task AFloodOfAnnouncementsOfLightsThatAreNotThere_IsBounded_AndDoesNotHurtTheOnesThatAre()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { MaxDevices = 50, RefreshOnConnect = false, ConnectTimeout = TimeSpan.FromMilliseconds(300) });
            var real = client.Apply(Announcements.From(bulb))!;

            // A neighbour that announces a thousand lights, every one of them at its own address (the loopback here), where nothing listens.
            for (var number = 0; number < 1000; number++)
            {
                client.Apply(Announcements.Of($"0xBAD{number:x4}", Announcements.LocationOf(FreePort.Tcp())));
            }

            Assert.Equal(50, client.Devices.Count);
            Assert.Same(real, client.FindDevice(bulb.Id));
            await Wait.UntilAsync(() => real.IsConnected, "the real light to be connected");
            await real.SetNameAsync("still works");
        }

        [Fact]
        public async Task AnnouncementsFromManyThreads_MakeOneDevicePerLight_AndEveryoneIsToldOnce()
        {
            await using var client = NewClient();
            var log = new ClientEventLog(client);
            const int lights = 40;
            const int announcements = 20;

            await Task.WhenAll(Enumerable.Range(0, lights * announcements).Select(number => Task.Run(() =>
            {
                client.Apply(Announce($"0x{number % lights:x4}", ("bright", ((number % 100) + 1).ToString())));
            })));

            await log.WaitUntilAsync(events => events.Count(e => e.Kind == "Discovered") == lights, "the news of every light");
            Assert.Equal(lights, client.Devices.Count);
            Assert.Equal(lights, client.Devices.Select(device => device.Id).Distinct().Count());
            await Wait.NotHappensAsync(() => log.CountOf("Discovered") > lights, "a light that was told about twice");
        }

        [Fact]
        public async Task AnnouncementsThatRaceRemovals_NeverLeaveTwoLightsWithOneId_NorALightThatIsToldAboutTwice()
        {
            await using var bulb = new SimulatedBulb(new SimulatedBulbOptions { MaxConnections = 10_000 });
            await using var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false });
            var log = new ClientEventLog(client);
            var created = new ConcurrentDictionary<YeelightDevice, byte>(ReferenceEqualityComparer.Instance);
            var failures = new ConcurrentQueue<Exception>();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var ids = new[] { "0x01", "0x02", "0x03" };

            var announcers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                var random = new Random(worker);
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var id = ids[random.Next(ids.Length)];
                        var device = client.Apply(Announcements.Of(id, bulb.Location, ("bright", random.Next(1, 100).ToString())));
                        if (device is not null)
                        {
                            created.TryAdd(device, 0);
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            }));
            var removers = Enumerable.Range(0, 2).Select(worker => Task.Run(async () =>
            {
                var random = new Random(100 + worker);
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var devices = client.Devices;
                        if (devices.Count > 0)
                        {
                            await client.RemoveDeviceAsync(devices[random.Next(devices.Count)]);
                        }

                        await Task.Delay(2);
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            }));

            await Task.WhenAll(announcers.Concat(removers));

            Assert.Empty(failures);
            var listed = client.Devices;
            Assert.Equal(listed.Count, listed.Select(device => device.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            await log.WaitUntilAsync(events => events.Count(e => e.Kind == "Discovered") >= created.Count, "the news of every light that was made", TimeSpan.FromSeconds(15));
            await Wait.NotHappensAsync(() => log.CountOf("Discovered") > created.Count, "a light that was told about twice");
            Assert.Equal(created.Count, log.CountOf("Discovered"));
            foreach (var device in client.Devices.ToArray())
            {
                await client.RemoveDeviceAsync(device);
            }

            await Wait.UntilAsync(() => bulb.ClientCount == 0, "every connection of every light that was made to be closed", TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task WaitForDevice_ReturnsAtOnce_ForALightThatIsKnown_WhateverTheCaseOfItsId()
        {
            await using var client = NewClient();
            var device = client.Apply(Announce("0xABC"))!;

            var found = await client.WaitForDeviceAsync("0xabc", TimeSpan.FromMilliseconds(1));

            Assert.Same(device, found);
        }

        [Fact]
        public async Task WaitForDevice_ReturnsTheLight_WhenItIsFoundLater_AndOnlyThatOne()
        {
            await using var client = NewClient();
            var waiting = client.WaitForDeviceAsync("0xDEF", TimeSpan.FromSeconds(30));
            client.Apply(Announce("0x111"));
            await Task.Delay(100);
            Assert.False(waiting.IsCompleted);

            var device = client.Apply(Announce("0xdef"))!;

            Assert.Same(device, await waiting.WithTimeoutAsync("the wait to end when the light was found"));
        }

        [Fact]
        public async Task WaitForDevice_ThrowsTimeoutException_NamingTheLight_WhenTheLightIsNotFoundInTime()
        {
            await using var client = NewClient();
            var watch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<TimeoutException>(() => client.WaitForDeviceAsync("0xNOPE", TimeSpan.FromMilliseconds(300)));

            Assert.Contains("0xNOPE", exception.Message, StringComparison.Ordinal);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<ArgumentNullException>(() => client.WaitForDeviceAsync(null!, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task TheWaitsOfAClient_EndWithOperationCanceled_WhenTheClientIsDisposed()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            var waiting = client.WaitForDeviceAsync("0xNOPE", TimeSpan.FromSeconds(60));
            var discovering = client.DiscoverAsync(TimeSpan.FromSeconds(60));
            var probing = client.ProbeAsync(IPAddress.Loopback);
            await Wait.UntilAsync(() => bulb.SearchCount >= 2, "the searches to go out");

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WithTimeoutAsync("WaitForDeviceAsync to end with the client", TimeSpan.FromSeconds(8)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering.WithTimeoutAsync("DiscoverAsync to end with the client", TimeSpan.FromSeconds(8)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing.WithTimeoutAsync("ProbeAsync to end with the client", TimeSpan.FromSeconds(8)));
        }

        [Fact]
        public async Task RemoveDevice_ForgetsTheLight_ClosesItsConnection_AndAnswersWhetherItHadIt()
        {
            await using var harness = await Harness.StartAsync();
            var device = harness.Device;
            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 1, "the light to have accepted the connection");

            var first = await harness.Client.RemoveDeviceAsync(device);
            var second = await harness.Client.RemoveDeviceAsync(device);

            Assert.True(first);
            Assert.False(second);
            Assert.Empty(harness.Client.Devices);
            Assert.Null(harness.Client.FindDevice(device.Id));
            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the light to have seen the connection close");
            Assert.False(device.IsConnected);
            var exception = await Assert.ThrowsAsync<YeelightException>(() => device.ToggleAsync());
            Assert.Equal(YeelightFailureKind.NotConnected, exception.Kind);
            await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.RemoveDeviceAsync(null!));
        }

        [Fact]
        public async Task ALightThatWasRemoved_IsFoundAgainAsANewObject_AndToldAboutAgain()
        {
            await using var harness = await EventHarness.StartAsync();
            var old = harness.Device;
            await harness.Client.RemoveDeviceAsync(old);

            var found = await harness.Client.ProbeAsync(IPAddress.Loopback);

            Assert.NotNull(found);
            Assert.NotSame(old, found);
            Assert.Same(found, Assert.Single(harness.Client.Devices));
            await harness.Log.WaitUntilAsync(events => events.Count(e => e.Kind == "Discovered") == 2, "the news of the light that was found again");
            await found.ConnectAsync();
            await found.SetNameAsync("the new object works");
            Assert.False(old.IsConnected);
        }

        [Fact]
        public async Task ALightOfAnotherClient_IsNotRemoved_ByThisOne()
        {
            await using var first = NewClient();
            await using var second = NewClient();
            var device = first.Apply(Announce("0x1"))!;

            Assert.False(await second.RemoveDeviceAsync(device));
            Assert.Single(first.Devices);
        }

        [Fact]
        public async Task LastSeen_MovesWhenTheLightIsHeardFrom_ByAnAnnouncement_OrOnItsConnection()
        {
            var time = new ManualTimeProvider();
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, RefreshOnConnect = false }, searchTarget: bulb, time: time);
            var device = await client.ProbeAsync(IPAddress.Loopback) ?? throw new InvalidOperationException("The light was not found.");
            await device.ConnectAsync();
            var start = device.LastSeen;
            await Wait.UntilAsync(() => device.IsConnected && bulb.ClientCount >= 1, "the connection, and the light to have registered it");
            var connected = device.LastSeen;
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), connected);

            time.Advance(TimeSpan.FromMinutes(5));
            client.Apply(Announcements.From(bulb));
            Assert.Equal(connected + TimeSpan.FromMinutes(5), device.LastSeen);

            time.Advance(TimeSpan.FromMinutes(7));
            await bulb.NotifyAsync("{\"method\":\"props\",\"params\":{\"bright\":\"42\"}}");
            await Wait.UntilAsync(() => device.Brightness == 42, "the report");
            Assert.Equal(connected + TimeSpan.FromMinutes(12), device.LastSeen);
            Assert.True(device.LastSeen >= start);
        }

        [Fact]
        public async Task TheLog_HasTheMessagesThatAreSentAndReceived_WithTheIdOfTheLightAndTheJson_AtTheDebugLevel()
        {
            await using var harness = await Harness.StartAsync(options: port => new YeelightClientOptions { DiscoveryPort = port, RefreshOnConnect = false });

            await harness.Device.SetBrightnessAsync(33);

            var entries = harness.Logger.Entries;
            var sent = Assert.Single(entries, entry => entry.Message.Contains("Sending:", StringComparison.Ordinal));
            var received = Assert.Single(entries, entry => entry.Message.Contains("Received:", StringComparison.Ordinal) && entry.Message.Contains("\"result\"", StringComparison.Ordinal));
            Assert.Equal(LogLevel.Debug, sent.Level);
            Assert.Equal(LogLevel.Debug, received.Level);
            Assert.StartsWith(harness.Device.Id + ": Sending: {", sent.Message, StringComparison.Ordinal);
            Assert.Contains("\"method\":\"set_bright\",\"params\":[33,\"sudden\",0]", sent.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", sent.Message, StringComparison.Ordinal);
            Assert.StartsWith(harness.Device.Id + ": Received: {\"id\":", received.Message, StringComparison.Ordinal);
            Assert.Contains("\"result\":[\"ok\"]", received.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheLog_TellsAtTheInformationLevel_WhenALightIsFound_ConnectedAndLost()
        {
            await using var harness = await Harness.StartAsync();
            await Wait.UntilAsync(() => harness.Bulb.ClientCount >= 1, "the light to have registered the connection");

            harness.Bulb.DropConnections();
            await Wait.UntilAsync(() => harness.Logger.Entries.Any(entry => entry.Message.Contains("ended", StringComparison.Ordinal)), "the loss of the connection to be logged");

            var information = harness.Logger.Entries.Where(entry => entry.Level == LogLevel.Information).Select(entry => entry.Message).ToArray();
            Assert.Contains(information, message => message.StartsWith("Found " + harness.Device.Id, StringComparison.Ordinal) && message.Contains(harness.Bulb.Location, StringComparison.Ordinal));
            Assert.Contains(information, message => message.StartsWith(harness.Device.Id + ": Connected to 127.0.0.1:", StringComparison.Ordinal));
            Assert.Contains(information, message => message.StartsWith(harness.Device.Id + ": The connection to 127.0.0.1:", StringComparison.Ordinal) && message.EndsWith("ended.", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AClientWithoutALogger_WorksTheSame()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, logger: null, searchTarget: bulb);

            var device = await client.ProbeAsync(IPAddress.Loopback) ?? throw new InvalidOperationException("The light was not found.");
            await device.ConnectAsync();
            await device.SetNameAsync("no log");

            Assert.Equal("no log", bulb.Name);
        }

        [Fact]
        public async Task ALoggerThatThrows_DoesNotStopTheClientFromFindingALightConnectingAndSendingCommands()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, new ThrowingLogger(), searchTarget: bulb);

            var device = await client.ProbeAsync(IPAddress.Loopback);

            Assert.NotNull(device);
            await Wait.UntilAsync(() => device.IsConnected, "the connection to be made, although the log fails");
            await device.SetNameAsync("the log fails");
            Assert.Equal("the log fails", bulb.Name);
            await bulb.ChangeAsync(("bright", "21"));
            await Wait.UntilAsync(() => device.Brightness == 21, "the report, although the log fails");
            bulb.DropConnections();
            await Wait.UntilAsync(() => !device.IsConnected, "the loss to be noticed");
            await Wait.UntilAsync(() => device.IsConnected, "the connection to be made again, although the log fails", TimeSpan.FromSeconds(20));
        }

        private sealed class ThrowingLogger : ILogger<YeelightClient>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                throw new InvalidOperationException("The log cannot be written.");
            }
        }
    }
}
