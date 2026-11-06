namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// A message ends with a line break, but one read of a socket returns whatever has arrived: part of a message, or several. The reader
    /// puts the messages back together, however they were cut up.
    /// </summary>
    public class YeelightLineReaderTests
    {
        // Every kind of character: one, two, three and four bytes of UTF-8, so that a cut can fall in the middle of one.
        private const string First = "{\"id\":1,\"result\":[\"é日本語\"]}";

        private const string Second = "{\"method\":\"props\",\"params\":{\"name\":\"\U0001F4A1 lamp\"}}";

        private static readonly byte[] Both = Encoding.UTF8.GetBytes(First + "\r\n" + Second + "\r\n");

        public static IEnumerable<object[]> SplitPositions()
        {
            return Enumerable.Range(1, Both.Length - 1).Select(position => new object[] { position });
        }

        private static YeelightLineReader ReaderOf(string text, int? maxLineLength = null)
        {
            return ReaderOf(Encoding.UTF8.GetBytes(text), maxLineLength);
        }

        private static YeelightLineReader ReaderOf(byte[] bytes, int? maxLineLength = null)
        {
            var stream = new ChunkStream(bytes);
            return maxLineLength is { } limit ? new YeelightLineReader(stream, limit) : new YeelightLineReader(stream);
        }

        private static async Task<List<string?>> ReadAllAsync(YeelightLineReader reader)
        {
            var lines = new List<string?>();
            for (var count = 0; count < 100_000; count++)
            {
                var line = await reader.ReadLineAsync();
                lines.Add(line);
                if (line is null)
                {
                    return lines;
                }
            }

            throw new InvalidOperationException("The reader never reached the end of the stream.");
        }

        [Fact]
        public async Task ALineEndedWithCrLf_IsReturnedWithoutIt()
        {
            var reader = ReaderOf("hello\r\n");

            Assert.Equal("hello", await reader.ReadLineAsync());
            Assert.Null(await reader.ReadLineAsync());
        }

        [Fact]
        public async Task ABareLineFeed_EndsALineToo()
        {
            var reader = ReaderOf("hello\nworld\n");

            Assert.Equal(["hello", "world", null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task ACarriageReturnInsideALine_IsKept_AndOnlyOneBeforeTheLineFeedIsDropped()
        {
            var reader = ReaderOf("a\rb\r\nc\r\r\nd\r\n");

            Assert.Equal(["a\rb", "c\r", "d", null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task EmptyLines_AreEmptyStrings_AndNotTheEnd()
        {
            var reader = ReaderOf("\r\n\n\r\nx\r\n\r\n");

            Assert.Equal([string.Empty, string.Empty, string.Empty, "x", string.Empty, null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task SeveralMessagesInOneRead_AreReturnedOneByOne()
        {
            var reader = ReaderOf(string.Concat(Enumerable.Range(0, 50).Select(number => $"{{\"id\":{number}}}\r\n")));

            var lines = await ReadAllAsync(reader);

            Assert.Equal(51, lines.Count);
            Assert.Equal("{\"id\":0}", lines[0]);
            Assert.Equal("{\"id\":49}", lines[49]);
            Assert.Null(lines[50]);
        }

        [Fact]
        public async Task TheLineBreakAtTheEndOfARead_AndTheNextMessageInTheNext_AreSeparate()
        {
            var reader = new YeelightLineReader(new ChunkStream(Encoding.UTF8.GetBytes("one\r\n"), Encoding.UTF8.GetBytes("two\r\n")));

            Assert.Equal(["one", "two", null], await ReadAllAsync(reader));
        }

        [Theory]
        [MemberData(nameof(SplitPositions))]
        public async Task TwoMessagesCutInTwoAtAnyPoint_ArePutBackTogether(int position)
        {
            var reader = new YeelightLineReader(new ChunkStream(Both[..position], Both[position..]));

            Assert.Equal([First, Second, null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task TwoMessagesCutInThreeAtAnyTwoPoints_ArePutBackTogether()
        {
            for (var first = 1; first < Both.Length - 1; first++)
            {
                for (var second = first + 1; second < Both.Length; second++)
                {
                    var reader = new YeelightLineReader(new ChunkStream(Both[..first], Both[first..second], Both[second..]));

                    var lines = await ReadAllAsync(reader);

                    Assert.True(
                        lines.SequenceEqual([First, Second, null]),
                        $"Cut at {first} and {second}: " + string.Join(" | ", lines.Select(line => line ?? "<end>")));
                }
            }
        }

        [Fact]
        public async Task TwoMessagesDeliveredAByteAtATime_ArePutBackTogether()
        {
            var reader = new YeelightLineReader(new ChunkStream(Both.Select(b => new[] { b }).ToArray()));

            Assert.Equal([First, Second, null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task ALineLongerThanTheBuffersOfTheReader_IsAssembled()
        {
            var text = new string('x', 10_000);
            var chunks = Encoding.UTF8.GetBytes(text + "\r\nnext\r\n").Chunk(100).ToArray();
            var reader = new YeelightLineReader(new ChunkStream(chunks));

            Assert.Equal([text, "next", null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task ALineOfExactlyTheLimit_IsAccepted()
        {
            var reader = ReaderOf(new string('a', 100) + "\nnext\n", maxLineLength: 100);

            Assert.Equal([new string('a', 100), "next", null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task ALineOfExactlyTheLimit_EndedWithCrLf_IsAccepted()
        {
            // The line is what comes before its line break, so a line of 100 bytes is not longer than 100, whatever ends it.
            var reader = ReaderOf(new string('a', 100) + "\r\nnext\r\n", maxLineLength: 100);

            Assert.Equal([new string('a', 100), "next", null], await ReadAllAsync(reader));
        }

        [Theory]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public async Task ALineOneByteOverTheLimit_Throws(string lineBreak)
        {
            var reader = ReaderOf(new string('a', 101) + lineBreak, maxLineLength: 100);

            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
        }

        [Fact]
        public async Task TheDefaultLimit_Is64KilobytesAndAppliesToTheWholeLine()
        {
            Assert.Equal(64 * 1024, YeelightLineReader.DefaultMaxLineLength);
            var accepted = new YeelightLineReader(new ChunkStream(Encoding.UTF8.GetBytes(new string('a', 64 * 1024) + "\n").Chunk(1000).ToArray()));
            var refused = new YeelightLineReader(new ChunkStream(Encoding.UTF8.GetBytes(new string('a', (64 * 1024) + 1) + "\n").Chunk(1000).ToArray()));

            Assert.Equal(64 * 1024, (await accepted.ReadLineAsync())!.Length);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await refused.ReadLineAsync());
        }

        [Fact]
        public async Task ALineThatNeverEnds_ThrowsAsSoonAsItIsTooLong_AndNotAtTheEndOfTheStream()
        {
            // An endless stream: reading it to its end would never finish, and fill the memory.
            var reader = new YeelightLineReader(new EndlessStream((byte)'a'), 1000);

            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
        }

        [Theory]
        [InlineData(10)]
        [InlineData(4500)]
        public async Task AfterALineThatWasTooLong_NoPieceOfItIsDeliveredAsALine(int limit)
        {
            // Not stated by the documentation, but a reader that went on would give the rest of the long line as if it were a message
            // (and in the worst case whatever follows the line break inside it), and one that cannot go on at all would throw for ever.
            var reader = ReaderOf(new string('a', 5000) + "\r\nok\r\n", maxLineLength: limit);

            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
            var next = await reader.ReadLineAsync();

            Assert.Equal("ok", next);
            Assert.Null(await reader.ReadLineAsync());
        }

        [Fact]
        public async Task ThePartOfALineThatCameBeforeTheEndOfTheStream_IsDropped()
        {
            var reader = ReaderOf("complete\r\npartial");

            Assert.Equal(["complete", null], await ReadAllAsync(reader));
        }

        [Fact]
        public async Task AfterTheEnd_TheReaderKeepsReturningTheEnd()
        {
            var reader = ReaderOf("one\r\n");

            Assert.Equal("one", await reader.ReadLineAsync());
            Assert.Null(await reader.ReadLineAsync());
            Assert.Null(await reader.ReadLineAsync());
            Assert.Null(await reader.ReadLineAsync());
        }

        [Fact]
        public async Task AnEmptyStream_IsTheEndAtOnce()
        {
            Assert.Null(await ReaderOf(string.Empty).ReadLineAsync());
        }

        [Fact]
        public async Task BytesThatAreNotUtf8_BecomeReplacementCharacters_AndNeverThrow()
        {
            var reader = ReaderOf([(byte)'a', 0xFF, 0xFE, (byte)'b', 0xC3, (byte)'\r', (byte)'\n']);

            var line = await reader.ReadLineAsync();

            Assert.NotNull(line);
            Assert.StartsWith("a", line);
            Assert.Contains('\uFFFD', line);
        }

        [Fact]
        public async Task ANullCharacter_IsKept()
        {
            var reader = ReaderOf("a\0b\r\n");

            Assert.Equal("a\0b", await reader.ReadLineAsync());
        }

        [Fact]
        public async Task WaitingForData_EndsWithACancellationWhenTheTokenIsCancelled()
        {
            using var cancellation = new CancellationTokenSource();
            var reader = new YeelightLineReader(new SilentStream());

            var reading = reader.ReadLineAsync(cancellation.Token).AsTask();
            Assert.False(reading.IsCompleted);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        }

        [Fact]
        public async Task ATokenThatIsAlreadyCancelled_StopsTheReadBeforeItStarts()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var reader = ReaderOf("one\r\n");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadLineAsync(cancellation.Token));
        }

        [Fact]
        public void ANullStream_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => new YeelightLineReader(null!));

            Assert.Equal("stream", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ALimitOfLessThanOneByte_IsRefused(int limit)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new YeelightLineReader(new ChunkStream(), limit));

            Assert.Equal("maxLineLength", refusal.ParamName);
        }

        [Fact]
        public async Task ALimitOfOneByte_StillReadsLinesOfOneByteAndOfNone()
        {
            var reader = ReaderOf("a\n\nb\n", maxLineLength: 1);

            Assert.Equal(["a", string.Empty, "b", null], await ReadAllAsync(reader));
        }

        /// <summary>A stream that hands out the chunks that it was given, one for every read, and then ends.</summary>
        private sealed class ChunkStream : Stream
        {
            private readonly Queue<byte[]> _chunks;
            private byte[]? _current;
            private int _offset;

            public ChunkStream(params byte[][] chunks)
            {
                this._chunks = new Queue<byte[]>(chunks);
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

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(this.Take(buffer.Span));
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return this.Take(buffer.AsSpan(offset, count));
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            private int Take(Span<byte> buffer)
            {
                while (this._current is null || this._offset >= this._current.Length)
                {
                    if (!this._chunks.TryDequeue(out this._current))
                    {
                        return 0;
                    }

                    this._offset = 0;
                }

                var count = Math.Min(buffer.Length, this._current.Length - this._offset);
                this._current.AsSpan(this._offset, count).CopyTo(buffer);
                this._offset += count;
                return count;
            }
        }

        /// <summary>A stream that never ends, and gives one byte over and over.</summary>
        private sealed class EndlessStream : Stream
        {
            private readonly byte _value;

            public EndlessStream(byte value)
            {
                this._value = value;
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

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                buffer.Span.Fill(this._value);
                return ValueTask.FromResult(buffer.Length);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                buffer.AsSpan(offset, count).Fill(this._value);
                return count;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>A stream that waits for ever (until a read is cancelled).</summary>
        private sealed class SilentStream : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
