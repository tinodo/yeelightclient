namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// What the line reader has to give for a stream of bytes, written down as plainly as possible, to be compared with what it gives
    /// however the stream is cut up: a line ends at a line feed (and loses the carriage return before it), a line longer than the limit
    /// is an error (one for each, and the line after it is read as usual), and what is left at the end without a line break is not a
    /// line (unless it is already longer than the limit allows, which is an error).
    /// </summary>
    internal static class LineReaderModel
    {
        public const string End = "END";

        public const string Error = "ERR";

        /// <summary>Gets what the reader has to give for the bytes: <c>L:text</c> for a line, <c>ERR</c> for an error, <c>END</c> at the end.</summary>
        public static List<string> Expected(byte[] data, int maxLineLength)
        {
            var events = new List<string>();
            var position = 0;
            while (true)
            {
                var newline = Array.IndexOf(data, (byte)'\n', position);
                if (newline < 0)
                {
                    // A line may be one byte longer than the limit while it comes in: that byte can be the carriage return of its line break.
                    if (data.Length - position > maxLineLength + 1)
                    {
                        events.Add(Error);
                    }

                    events.Add(End);
                    return events;
                }

                var length = newline - position;
                if (length > 0 && data[newline - 1] == (byte)'\r')
                {
                    length--;
                }

                events.Add(length > maxLineLength ? Error : "L:" + Encoding.UTF8.GetString(data, position, length));
                position = newline + 1;
            }
        }

        /// <summary>Gets what the reader gives for the bytes when the stream gives them in pieces of the sizes that are told.</summary>
        public static List<string> Actual(byte[] data, int[] sizes, int maxLineLength)
        {
            var events = new List<string>();
            var reader = new YeelightLineReader(new ChunkedStream(data, sizes), maxLineLength);
            while (true)
            {
                try
                {
                    var line = reader.ReadLineAsync().AsTask().GetAwaiter().GetResult();
                    if (line is null)
                    {
                        events.Add(End);
                        return events;
                    }

                    events.Add("L:" + line);
                }
                catch (InvalidDataException)
                {
                    events.Add(Error);
                }

                // A reader that goes on for ever on the same bytes is a failure too, and one that says so.
                if (events.Count > data.Length + 5)
                {
                    events.Add("NEVER ENDS");
                    return events;
                }
            }
        }
    }
}
