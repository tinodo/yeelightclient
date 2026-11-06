namespace Yeelight
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads the lines of a Yeelight control connection. A message ends with a line break (CR LF), but one read of a socket returns
    /// whatever has arrived: part of a message, or several messages at once. This puts them back together, however they were cut up.
    /// </summary>
    internal sealed class YeelightLineReader
    {
        /// <summary>The longest line that is accepted: a message of a device is a few hundred bytes.</summary>
        public const int DefaultMaxLineLength = 64 * 1024;

        private readonly Stream _stream;
        private readonly int _maxLineLength;
        private readonly byte[] _buffer = new byte[4096];
        private int _position;
        private int _length;
        private byte[] _line = new byte[512];
        private int _lineLength;
        private bool _skipping;

        public YeelightLineReader(Stream stream, int maxLineLength = DefaultMaxLineLength)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxLineLength, 1);

            this._stream = stream;
            this._maxLineLength = maxLineLength;
        }

        /// <summary>
        /// Reads the next line, without its line break (a bare line feed is accepted too).
        /// </summary>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns>The line, which can be empty, or <c>null</c> when the other side closed the connection. Whatever came after the
        /// last line break before that is not a message and is dropped.</returns>
        /// <exception cref="InvalidDataException">The line is longer than the limit (its line break is not part of it). The rest of that
        /// line is skipped as it comes in, so the next call gives the line after it.</exception>
        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                if (this._position == this._length)
                {
                    this._position = 0;
                    this._length = await this._stream.ReadAsync(this._buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (this._length == 0)
                    {
                        this._lineLength = 0;
                        this._skipping = false;
                        return null;
                    }
                }

                var newline = Array.IndexOf(this._buffer, (byte)'\n', this._position, this._length - this._position);
                if (this._skipping)
                {
                    // What is left of a line that was too long is not a message: up to its line break it is dropped.
                    this._position = newline < 0 ? this._length : newline + 1;
                    this._skipping = newline < 0;
                    continue;
                }

                var end = newline < 0 ? this._length : newline;
                this.Append(this._position, end - this._position);
                if (newline < 0)
                {
                    this._position = this._length;
                    continue;
                }

                this._position = newline + 1;
                var length = this._lineLength;
                this._lineLength = 0;
                if (length > 0 && this._line[length - 1] == (byte)'\r')
                {
                    length--;
                }

                if (length > this._maxLineLength)
                {
                    throw new InvalidDataException($"A line is longer than {this._maxLineLength} bytes.");
                }

                return Encoding.UTF8.GetString(this._line, 0, length);
            }
        }

        private void Append(int offset, int count)
        {
            if (count == 0)
            {
                return;
            }

            // The line break is not part of the line, so a line may hold one more byte than the limit while it comes in: the CR of its line break.
            var limit = this._maxLineLength + 1;
            if (this._lineLength + count > limit)
            {
                this._lineLength = 0;
                this._skipping = true;
                throw new InvalidDataException($"A line is longer than {this._maxLineLength} bytes.");
            }

            if (this._lineLength + count > this._line.Length)
            {
                Array.Resize(ref this._line, Math.Min(limit, Math.Max(this._line.Length * 2, this._lineLength + count)));
            }

            Buffer.BlockCopy(this._buffer, offset, this._line, this._lineLength, count);
            this._lineLength += count;
        }
    }
}
