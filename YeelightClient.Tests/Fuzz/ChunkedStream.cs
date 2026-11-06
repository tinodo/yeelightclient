namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A stream that gives its bytes in pieces of the sizes that it is told (over and over when it runs out of sizes), the way the reads
    /// of a socket cut up what was sent: whatever has arrived, which can be one byte or many messages.
    /// </summary>
    internal sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _sizes;
        private int _position;
        private int _next;

        /// <param name="data">What is read.</param>
        /// <param name="sizes">The size of the pieces, each at least one; none means everything at once.</param>
        public ChunkedStream(byte[] data, int[] sizes)
        {
            this._data = data;
            this._sizes = sizes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => this.Take(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => this.Take(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(this.Take(buffer.AsSpan(offset, count)));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(this.Take(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Take(Span<byte> buffer)
        {
            if (this._position >= this._data.Length)
            {
                return 0;
            }

            var size = this._sizes.Length == 0 ? int.MaxValue : this._sizes[this._next++ % this._sizes.Length];
            var count = Math.Min(Math.Min(size, buffer.Length), this._data.Length - this._position);
            this._data.AsSpan(this._position, count).CopyTo(buffer);
            this._position += count;
            return count;
        }
    }
}
