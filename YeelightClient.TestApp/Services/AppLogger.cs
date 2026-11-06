namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The logger of the clients of the app. It records everything that a client logs (the debug lines <c>Sending:</c> and <c>Received:</c> with
    /// the id of the light, the connections and the searches, the warnings) in the log of the app, which outlives the clients: the app makes
    /// a new client when the settings are applied, and the old one can still log for a moment. Messages are kept whole, never cut.
    /// </summary>
    internal sealed class AppLogger : ILogger<YeelightClient>
    {
        private const string SendingMarker = "Sending: ";
        private const string ReceivedMarker = "Received: ";
        private const int MaxPending = 1024;

        private readonly BoundedBuffer<LogEntry> _store;
        private readonly object _gate = new();
        private readonly Dictionary<(string Light, int Id), (string Method, long Timestamp)> _pending = [];

        /// <summary>Initializes a new instance of the <see cref="AppLogger"/> class.</summary>
        /// <param name="store">Where the entries are kept.</param>
        public AppLogger(BoundedBuffer<LogEntry> store)
        {
            ArgumentNullException.ThrowIfNull(store);
            this._store = store;
        }

        /// <summary>Gets where the entries are kept.</summary>
        public BoundedBuffer<LogEntry> Store => this._store;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None)
            {
                return;
            }

            // A client logs a message before it sends it, and while it reads one: a logger that throws would break the connection. A log
            // that cannot format a message keeps the report of that, in place of the entry.
            LogEntry entry;
            try
            {
                entry = this.Create(DateTimeOffset.Now, logLevel, LightOf(state), formatter(state, exception), exception);
            }
            catch (Exception problem) when (problem is not OutOfMemoryException)
            {
                entry = new LogEntry
                {
                    Time = DateTimeOffset.Now,
                    Kind = LogKind.Client,
                    Level = LogLevel.Error,
                    Title = "The log could not make an entry",
                    Text = problem.ToString(),
                };
            }

            this._store.Add(entry);
        }

        /// <summary>Adds a note of the app itself to the log.</summary>
        /// <param name="level">The level.</param>
        /// <param name="message">The message.</param>
        /// <param name="light">The id of the light that it is about, if any.</param>
        public void Note(LogLevel level, string message, string light = "")
        {
            ArgumentNullException.ThrowIfNull(message);
            this._store.Add(this.Create(DateTimeOffset.Now, level, light, message, null));
        }

        /// <summary>Makes the entry for something that was logged.</summary>
        /// <param name="time">The time.</param>
        /// <param name="level">The level.</param>
        /// <param name="light">The id of the light that the message is about, or nothing.</param>
        /// <param name="message">The formatted message.</param>
        /// <param name="exception">The exception that was logged with it, if any.</param>
        /// <returns>The entry; a message of the wire (<c>Sending:</c> and <c>Received:</c>) is read, so that the list can say what it is.</returns>
        internal LogEntry Create(DateTimeOffset time, LogLevel level, string light, string message, Exception? exception)
        {
            var prefix = light.Length > 0 ? light + ": " : string.Empty;
            if (message.StartsWith(prefix + SendingMarker, StringComparison.Ordinal))
            {
                return this.CreateSent(time, level, light, message[(prefix.Length + SendingMarker.Length)..]);
            }

            if (message.StartsWith(prefix + ReceivedMarker, StringComparison.Ordinal))
            {
                return this.CreateReceived(time, level, light, message[(prefix.Length + ReceivedMarker.Length)..]);
            }

            var firstLine = message.Split('\n', 2)[0].TrimEnd('\r');
            var text = exception is null ? message : message + Environment.NewLine + exception;
            return new LogEntry { Time = time, Kind = LogKind.Client, Level = level, Light = light, Title = firstLine, Text = text };
        }

        private static string LightOf<TState>(TState state)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    if (pair.Key == "Light")
                    {
                        return pair.Value?.ToString() ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        private static string Short(string text, int maximum)
        {
            return text.Length <= maximum ? text : text[..maximum] + "\u2026";
        }

        private LogEntry CreateSent(DateTimeOffset time, LogLevel level, string light, string json)
        {
            string? method = null;
            int? id = null;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    method = root.TryGetProperty("method", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                    id = root.TryGetProperty("id", out var number) && number.TryGetInt32(out var value) ? value : null;
                }
            }
            catch (JsonException)
            {
                // Not JSON: the entry is kept as text.
            }

            if (method is not null && id is { } requestId)
            {
                lock (this._gate)
                {
                    if (this._pending.Count >= MaxPending)
                    {
                        this._pending.Clear();
                    }

                    this._pending[(light, requestId)] = (method, Stopwatch.GetTimestamp());
                }
            }

            var title = method is null ? "(a message that is not understood)" : string.Create(CultureInfo.InvariantCulture, $"{method} #{id}");
            return new LogEntry { Time = time, Kind = LogKind.Sent, Level = level, Light = light, Title = title, Text = json, Method = method, RequestId = id };
        }

        private LogEntry CreateReceived(DateTimeOffset time, LogLevel level, string light, string json)
        {
            string? method = null;
            int? id = null;
            double? elapsed = null;
            bool failed = false;
            string title = "(a message that is not understood)";
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("method", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        // Something that the light says by itself: props, when its state changed.
                        method = name.GetString();
                        title = method == "props" && root.TryGetProperty("params", out var changed) && changed.ValueKind == JsonValueKind.Object
                            ? "props: " + Short(string.Join(", ", NamesOf(changed)), 120)
                            : method ?? title;
                    }
                    else if (root.TryGetProperty("id", out var number) && number.TryGetInt32(out var value))
                    {
                        id = value;
                        (string Method, long Timestamp) request = default;
                        bool known;
                        lock (this._gate)
                        {
                            known = this._pending.Remove((light, value), out request);
                        }

                        if (known)
                        {
                            method = request.Method;
                            elapsed = Stopwatch.GetElapsedTime(request.Timestamp).TotalMilliseconds;
                        }

                        var what = method ?? "answer";
                        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                        {
                            failed = true;
                            var code = error.TryGetProperty("code", out var codeValue) ? codeValue.ToString() : "?";
                            var message = error.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String ? messageValue.GetString() : string.Empty;
                            title = string.Create(CultureInfo.InvariantCulture, $"{what} #{id} \u2192 error {code}: {message}");
                        }
                        else
                        {
                            var result = root.TryGetProperty("result", out var values) ? values.GetRawText() : "[]";
                            title = string.Create(CultureInfo.InvariantCulture, $"{what} #{id} \u2192 {(result == "[\"ok\"]" ? "ok" : Short(result, 100))}");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON: the entry is kept as text.
            }

            return new LogEntry
            {
                Time = time,
                Kind = LogKind.Received,
                Level = level,
                Light = light,
                Title = title,
                Text = json,
                Method = method,
                RequestId = id,
                ElapsedMs = elapsed,
                Failed = failed,
            };
        }

        private static IEnumerable<string> NamesOf(JsonElement properties)
        {
            foreach (var property in properties.EnumerateObject())
            {
                yield return property.Name;
            }
        }
    }
}
