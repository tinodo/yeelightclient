namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The events page of the test app: what an event is made of, how it is filtered and exported, and that the store of the events is bounded.</summary>
    public sealed class EventEntryTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "yeelight-testapp-events-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(this._folder))
            {
                Directory.Delete(this._folder, recursive: true);
            }
        }

        [Fact]
        public async Task ALightThatWasFound_IsAnEventWithItsLocation()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Name = "Desk lamp" });
            var now = DateTimeOffset.Now;

            var entry = EventEntry.Discovered(harness.Device, now);

            Assert.Equal(EventKind.DeviceDiscovered, entry.Kind);
            Assert.Equal(harness.Bulb.Id, entry.LightId);
            Assert.Equal("Desk lamp", entry.LightName);
            Assert.Equal("Device", entry.Property);
            Assert.Equal($"Desk lamp found at {harness.Bulb.Location}", entry.Value);
            Assert.Equal(entry.Value, entry.Summary);
            Assert.Equal("DeviceDiscovered", entry.KindText);
            Assert.Equal(now, entry.Time);
        }

        [Fact]
        public async Task ALightWithoutAName_IsKnownByItsModel()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Name = string.Empty, Model = "mono" });

            var entry = EventEntry.Discovered(harness.Device, DateTimeOffset.Now);

            Assert.Equal("mono", entry.LightName);
            Assert.EndsWith("243f", entry.LightText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AChange_ShowsWhatThePropertyIsWhenTheEventIsHandled()
        {
            await using var harness = await Harness.StartAsync();
            await harness.SettleAsync();
            await harness.Bulb.ChangeAsync(("bright", "35"));
            await Wait.UntilAsync(() => harness.Device.Brightness == 35, "the brightness");

            var state = EventEntry.StateChanged(harness.Device, harness.Device, "Brightness", DateTimeOffset.Now);
            var own = EventEntry.PropertyChanged(harness.Device, harness.Device, "Brightness", DateTimeOffset.Now);

            Assert.Equal(EventKind.DeviceStateChanged, state.Kind);
            Assert.Equal(EventKind.PropertyChanged, own.Kind);
            Assert.Equal("35%", state.Value);
            Assert.Equal("Brightness = 35%", state.Summary);
            Assert.False(state.IsBackground);
            Assert.Equal("main", state.Target);
        }

        [Fact]
        public async Task AChangeOfTheBackgroundLight_IsMarkedAsSuch()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            await harness.SettleAsync();
            await harness.Bulb.ChangeAsync(("bg_bright", "20"));
            await Wait.UntilAsync(() => harness.Device.Background.Brightness == 20, "the brightness of the background light");

            var entry = EventEntry.StateChanged(harness.Device, harness.Device.Background, "Brightness", DateTimeOffset.Now);

            Assert.True(entry.IsBackground);
            Assert.Equal("background", entry.Target);
            Assert.Equal("20%", entry.Value);
            Assert.Equal("background Brightness = 20%", entry.Summary);
        }

        [Fact]
        public async Task TheRealEventsOfTheClient_BecomeEntries_ThatCanAllBeShown()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            await harness.SettleAsync();
            var entries = new ConcurrentQueue<EventEntry>();
            harness.Client.DeviceStateChanged += (_, e) => entries.Enqueue(EventEntry.StateChanged(e.Device, e.Light, e.PropertyName, DateTimeOffset.Now));
            harness.Device.PropertyChanged += (_, e) => entries.Enqueue(EventEntry.PropertyChanged(harness.Device, harness.Device, e.PropertyName ?? string.Empty, DateTimeOffset.Now));

            await harness.Device.SetBrightnessAsync(35);
            await harness.Device.Background.TurnOnAsync();

            await Wait.UntilAsync(
                () => entries.Any(entry => entry.Kind == EventKind.DeviceStateChanged && entry.IsBackground && entry.Property == "IsOn")
                    && entries.Any(entry => entry.Kind == EventKind.PropertyChanged && entry.Property == "Brightness"),
                "the events");
            Assert.All(entries, entry => Assert.NotEqual("(not shown)", entry.Value));
            Assert.Contains(entries, entry => entry.Kind == EventKind.DeviceStateChanged && !entry.IsBackground && entry.Property == "Brightness" && entry.Value == "35%");
        }

        [Fact]
        public async Task AnEvent_IsWrittenAsJson_ForTheDetailsAndForAnExport()
        {
            await using var harness = await Harness.StartAsync(new SimulatedBulbOptions { Name = "Desk \"lamp\"" });
            var entry = EventEntry.StateChanged(harness.Device, harness.Device, "IsOn", new DateTimeOffset(2026, 11, 6, 12, 30, 15, TimeSpan.Zero));

            using var indented = JsonDocument.Parse(entry.ToJson());
            using var line = JsonDocument.Parse(entry.ToLine());

            Assert.Contains("\n", entry.ToJson(), StringComparison.Ordinal);
            Assert.DoesNotContain("\n", entry.ToLine(), StringComparison.Ordinal);
            foreach (var document in new[] { indented, line })
            {
                Assert.Equal("DeviceStateChanged", document.RootElement.GetProperty("event").GetString());
                Assert.Equal(harness.Bulb.Id, document.RootElement.GetProperty("light").GetString());
                Assert.Equal("Desk \"lamp\"", document.RootElement.GetProperty("name").GetString());
                Assert.Equal("main", document.RootElement.GetProperty("target").GetString());
                Assert.Equal("IsOn", document.RootElement.GetProperty("property").GetString());
                Assert.Equal("on", document.RootElement.GetProperty("value").GetString());
                Assert.Equal("2026-11-06T12:30:15+00:00", document.RootElement.GetProperty("time").GetString());
            }
        }

        [Fact]
        public async Task TheEvents_AreWrittenAsOneLineForEachEvent()
        {
            await using var harness = await Harness.StartAsync();
            var entries = Enumerable.Range(0, 12).Select(_ => EventEntry.StateChanged(harness.Device, harness.Device, "Brightness", DateTimeOffset.Now)).ToArray();

            var path = SessionExporter.ExportEvents(entries, this._folder);

            Assert.StartsWith(this._folder, path, StringComparison.Ordinal);
            var lines = File.ReadAllLines(path);
            Assert.Equal(12, lines.Length);
            Assert.All(lines, text =>
            {
                using var document = JsonDocument.Parse(text);
                Assert.Equal("Brightness", document.RootElement.GetProperty("property").GetString());
            });
        }

        [Fact]
        public async Task TheFilter_ChoosesByLightKindAndText()
        {
            await using var first = await Harness.StartAsync(new SimulatedBulbOptions { Id = "0x000000000000000a", Name = "Desk lamp" });
            await using var second = await Harness.StartAsync(new SimulatedBulbOptions { Id = "0x000000000000000b", Name = "Ceiling" });
            var now = DateTimeOffset.Now;
            var all = new[]
            {
                EventEntry.Discovered(first.Device, now),
                EventEntry.StateChanged(first.Device, first.Device, "Brightness", now),
                EventEntry.PropertyChanged(first.Device, first.Device, "IsOn", now),
                EventEntry.Discovered(second.Device, now),
                EventEntry.StateChanged(second.Device, second.Device, "ColorTemperature", now),
            };

            Assert.Equal(5, all.Count(EventFilter.All.Matches));
            Assert.Equal(3, all.Count(new EventFilter { LightId = first.Bulb.Id }.Matches));
            Assert.Equal(2, all.Count(new EventFilter { LightId = second.Bulb.Id.ToUpperInvariant().Replace("0X", "0x") }.Matches));
            Assert.Equal(2, all.Count(new EventFilter { Kind = EventKind.DeviceDiscovered }.Matches));
            Assert.Equal(2, all.Count(new EventFilter { Kind = EventKind.DeviceStateChanged }.Matches));
            Assert.Equal(1, all.Count(new EventFilter { Kind = EventKind.PropertyChanged }.Matches));
            Assert.Equal(1, all.Count(new EventFilter { LightId = first.Bulb.Id, Kind = EventKind.PropertyChanged }.Matches));
            Assert.Equal(0, all.Count(new EventFilter { LightId = second.Bulb.Id, Kind = EventKind.PropertyChanged }.Matches));
            Assert.Equal(1, all.Count(new EventFilter { Text = "colortemperature" }.Matches));
            Assert.Equal(3, all.Count(new EventFilter { Text = "DESK" }.Matches));
            Assert.Equal(2, all.Count(new EventFilter { Text = second.Bulb.Id }.Matches));
        }

        [Fact]
        public void TheStore_KeepsTheLatestEvents_AndNumbersThem()
        {
            var store = EventEntry.NewStore(50);

            for (var index = 0; index < 500; index++)
            {
                store.Add(new EventEntry { Time = DateTimeOffset.Now, Kind = EventKind.DeviceStateChanged, Property = "IsOn", Value = "on" });
            }

            Assert.Equal(500, store.Sequence);
            Assert.InRange(store.Count, 50, 55);
            Assert.Equal(500, store.Snapshot()[^1].Sequence);
            Assert.Equal(5_000, EventEntry.Capacity);
        }
    }
}
