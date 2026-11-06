namespace Yeelight.Tests.Integration
{
    using System;
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
    /// What a light reports (the <c>props</c> notification, the announcement of a light that is found again) and what the client makes of it:
    /// the state of the lights, and the events that tell about it, which are raised one at a time and in order by a dispatcher of the client.
    /// </summary>
    public class StateAndEventsTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        // What the handler of an application does that does not use async: it blocks, which is the point of the tests that call this.
        private static void Block(Task task) => task.GetAwaiter().GetResult();

        private static void Block(ManualResetEventSlim gate) => gate.Wait(Timeout);

        // A handler that takes its time, without a sleep: the thread waits on a gate that nobody opens.
        private static void Pause(int milliseconds)
        {
            using var gate = new ManualResetEventSlim(false);
            gate.Wait(milliseconds);
        }

        private static string Props(string members) => "{\"method\":\"props\",\"params\":{" + members + "}}";

        // A change after the one that is looked at, which is only raised when everything before it was dealt with: nothing is raised for what is
        // not raised, and waiting for it is the only way to know without sleeping.
        private static Task SentinelAsync(EventHarness harness) => harness.ChangeAndWaitAsync("name", "sentinel-" + Guid.NewGuid().ToString("N"), nameof(YeelightDevice.Name));

        [Fact]
        public async Task Props_WithTextValues_ChangeTheState_AndRaiseOneEventPerChange_InOrder()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"power\":\"off\",\"bright\":\"45\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Brightness)) == 1, "the event about the brightness");
            Assert.False(harness.Device.IsOn);
            Assert.Equal(45, harness.Device.Brightness);
            Assert.Equal(["IsConnected", "IsOn", "Brightness"], harness.Log.PropertiesOf(harness.Device));
            Assert.Equal(["IsConnected", "IsOn", "Brightness"], harness.Log.StatesOf(harness.Device));
        }

        [Fact]
        public async Task Props_WithNumbers_ChangeTheState_JustLikeProps_WithText()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"bright\":45,\"ct\":4500,\"rgb\":255,\"hue\":120,\"sat\":50,\"color_mode\":3"));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.ColorMode)) == 1, "the event about the mode");
            var device = harness.Device;
            Assert.Equal(45, device.Brightness);
            Assert.Equal(4500, device.ColorTemperature);
            Assert.Equal(255, device.Rgb);
            Assert.Equal(120, device.Hue);
            Assert.Equal(50, device.Saturation);
            Assert.Equal(ColorMode.Hsv, device.ColorMode);
            Assert.Equal(["IsConnected", "Brightness", "ColorTemperature", "Rgb", "Color", "Hue", "Saturation", "ColorMode"], harness.Log.PropertiesOf(device));
        }

        [Fact]
        public async Task Props_ThatAreNotValues_ChangeNothingAndRaiseNothing_AndTheConnectionGoesOn()
        {
            await using var harness = await EventHarness.StartAsync();
            var device = harness.Device;
            var garbage = new[]
            {
                "\"bright\":\"abc\",\"ct\":{\"x\":1},\"power\":[\"on\"],\"hue\":null,\"sat\":true,\"rgb\":16777216,\"color_mode\":0,\"flowing\":\"2\"",
                "\"bright\":101,\"bright\":-1,\"hue\":360,\"sat\":101,\"ct\":-1,\"color_mode\":4,\"rgb\":-1",
                "\"bright\":50.5,\"ct\":1e3,\"hue\":\"\",\"sat\":\"1 2\",\"power\":\"maybe\",\"power\":\"ON\",\"flowing\":true",
                "\"bright\":99999999999999999999,\"ct\":\"99999999999999999999\",\"rgb\":\"0x10\",\"nl_br\":101,\"active_mode\":2,\"delayoff\":-5,\"music_on\":\"2\"",
            };

            foreach (var members in garbage)
            {
                await harness.Bulb.NotifyAsync(Props(members));
            }

            await SentinelAsync(harness);
            Assert.True(device.IsOn);
            Assert.Equal(100, device.Brightness);
            Assert.Equal(4000, device.ColorTemperature);
            Assert.Equal(16711680, device.Rgb);
            Assert.Equal(100, device.Hue);
            Assert.Equal(35, device.Saturation);
            Assert.Equal(ColorMode.ColorTemperature, device.ColorMode);
            Assert.False(device.IsFlowing);
            Assert.Equal(["IsConnected", "Name"], harness.Log.PropertiesOf(device));
            Assert.Equal(1, harness.Bulb.ConnectionCount);
            Assert.True(device.IsConnected);
        }

        [Fact]
        public async Task Props_WithKeysThatAreNotKnown_AreIgnored_AndTheKnownOnesAreTakenOver()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"foo\":\"bar\",\"power\":\"off\",\"bg_unknown\":\"1\",\"\":\"x\",\"POWER\":\"on\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.IsOn)) == 1, "the event about the power");
            await SentinelAsync(harness);
            Assert.False(harness.Device.IsOn);
            Assert.Equal(["IsConnected", "IsOn", "Name"], harness.Log.PropertiesOf(harness.Device));
        }

        [Fact]
        public async Task Props_OfTheDevice_AreTakenOverToo()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"name\":\"desk\",\"delayoff\":\"30\",\"music_on\":\"1\",\"nl_br\":\"20\",\"active_mode\":\"1\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightDevice.ActiveMode)) == 1, "the event about the mode of the ceiling light");
            var device = harness.Device;
            Assert.Equal("desk", device.Name);
            Assert.Equal(TimeSpan.FromMinutes(30), device.SleepTimerRemaining);
            Assert.True(device.IsMusicModeOn);
            Assert.Equal(20, device.NightLightBrightness);
            Assert.Equal(ActiveMode.Moonlight, device.ActiveMode);
            Assert.Equal(["IsConnected", "Name", "SleepTimerRemaining", "IsMusicModeOn", "NightLightBrightness", "ActiveMode"], harness.Log.PropertiesOf(device));
        }

        [Fact]
        public async Task Props_OfTheBackgroundLight_ChangeItAndNotTheMainLight()
        {
            await using var harness = await EventHarness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            var device = harness.Device;

            await harness.Bulb.NotifyAsync(Props(
                "\"bg_power\":\"on\",\"bg_bright\":\"30\",\"bg_lmode\":\"3\",\"bg_hue\":\"12\",\"bg_sat\":\"34\",\"bg_rgb\":\"65535\",\"bg_ct\":\"4200\",\"bg_flowing\":\"1\",\"bg_flow_params\":\"1,0,500,1,255,50\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.FlowParameters)) == 1, "the event about the flow of the background light");
            var background = device.Background;
            Assert.True(background.IsOn);
            Assert.Equal(30, background.Brightness);
            Assert.Equal(ColorMode.Hsv, background.ColorMode);
            Assert.Equal(12, background.Hue);
            Assert.Equal(34, background.Saturation);
            Assert.Equal(65535, background.Rgb);
            Assert.Equal(4200, background.ColorTemperature);
            Assert.True(background.IsFlowing);
            Assert.Equal("1,0,500,1,255,50", background.FlowParameters);
            string[] expected = ["IsOn", "Brightness", "ColorMode", "Hue", "Saturation", "Rgb", "Color", "ColorTemperature", "IsFlowing", "FlowParameters"];
            Assert.Equal(expected, harness.Log.PropertiesOf(background));
            Assert.Equal(expected, harness.Log.StatesOf(background));
            Assert.Equal(["IsConnected"], harness.Log.PropertiesOf(device));
            Assert.True(device.IsOn);
            Assert.Equal(100, device.Brightness);
        }

        [Fact]
        public async Task TheColourOfALight_RaisesRgbAndThenColor_OnceEach_AndNothingWhenItDoesNotChange()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"rgb\":255"));
            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Color)) == 1, "the first event about the colour");
            await harness.Bulb.NotifyAsync(Props("\"rgb\":255"));
            await harness.Bulb.NotifyAsync(Props("\"rgb\":\"255\""));
            await SentinelAsync(harness);

            Assert.Equal(["IsConnected", "Rgb", "Color", "Name"], harness.Log.PropertiesOf(harness.Device));
            Assert.Equal(Color(0, 0, 255), harness.Device.Color);

            await harness.Bulb.NotifyAsync(Props("\"rgb\":256"));
            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Color)) == 2, "the second event about the colour");
            Assert.Equal(["IsConnected", "Rgb", "Color", "Name", "Rgb", "Color"], harness.Log.PropertiesOf(harness.Device));
        }

        private static System.Drawing.Color Color(int red, int green, int blue) => System.Drawing.Color.FromArgb(255, red, green, blue);

        [Fact]
        public async Task WhatTheLightReportsAgain_WithoutChange_RaisesNothing()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"power\":\"on\",\"bright\":\"100\",\"ct\":\"4000\",\"color_mode\":\"2\",\"name\":\"my_bulb\""));
            await harness.Bulb.NotifyAsync(Props("\"power\":\"on\""));
            await SentinelAsync(harness);

            Assert.Equal(["IsConnected", "Name"], harness.Log.PropertiesOf(harness.Device));
            Assert.Equal(["IsConnected", "Name"], harness.Log.StatesOf(harness.Device));
        }

        [Fact]
        public async Task EveryChangeBack_AndForth_IsReported_WhenTheHandlerKeepsUp()
        {
            await using var harness = await EventHarness.StartAsync();
            var expected = 0;

            foreach (var power in new[] { "off", "on", "off", "on" })
            {
                expected++;
                await harness.Bulb.NotifyAsync(Props($"\"power\":\"{power}\""));
                await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.IsOn)) == expected, $"event {expected} about the power");
            }

            Assert.Equal(["IsConnected", "IsOn", "IsOn", "IsOn", "IsOn"], harness.Log.PropertiesOf(harness.Device));
            Assert.True(harness.Device.IsOn);
        }

        [Fact]
        public async Task SeveralMessagesInOneSegment_AreAllTakenOver_AndTheLatestValueIsReported()
        {
            await using var harness = await EventHarness.StartAsync();
            var seen = new EventRecorder<int>();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    seen.Add(harness.Device.Brightness);
                }
            };

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\"") + "\r\n" + Props("\"bright\":\"20\"") + "\r\n" + Props("\"bright\":\"30\""));

            await seen.WaitUntilAsync(values => values.Count > 0 && values[^1] == 30, "the event that carries the last brightness");
            await SentinelAsync(harness);

            var values = seen.Items;
            Assert.Equal(30, harness.Device.Brightness);
            Assert.InRange(values.Count, 1, 3);
            Assert.Equal(values.Order(), values);
        }

        [Fact]
        public async Task DeviceStateChanged_SaysWhichLightChanged_TheMainLight_TheBackgroundLight_OrTheDevice()
        {
            await using var harness = await EventHarness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            var device = harness.Device;

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\",\"bg_bright\":\"20\",\"name\":\"n\",\"bg_power\":\"on\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.IsOn)) == 1, "the last event");
            var states = harness.Log.Events.Where(e => e.Kind == "State" && e.Property != nameof(YeelightDevice.IsConnected)).ToList();
            Assert.Equal(["Brightness", "Brightness", "Name", "IsOn"], states.Select(e => e.Property));
            Assert.All(states, e => Assert.Same(device, e.Device));
            Assert.Same(device, states[0].Light);
            Assert.Same(device.Background, states[1].Light);
            Assert.Same(device, states[2].Light);
            Assert.Same(device.Background, states[3].Light);
        }

        [Fact]
        public async Task TheEventsOfTwoDevices_AreKeptApart_InOneOrder()
        {
            await using var harness = await EventHarness.StartAsync();
            await using var second = new SimulatedBulb(new SimulatedBulbOptions { Id = "0x0000000000000002" });
            var other = harness.Client.Apply(Announcements.From(second)) ?? throw new InvalidOperationException("The second light was not taken over.");
            await harness.Log.WaitUntilAsync(_ => harness.Log.StatesOf(other).Contains(nameof(YeelightDevice.IsConnected)), "the second device to be connected");
            await Wait.UntilAsync(() => second.ClientCount >= 1, "the second light to have registered the connection");

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"11\""));
            await second.NotifyAsync(Props("\"bright\":\"22\""));
            await harness.Log.WaitUntilAsync(_ => harness.Log.StatesOf(harness.Device).Contains("Brightness") && harness.Log.StatesOf(other).Contains("Brightness"), "the first event of each device");
            await harness.Bulb.NotifyAsync(Props("\"bright\":\"12\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.StatesOf(harness.Device).Count(name => name == "Brightness") == 2, "the second event of the first device");
            Assert.Equal(12, harness.Device.Brightness);
            Assert.Equal(22, other.Brightness);
            Assert.Equal(1, harness.Log.StatesOf(other).Count(name => name == "Brightness"));
        }

        [Fact]
        public async Task DeviceDiscovered_IsRaisedOnce_ForALight_AndTheLightIsInDevicesByThen()
        {
            var seen = new EventRecorder<(bool InDevices, bool Found, int Count)>();
            await using var harness = await EventHarness.StartAsync(configure: client => client.DeviceDiscovered += (_, e) =>
                seen.Add((client.Devices.Contains(e.Device), ReferenceEquals(client.FindDevice(e.Device.Id), e.Device), client.Devices.Count)));

            await harness.Client.ProbeAsync(IPAddress.Loopback);
            harness.Client.Apply(Announcements.From(harness.Bulb));
            harness.Client.Apply(Announcements.From(harness.Bulb, advertisement: false));
            await SentinelAsync(harness);

            Assert.Equal(1, harness.Log.CountOf("Discovered"));
            Assert.Equal((true, true, 1), Assert.Single(seen.Items));
        }

        [Fact]
        public async Task DeviceDiscovered_IsRaisedBeforeAnyEventAboutTheLight()
        {
            await using var harness = await EventHarness.StartAsync();

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));
            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Brightness)) == 1, "the event about the brightness");

            var events = harness.Log.Events;
            Assert.Equal("Discovered", events[0].Kind);
            Assert.Same(harness.Device, events[0].Device);
            Assert.All(events.Skip(1), e => Assert.NotEqual("Discovered", e.Kind));
        }

        [Fact]
        public async Task DeviceDiscovered_IsRaisedBeforeAnyStateEvent_WhenTheLightIsAnnouncedFromManyThreadsAtOnce()
        {
            // The first announcement of a light makes it and tells about it; the others only update it, and every change that they make is an
            // event. Whoever is faster, the news of the light comes first. (The window is a few microseconds wide: it takes this many lights
            // to be sure to meet it.)
            const int lights = 3000;
            const int announcers = 6;
            var logger = new ListLogger();
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, RefreshOnConnect = false, MaxDevices = lights + 10 }, logger);
            var log = new ClientEventLog(client);
            using var go = new Barrier(announcers);

            for (var light = 0; light < lights; light++)
            {
                var id = $"0x{light:x16}";
                await Task.WhenAll(Enumerable.Range(0, announcers).Select(announcer => Task.Run(() =>
                {
                    var announcement = Announcements.Of(id, "yeelight://127.0.0.1:55443", ("power", "on"), ("bright", (announcer + 1).ToString()));
                    go.SignalAndWait();
                    client.Apply(announcement);
                })));
            }

            var sentinel = client.Apply(Announcements.Of("0xFFFFFFFFFFFFFFFF", "yeelight://127.0.0.1:55443")) ?? throw new InvalidOperationException("The last light was not taken over.");
            await log.WaitUntilAsync(events => events.Any(e => e.Kind == "Discovered" && ReferenceEquals(e.Device, sentinel)), "the event about the light that was added last, which is raised after every event before it");
            var events = log.Events;
            var late = new List<string>();
            foreach (var light in events.Select(e => e.Device).Distinct())
            {
                var discovered = events.First(e => ReferenceEquals(e.Device, light) && e.Kind == "Discovered").Sequence;
                if (events.Any(e => ReferenceEquals(e.Device, light) && e.Kind != "Discovered" && e.Sequence < discovered))
                {
                    late.Add(light.Id);
                }
            }

            Assert.True(late.Count == 0, $"{late.Count} of {lights} lights had an event about them before DeviceDiscovered, for example {late.FirstOrDefault()}.");
            Assert.Equal(lights + 1, client.Devices.Count);
        }

        [Fact]
        public async Task TheHandlersRun_OnTheDispatcher_AndNeverInsideTheCodeThatReadsTheConnection()
        {
            var stacks = new EventRecorder<string>();
            await using var harness = await EventHarness.StartAsync(configure: client => client.DeviceDiscovered += (_, e) =>
            {
                e.Device.PropertyChanged += (_, _) => stacks.Add(new StackTrace().ToString());
                client.DeviceStateChanged += (_, _) => stacks.Add(new StackTrace().ToString());
            });

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));

            await stacks.WaitForCountAsync(4, "the handlers of IsConnected and of Brightness");
            foreach (var stack in stacks.Items)
            {
                Assert.Contains("EventDispatcher", stack, StringComparison.Ordinal);
                Assert.DoesNotContain("YeelightConnection", stack, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task AHandlerThatBlocksOnACommand_GetsTheAnswer_AndTheCommandDoesNotTimeOut()
        {
            // The thread of the handler waits for the answer, and another thread reads it: a handler that ran on the thread that reads
            // would keep the answer from ever arriving.
            await using var harness = await EventHarness.StartAsync(options: port => new YeelightClientOptions { DiscoveryPort = port, RefreshOnConnect = false, CommandTimeout = TimeSpan.FromSeconds(20) });
            var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watch = new Stopwatch();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(YeelightLight.Brightness))
                {
                    return;
                }

                watch.Start();
                try
                {
                    Block(harness.Device.SetNameAsync("set by a handler"));
                    outcome.TrySetResult("answered");
                }
                catch (Exception exception)
                {
                    outcome.TrySetResult(exception.GetType().Name + ": " + exception.Message);
                }
            };

            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));

            Assert.Equal("answered", await outcome.Task.WithTimeoutAsync("the handler to get the answer to its command"));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The command took {watch.Elapsed.TotalSeconds:0.0} seconds, in a handler.");
            Assert.Equal("set by a handler", harness.Bulb.Name);
        }

        [Fact]
        public async Task ASlowHandler_DelaysTheNextEvent_ButNotTheCommands()
        {
            await using var harness = await EventHarness.StartAsync();
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    entered.TrySetResult();
                    Block(release);
                }
            };

            try
            {
                await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));
                await entered.Task.WithTimeoutAsync("the handler to be called");

                // The handler holds the dispatcher: a command is answered all the same, and the event that it causes waits behind the handler.
                await harness.Device.SetNameAsync("while the handler runs");
                await Wait.UntilAsync(() => harness.Device.Name == "while the handler runs", "the report of the new name");
                await Wait.NotHappensAsync(() => harness.Log.CountOf("State", nameof(YeelightDevice.Name)) > 0, "the event about the name while the handler holds the dispatcher");
            }
            finally
            {
                release.Set();
            }

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightDevice.Name)) == 1, "the event that waited");
            Assert.Equal(["IsConnected", "Brightness", "Name"], harness.Log.PropertiesOf(harness.Device));
        }

        [Fact]
        public async Task TheEvents_AreNeverRaisedAtTheSameTime_AndWhatAHandlerSeesNeverGoesBackInTime()
        {
            await using var harness = await EventHarness.StartAsync();
            const int count = 40;
            var running = 0;
            var overlaps = 0;
            var seen = new EventRecorder<int>();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(YeelightLight.Brightness))
                {
                    return;
                }

                if (Interlocked.Increment(ref running) > 1)
                {
                    Interlocked.Increment(ref overlaps);
                }

                // Slow enough for the changes behind this one to pile up.
                Pause(2);
                seen.Add(harness.Device.Brightness);
                Interlocked.Decrement(ref running);
            };

            for (var level = 1; level <= count; level++)
            {
                await harness.Bulb.NotifyAsync(Props($"\"bright\":\"{level}\""));
            }

            await seen.WaitUntilAsync(values => values.Count > 0 && values[^1] == count, "the event that carries the last brightness");

            // The handler can still be called for a change that came while it ran: the sentinel ends only after the last of them.
            await SentinelAsync(harness);
            var values = seen.Items;
            Assert.Equal(0, Volatile.Read(ref overlaps));
            Assert.Equal(values.Order(), values);
            Assert.Equal(count, harness.Device.Brightness);
        }

        [Fact]
        public async Task AHandlerThatIsSlowerThanTheLight_SeesEveryPropertyThatChanged_WithItsLatestValue_WhateverTheLightSends()
        {
            // The notification only names the property: one that waits already covers the changes that come after it, so the queue
            // stays as small as the number of properties, however much a light says while a handler is busy.
            await using var harness = await EventHarness.StartAsync();
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var seen = new EventRecorder<(string Property, int Value)>();
            harness.Device.PropertyChanged += (_, e) =>
            {
                var device = harness.Device;
                switch (e.PropertyName)
                {
                    case nameof(YeelightLight.Hue):
                        entered.TrySetResult();
                        Block(release);
                        seen.Add((e.PropertyName, device.Hue));
                        break;
                    case nameof(YeelightLight.Brightness):
                        seen.Add((e.PropertyName, device.Brightness));
                        break;
                    case nameof(YeelightLight.ColorTemperature):
                        seen.Add((e.PropertyName, device.ColorTemperature));
                        break;
                }
            };

            try
            {
                await harness.Bulb.NotifyAsync(Props("\"hue\":\"1\""));
                await entered.Task.WithTimeoutAsync("the handler to be called");
                for (var level = 1; level <= 300; level++)
                {
                    await harness.Bulb.NotifyAsync(Props($"\"bright\":\"{(level % 99) + 1}\",\"ct\":\"{2000 + level}\""));
                }

                await Wait.UntilAsync(() => harness.Device.ColorTemperature == 2300, "the state to follow the light while the handler holds the dispatcher");
            }
            finally
            {
                release.Set();
            }

            await seen.WaitUntilAsync(values => values.Contains(("Brightness", 1 + (300 % 99))) && values.Contains(("ColorTemperature", 2300)), "an event for each property, with its latest value");
            Assert.Equal(("Hue", 1), seen.Items[0]);
            Assert.True(seen.Items.Count(entry => entry.Property == "Brightness") <= 2, "The changes of the brightness that came while the handler was busy were not put together.");
            Assert.True(seen.Items.Count(entry => entry.Property == "ColorTemperature") <= 2, "The changes of the colour temperature that came while the handler was busy were not put together.");
        }

        [Fact]
        public async Task AHandlerThatThrows_KeepsNeitherTheOtherHandlersNorTheNextEventsFromBeingRaised()
        {
            await using var harness = await EventHarness.StartAsync();
            var first = new HitCounter();
            var behindIt = new HitCounter();
            var state = new HitCounter();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.IsOn))
                {
                    first.Increment();
                    throw new InvalidOperationException("handler failure of PropertyChanged");
                }
            };
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.IsOn))
                {
                    behindIt.Increment();
                }
            };
            harness.Client.DeviceStateChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.IsOn))
                {
                    state.Increment();
                    throw new InvalidOperationException("handler failure of DeviceStateChanged");
                }
            };

            await harness.Bulb.NotifyAsync(Props("\"power\":\"off\""));
            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Brightness)) == 1, "the event behind the handlers that threw");
            Assert.Equal(1, first.Value);
            Assert.Equal(1, behindIt.Value);
            Assert.Equal(1, state.Value);
            Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException));
            Assert.True(harness.Device.IsConnected);
        }

        [Fact]
        public async Task AfterTheClientIsDisposed_NothingMoreIsRaised_AndADisposeDoesNotWaitForAHandlerThatRuns()
        {
            await using var harness = await EventHarness.StartAsync();
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var raised = new EventRecorder<string>();
            harness.Device.PropertyChanged += (_, e) =>
            {
                raised.Add(e.PropertyName ?? string.Empty);
                if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    entered.TrySetResult();
                    Block(release);
                }
            };
            harness.Client.DeviceStateChanged += (_, e) => raised.Add("State:" + e.PropertyName);

            try
            {
                await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));
                await entered.Task.WithTimeoutAsync("the handler to be called");
                await harness.Bulb.NotifyAsync(Props("\"bright\":\"20\""));
                await harness.Bulb.NotifyAsync(Props("\"power\":\"off\""));
                await Wait.UntilAsync(() => !harness.Device.IsOn, "the state to follow the light");

                // Queued behind the handler that runs: dropped, and the dispose does not wait for the handler.
                await harness.Client.DisposeAsync().AsTask().WithTimeoutAsync("the dispose of the client, which does not wait for a handler");
            }
            finally
            {
                release.Set();
            }

            await Wait.NotHappensAsync(() => raised.Items.Contains("IsOn") || raised.Items.Contains("State:Brightness"), "an event after the dispose", TimeSpan.FromMilliseconds(500));
            Assert.Equal(["Brightness"], raised.Items);
        }

        [Fact]
        public async Task ANotificationThatIsUnderWay_StopsBeforeTheNextHandler_WhenTheClientIsDisposed()
        {
            await using var harness = await EventHarness.StartAsync();
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var next = new HitCounter();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    entered.TrySetResult();
                    Block(release);
                }
            };
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    next.Increment();
                }
            };

            try
            {
                await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));
                await entered.Task.WithTimeoutAsync("the handler to be called");
                await harness.Client.DisposeAsync().AsTask().WithTimeoutAsync("the dispose of the client");
            }
            finally
            {
                release.Set();
            }

            await Wait.NotHappensAsync(() => next.Value > 0, "the next handler of an event that was under way when the client was disposed", TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public async Task AHandlerThatDisposesTheClient_DoesNotDeadlock_AndNothingIsRaisedAfterIt()
        {
            await using var harness = await EventHarness.StartAsync();
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var after = new HitCounter();
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.IsOn))
                {
                    harness.Client.Dispose();
                    disposed.TrySetResult();
                }
                else if (e.PropertyName == nameof(YeelightLight.Brightness))
                {
                    after.Increment();
                }
            };

            await harness.Bulb.NotifyAsync(Props("\"power\":\"off\""));
            await disposed.Task.WithTimeoutAsync("the handler to have disposed the client");
            await harness.Bulb.NotifyAsync(Props("\"bright\":\"10\""));

            await Wait.UntilAsync(() => harness.Bulb.ClientCount == 0, "the connection of the disposed client to be closed");
            await Wait.NotHappensAsync(() => after.Value > 0, "an event after the dispose");
            Assert.Empty(harness.Client.Devices);
        }

        [Fact]
        public async Task ACommandFromInsideTheHandlerOfAnEvent_WorksAndItsOwnEventsFollow()
        {
            await using var harness = await EventHarness.StartAsync();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(YeelightLight.Brightness) && harness.Device.Brightness == 10)
                {
                    Block(harness.Device.AdjustBrightnessAsync(-5));
                    done.TrySetResult();
                }
            };

            await harness.Bulb.ChangeAsync(("bright", "10"));

            await done.Task.WithTimeoutAsync("the handler to have sent its command");
            await Wait.UntilAsync(() => harness.Device.Brightness == 5, "the report of the change that the handler made");
            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightLight.Brightness)) == 2, "the events about both changes");
        }

        [Fact]
        public async Task ALightThatIsFoundAgain_RaisesEventsOnlyForWhatChanged()
        {
            await using var harness = await EventHarness.StartAsync();
            var device = harness.Device;

            harness.Client.Apply(Announcements.From(harness.Bulb));
            harness.Client.Apply(Announcements.From(harness.Bulb, advertisement: false));
            await SentinelAsync(harness);
            Assert.Equal(["IsConnected", "Name"], harness.Log.PropertiesOf(device));

            harness.Client.Apply(Announcements.Of(
                harness.Bulb.Id,
                harness.Bulb.Location,
                ("model", "stripe"),
                ("fw_ver", "19"),
                ("support", "get_prop set_power"),
                ("power", "off"),
                ("bright", "40"),
                ("name", "renamed")));

            await harness.Log.WaitUntilAsync(_ => harness.Log.CountOf("State", nameof(YeelightDevice.Name)) == 2, "the events about the light that was found again");
            var changed = harness.Log.PropertiesOf(device);
            Assert.Equal(["IsConnected", "Name", "Model", "FirmwareVersion", "SupportedMethods"], changed.Take(5));
            Assert.Equal(["Brightness", "IsOn", "Name"], changed.Skip(5).Order(StringComparer.Ordinal));
            Assert.Equal("stripe", device.Model);
            Assert.Equal("19", device.FirmwareVersion);
            Assert.Equal(["get_prop", "set_power"], device.SupportedMethods);
            Assert.False(device.IsOn);
            Assert.Equal(40, device.Brightness);
            Assert.Equal("renamed", device.Name);
        }

        [Fact]
        public async Task ALightThatMovesToAnotherPort_RaisesTheEventsOfTheLocationAndThePort_ButNotOfTheAddress()
        {
            var bulb = new SimulatedBulb();
            await using var owner = bulb;
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, DiscoveryPort = bulb.DiscoveryPort });
            var log = new ClientEventLog(client);
            var device = client.Apply(Announcements.From(bulb)) ?? throw new InvalidOperationException("The light was not taken over.");
            await log.WaitUntilAsync(events => events.Any(e => e.Kind == "Discovered"), "the discovery");
            var before = device.Address;

            client.Apply(Announcements.Of(bulb.Id, "yeelight://127.0.0.1:12345"));

            await log.WaitUntilAsync(events => events.Any(e => e.Kind == "State" && e.Property == nameof(YeelightDevice.Port)), "the event about the port");
            Assert.Equal(12345, device.Port);
            Assert.Equal(before, device.Address);
            Assert.Equal("yeelight://127.0.0.1:12345", device.Location);
            var changed = log.PropertiesOf(device);
            Assert.Contains(nameof(YeelightDevice.Location), changed);
            Assert.DoesNotContain(nameof(YeelightDevice.Address), changed);
        }
    }
}
