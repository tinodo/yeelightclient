namespace Yeelight.TestApp.Services
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    /// <summary>The kinds of events that the client and its lights raise.</summary>
    internal enum EventKind
    {
        /// <summary><see cref="YeelightClient.DeviceDiscovered"/>: a light was found for the first time.</summary>
        DeviceDiscovered,

        /// <summary><see cref="YeelightClient.DeviceStateChanged"/>: a property of one of the lights of the client changed.</summary>
        DeviceStateChanged,

        /// <summary><see cref="YeelightLight.PropertyChanged"/>: a property of the selected light changed (the event of the light itself).</summary>
        PropertyChanged,
    }

    /// <summary>One event, as the client raised it: when, what, for which light, and for a change what the property is now.</summary>
    internal sealed class EventEntry
    {
        /// <summary>The number of events that the page keeps: the oldest are let go of.</summary>
        public const int Capacity = 5_000;

        private static readonly JsonWriterOptions Indented = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Gets the position of the event; it grows with every event.</summary>
        public long Sequence { get; internal set; }

        /// <summary>Gets the time the event was handled by the app.</summary>
        public DateTimeOffset Time { get; init; }

        /// <summary>Gets the kind of event.</summary>
        public EventKind Kind { get; init; }

        /// <summary>Gets the id of the device.</summary>
        public string LightId { get; init; } = string.Empty;

        /// <summary>Gets the name of the device when it has one, or else its model.</summary>
        public string LightName { get; init; } = string.Empty;

        /// <summary>Gets a value indicating whether the change is of the background light of the device.</summary>
        public bool IsBackground { get; init; }

        /// <summary>Gets the name of the property that changed (<c>Brightness</c>), or <c>Device</c> for a light that was found.</summary>
        public string Property { get; init; } = string.Empty;

        /// <summary>
        /// Gets what the property is when the app handled the event. The event itself does not carry the value: the app reads it from the
        /// light, so it can be newer than the change that the event reports when the light changed again in between.
        /// </summary>
        public string Value { get; init; } = string.Empty;

        /// <summary>Gets the time, for the list.</summary>
        public string TimeText => this.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        /// <summary>Gets the name of the kind of event, for the list.</summary>
        public string KindText => this.Kind.ToString();

        /// <summary>Gets which light of the device it is about: <c>main</c> or <c>background</c>.</summary>
        public string Target => this.IsBackground ? "background" : "main";

        /// <summary>Gets the light for the list: its name and the end of its id.</summary>
        public string LightText => (this.LightName.Length > 0 ? this.LightName : "light") + " \u00b7 " + LogEntry.ShortId(this.LightId);

        /// <summary>Gets what happened on one line, for the list.</summary>
        public string Summary => this.Kind == EventKind.DeviceDiscovered
            ? this.Value
            : (this.IsBackground ? "background " : string.Empty) + this.Property + " = " + this.Value;

        /// <summary>Makes the store that holds the events.</summary>
        /// <param name="capacity">How many events it keeps.</param>
        /// <returns>The store; an event that is added to it is numbered.</returns>
        public static BoundedBuffer<EventEntry> NewStore(int capacity = Capacity)
        {
            return new BoundedBuffer<EventEntry>(capacity, static (entry, sequence) => entry.Sequence = sequence);
        }

        /// <summary>Makes the event for a light that was found.</summary>
        /// <param name="device">The light.</param>
        /// <param name="time">The time.</param>
        /// <returns>The event.</returns>
        public static EventEntry Discovered(YeelightDevice device, DateTimeOffset time)
        {
            ArgumentNullException.ThrowIfNull(device);
            return new EventEntry
            {
                Time = time,
                Kind = EventKind.DeviceDiscovered,
                LightId = device.Id,
                LightName = NameOf(device),
                Property = "Device",
                Value = $"{NameOf(device)} found at {device.Location}",
            };
        }

        /// <summary>Makes the event for a change of a property that <see cref="YeelightClient.DeviceStateChanged"/> reported.</summary>
        /// <param name="device">The device.</param>
        /// <param name="light">The light that changed: the device, or its background light.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="time">The time.</param>
        /// <returns>The event.</returns>
        public static EventEntry StateChanged(YeelightDevice device, YeelightLight light, string propertyName, DateTimeOffset time)
        {
            return Change(EventKind.DeviceStateChanged, device, light, propertyName, time);
        }

        /// <summary>Makes the event for a change of a property that <see cref="YeelightLight.PropertyChanged"/> reported.</summary>
        /// <param name="device">The device.</param>
        /// <param name="light">The light that changed: the device, or its background light.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="time">The time.</param>
        /// <returns>The event.</returns>
        public static EventEntry PropertyChanged(YeelightDevice device, YeelightLight light, string propertyName, DateTimeOffset time)
        {
            return Change(EventKind.PropertyChanged, device, light, propertyName, time);
        }

        /// <summary>Writes the event as indented JSON, for the details.</summary>
        /// <returns>The JSON.</returns>
        public string ToJson()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, Indented))
            {
                this.Write(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Writes the event as one line of JSON, for an export.</summary>
        /// <returns>The line.</returns>
        public string ToLine()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                this.Write(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string NameOf(YeelightDevice device) => device.Name.Length > 0 ? device.Name : device.Model.Length > 0 ? device.Model : "light";

        private static EventEntry Change(EventKind kind, YeelightDevice device, YeelightLight light, string propertyName, DateTimeOffset time)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(light);
            ArgumentNullException.ThrowIfNull(propertyName);
            return new EventEntry
            {
                Time = time,
                Kind = kind,
                LightId = device.Id,
                LightName = NameOf(device),
                IsBackground = light is YeelightBackgroundLight,
                Property = propertyName,
                Value = LightFormat.ValueOf(light, propertyName),
            };
        }

        private void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("time", this.Time);
            writer.WriteString("event", this.KindText);
            writer.WriteString("light", this.LightId);
            writer.WriteString("name", this.LightName);
            writer.WriteString("target", this.Target);
            writer.WriteString("property", this.Property);
            writer.WriteString("value", this.Value);
            writer.WriteEndObject();
        }
    }

    /// <summary>What the Events page shows: one light, one kind of event, and a piece of text.</summary>
    internal sealed class EventFilter
    {
        /// <summary>Gets the filter that shows everything.</summary>
        public static EventFilter All { get; } = new();

        /// <summary>Gets the id of the light whose events are shown, or nothing for all of them.</summary>
        public string LightId { get; init; } = string.Empty;

        /// <summary>Gets the kind of event that is shown, or <c>null</c> for all of them.</summary>
        public EventKind? Kind { get; init; }

        /// <summary>Gets the text that has to be in the event (the case does not matter), or nothing.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Determines whether an event is shown.</summary>
        /// <param name="entry">The event.</param>
        /// <returns><c>true</c> when it passes every part of the filter.</returns>
        public bool Matches(EventEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (this.Kind is { } kind && entry.Kind != kind)
            {
                return false;
            }

            if (this.LightId.Length > 0 && !string.Equals(entry.LightId, this.LightId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var text = this.Text.Trim();
            return text.Length == 0
                || entry.KindText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Property.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Value.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.LightName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.LightId.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
