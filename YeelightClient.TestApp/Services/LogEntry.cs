namespace Yeelight.TestApp.Services
{
    using System;
    using System.Globalization;
    using Microsoft.Extensions.Logging;

    /// <summary>Where an entry of the log comes from.</summary>
    internal enum LogKind
    {
        /// <summary>Something that the client reported, or that the app noted: a connection, a search, a warning.</summary>
        Client,

        /// <summary>A message that was sent to a light.</summary>
        Sent,

        /// <summary>A message that was received from a light.</summary>
        Received,
    }

    /// <summary>One line of the log: a message that went over the wire, or something that the client or the app reported.</summary>
    internal sealed class LogEntry
    {
        /// <summary>The number of entries that the log keeps: the oldest are let go of.</summary>
        public const int Capacity = 20_000;

        /// <summary>Gets the position of the entry in the log; it grows with every entry.</summary>
        public long Sequence { get; internal set; }

        /// <summary>Gets the time the entry was made.</summary>
        public DateTimeOffset Time { get; init; }

        /// <summary>Gets where the entry comes from.</summary>
        public LogKind Kind { get; init; }

        /// <summary>Gets the level of the entry.</summary>
        public LogLevel Level { get; init; }

        /// <summary>Gets the id of the light that the entry is about, or nothing when it is about no light in particular.</summary>
        public string Light { get; init; } = string.Empty;

        /// <summary>Gets what the entry is, in a few words.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Gets the whole entry: the JSON of a message exactly as it went over the wire, or the text of the report. It is never cut.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Gets the method that a message is about (of a request, and of the answer to it), when it could be read.</summary>
        public string? Method { get; init; }

        /// <summary>Gets the number of the request that a message is about, when it could be read.</summary>
        public int? RequestId { get; init; }

        /// <summary>Gets how long the light took to answer, in milliseconds, for the answer to a request that was logged.</summary>
        public double? ElapsedMs { get; init; }

        /// <summary>Gets a value indicating whether the message is an answer that has an error in it.</summary>
        public bool Failed { get; init; }

        /// <summary>Gets a value indicating whether the entry deserves attention: an error answer, a warning or worse.</summary>
        public bool IsProblem => this.Failed || this.Level >= LogLevel.Warning;

        /// <summary>Gets the time, for the list.</summary>
        public string TimeText => this.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        /// <summary>Gets the direction of a message, for the list.</summary>
        public string Direction => this.Kind switch
        {
            LogKind.Sent => "\u2192",
            LogKind.Received => "\u2190",
            _ => "\u2022",
        };

        /// <summary>Gets the level, for the list.</summary>
        public string LevelText => this.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => string.Empty,
        };

        /// <summary>Gets the light for the list: the id, shortened to its last digits.</summary>
        public string LightText => ShortId(this.Light);

        /// <summary>Gets how long the light took to answer, for the list: <c>4 ms</c>, or nothing when that is not known.</summary>
        public string ElapsedText => this.ElapsedMs is { } elapsed ? string.Create(CultureInfo.InvariantCulture, $"{elapsed:0.#} ms") : string.Empty;

        /// <summary>Makes the store that holds the log.</summary>
        /// <param name="capacity">How many entries it keeps.</param>
        /// <returns>The store; an entry that is added to it is numbered.</returns>
        public static BoundedBuffer<LogEntry> NewStore(int capacity = Capacity)
        {
            return new BoundedBuffer<LogEntry>(capacity, static (entry, sequence) => entry.Sequence = sequence);
        }

        /// <summary>Shortens the id of a light for a list: the last six characters, such as <c>15243f</c>.</summary>
        /// <param name="id">The id, for example <c>0x000000000015243f</c>.</param>
        /// <returns>The short form; an empty text stays empty.</returns>
        public static string ShortId(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            return id.Length <= 8 ? id : id[^6..];
        }
    }
}
