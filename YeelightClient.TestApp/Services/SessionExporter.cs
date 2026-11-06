namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    /// <summary>Writes what the app saw to files that can be looked at and analysed: the log and the events as JSON lines, one entry per line.</summary>
    internal static class SessionExporter
    {
        /// <summary>Writes the log as JSON lines.</summary>
        /// <param name="entries">The entries.</param>
        /// <param name="folder">The folder to write to, or <c>null</c> for the export folder of the app (<see cref="AppSettings.ExportFolder"/>).</param>
        /// <returns>The path of the file.</returns>
        public static string ExportLog(IEnumerable<LogEntry> entries, string? folder = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            var path = NewPath("yeelight-log", "jsonl", folder);
            using var file = new StreamWriter(path, false, new UTF8Encoding(false));
            foreach (var entry in entries)
            {
                file.WriteLine(ToLine(entry));
            }

            return path;
        }

        /// <summary>Writes the events as JSON lines.</summary>
        /// <param name="entries">The events.</param>
        /// <param name="folder">The folder to write to, or <c>null</c> for the export folder of the app (<see cref="AppSettings.ExportFolder"/>).</param>
        /// <returns>The path of the file.</returns>
        public static string ExportEvents(IEnumerable<EventEntry> entries, string? folder = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            var path = NewPath("yeelight-events", "jsonl", folder);
            using var file = new StreamWriter(path, false, new UTF8Encoding(false));
            foreach (var entry in entries)
            {
                file.WriteLine(entry.ToLine());
            }

            return path;
        }

        /// <summary>Opens the folder of a file, with the file selected.</summary>
        /// <param name="path">The file.</param>
        public static void ShowInFolder(string? path)
        {
            if (path is not null && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }

        /// <summary>Writes an entry of the log as one line of JSON. A message of the wire is written as the JSON that it is, and not as text in a string.</summary>
        /// <param name="entry">The entry.</param>
        /// <returns>The line, without a line break.</returns>
        internal static string ToLine(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartObject();
                writer.WriteString("time", entry.Time);
                writer.WriteString("level", entry.Level.ToString());
                writer.WriteString("kind", entry.Kind.ToString().ToLowerInvariant());
                if (entry.Light.Length > 0)
                {
                    writer.WriteString("light", entry.Light);
                }

                if (entry.Method is not null)
                {
                    writer.WriteString("method", entry.Method);
                }

                if (entry.RequestId is { } id)
                {
                    writer.WriteNumber("id", id);
                }

                if (entry.ElapsedMs is { } elapsed)
                {
                    writer.WriteNumber("elapsedMs", Math.Round(elapsed, 2));
                }

                if (entry.Failed)
                {
                    writer.WriteBoolean("failed", true);
                }

                writer.WriteString("title", entry.Title);
                if (entry.Kind != LogKind.Client && IsJson(entry.Text))
                {
                    writer.WritePropertyName("frame");
                    writer.WriteRawValue(entry.Text);
                }
                else
                {
                    writer.WriteString("text", entry.Text);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string NewPath(string name, string extension, string? folder)
        {
            var target = folder ?? AppSettings.ExportFolder;
            Directory.CreateDirectory(target);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            return Path.Combine(target, $"{name}-{stamp}.{extension}");
        }

        private static bool IsJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
