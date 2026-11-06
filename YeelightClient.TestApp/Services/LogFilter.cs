namespace Yeelight.TestApp.Services
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>What the Log page shows: the level, the light, the kind of entry, and a piece of text that has to be in it.</summary>
    internal sealed class LogFilter
    {
        /// <summary>Gets the filter that shows everything.</summary>
        public static LogFilter All { get; } = new();

        /// <summary>Gets the lowest level that is shown.</summary>
        public LogLevel MinimumLevel { get; init; } = LogLevel.Trace;

        /// <summary>Gets the id of the light whose entries are shown, or nothing for all of them (and for the entries that are about no light).</summary>
        public string LightId { get; init; } = string.Empty;

        /// <summary>Gets the text that has to be in the title, the entry or the id of the light (the case does not matter), or nothing.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Gets a value indicating whether the messages that were sent are shown.</summary>
        public bool ShowSent { get; init; } = true;

        /// <summary>Gets a value indicating whether the messages that were received are shown.</summary>
        public bool ShowReceived { get; init; } = true;

        /// <summary>Gets a value indicating whether what the client and the app reported is shown.</summary>
        public bool ShowClient { get; init; } = true;

        /// <summary>Determines whether an entry is shown.</summary>
        /// <param name="entry">The entry.</param>
        /// <returns><c>true</c> when it passes every part of the filter.</returns>
        public bool Matches(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var shown = entry.Kind switch
            {
                LogKind.Sent => this.ShowSent,
                LogKind.Received => this.ShowReceived,
                _ => this.ShowClient,
            };
            if (!shown || entry.Level < this.MinimumLevel)
            {
                return false;
            }

            if (this.LightId.Length > 0 && !string.Equals(entry.Light, this.LightId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var text = this.Text.Trim();
            return text.Length == 0
                || entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Light.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
