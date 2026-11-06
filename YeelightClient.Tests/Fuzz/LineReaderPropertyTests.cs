namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;

    /// <summary>
    /// What the line reader of the control connection promises for any bytes that arrive, however the reads of the socket cut them up: the
    /// lines come out as they went in, a line that is too long is an error that costs nothing else, and what is left at the end is not a
    /// line. The cases are made by FsCheck from a fixed seed (see <see cref="FuzzSettings"/>), and compared with <see cref="LineReaderModel"/>.
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class LineReaderPropertyTests(ITestOutputHelper output)
    {
        // A line of the stream: text, bytes that are text (or not), nothing. It has no line feed in it, which would make two lines of it.
        private static readonly Gen<byte[]> LineBytes = Gen.Frequency(
            (3, Gen.ListOf(Gen.Choose(32, 126).Select(value => (byte)value)).Select(bytes => bytes.ToArray())),
            (2, FuzzGenerators.Text.Select(text => Encoding.UTF8.GetBytes(text.Replace("\n", string.Empty, StringComparison.Ordinal)))),
            (2, Gen.ListOf(Gen.Choose(0, 255).Select(value => (byte)value)).Select(bytes => bytes.Where(value => value != 10).ToArray())),
            (1, Gen.Constant(Array.Empty<byte>())));

        // The pieces that the reads of a socket give: one byte, a few, a few hundred, more than the longest line.
        private static readonly Gen<int[]> Sizes = Gen.ListOf(
            Gen.Frequency((5, Gen.Choose(1, 8)), (3, Gen.Choose(1, 100)), (2, Gen.Constant(1)), (1, Gen.Choose(1, 70_000))))
            .Select(sizes => sizes.ToArray());

        private static readonly Gen<byte[]> Stream =
            from lines in Gen.ListOf(from line in LineBytes from crlf in Gen.Elements(true, false) select Terminate(line, crlf))
            from fragment in Gen.Frequency((3, Gen.Constant(Array.Empty<byte>())), (1, LineBytes))
            select lines.SelectMany(line => line).Concat(fragment).ToArray();

        // A limit that is small, and a stream of lines whose lengths are around it (the carriage return of the line break is the interesting byte).
        private static readonly Gen<(int Limit, byte[] Data)> StreamAroundALimit =
            from limit in Gen.Choose(1, 40)
            from lines in Gen.ListOf(from line in LineAround(limit) from crlf in Gen.Elements(true, false) select Terminate(line, crlf))
            from fragment in Gen.Frequency((3, Gen.Constant(Array.Empty<byte>())), (2, LineAround(limit)))
            select (limit, lines.SelectMany(line => line).Concat(fragment).ToArray());

        [Fact]
        public void AnyChunking_OfAnySetOfLines_GivesTheLinesBack()
        {
            var limit = YeelightLineReader.DefaultMaxLineLength;

            FuzzCheck.ForAll(
                Stream,
                Sizes,
                (data, sizes) =>
                {
                    Assert.Equal(LineReaderModel.Expected(data, limit), LineReaderModel.Actual(data, sizes, limit));
                    return true;
                },
                seedOffset: 1);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void ALineThatIsTooLong_IsOneErrorWhateverTheChunking_AndTheLinesAroundItAreRead()
        {
            FuzzCheck.ForAll(
                StreamAroundALimit,
                Sizes,
                (stream, sizes) =>
                {
                    Assert.Equal(LineReaderModel.Expected(stream.Data, stream.Limit), LineReaderModel.Actual(stream.Data, sizes, stream.Limit));
                    return true;
                },
                seedOffset: 2);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void TwoChunkingsOfTheSameBytes_GiveTheSameLines()
        {
            FuzzCheck.ForAll(
                StreamAroundALimit,
                from first in Sizes from second in Sizes select (first, second),
                (stream, sizes) =>
                {
                    var first = LineReaderModel.Actual(stream.Data, sizes.first, stream.Limit);
                    var second = LineReaderModel.Actual(stream.Data, sizes.second, stream.Limit);
                    Assert.Equal(first, second);
                    return true;
                },
                seedOffset: 3);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void NoLineIsEverGivenWithALineBreakInIt_AndEveryLineIsText()
        {
            FuzzCheck.ForAll(
                Stream,
                Sizes,
                (data, sizes) =>
                {
                    var limit = YeelightLineReader.DefaultMaxLineLength;
                    foreach (var line in LineReaderModel.Actual(data, sizes, limit).Where(item => item.StartsWith("L:", StringComparison.Ordinal)))
                    {
                        Assert.DoesNotContain('\n', line);
                    }

                    return true;
                },
                seedOffset: 4);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(4096)]
        [InlineData(100_000)]
        public void AMegabyteLine_IsOneError_AndTheLineAfterItIsRead(int chunk)
        {
            var data = Encoding.ASCII.GetBytes("first\r\n")
                .Concat(Enumerable.Repeat((byte)'x', 1024 * 1024))
                .Concat(Encoding.ASCII.GetBytes("\r\nsecond\r\n"))
                .ToArray();

            var events = LineReaderModel.Actual(data, [chunk], YeelightLineReader.DefaultMaxLineLength);

            string[] expected = ["L:first", LineReaderModel.Error, "L:second", LineReaderModel.End];
            Assert.Equal(expected, events);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100_000)]
        public void AMegabyteWithoutALineBreak_IsOneError_AndThenTheEnd(int chunk)
        {
            var data = Enumerable.Repeat((byte)'y', 1024 * 1024).ToArray();

            var events = LineReaderModel.Actual(data, [chunk], YeelightLineReader.DefaultMaxLineLength);

            string[] expected = [LineReaderModel.Error, LineReaderModel.End];
            Assert.Equal(expected, events);
        }

        [Fact]
        public void TheLongestLineThatIsAccepted_IsReadWhole_AndOneByteMoreIsNot()
        {
            var limit = YeelightLineReader.DefaultMaxLineLength;
            var longest = new string('z', limit);
            var data = Encoding.ASCII.GetBytes(longest + "\r\n" + longest + "\n" + longest + "z\r\n" + "after\n");

            var events = LineReaderModel.Actual(data, [4096], limit);

            string[] expected = ["L:" + longest, "L:" + longest, LineReaderModel.Error, "L:after", LineReaderModel.End];
            Assert.Equal(expected, events);
        }

        private static byte[] Terminate(byte[] line, bool crlf)
        {
            return crlf ? [.. line, 13, 10] : [.. line, 10];
        }

        private static Gen<byte[]> LineAround(int limit)
        {
            return from length in Gen.Choose(Math.Max(0, limit - 2), limit + 3)
                   from endsWithReturn in Gen.Elements(false, true)
                   from fill in Gen.Choose(65, 90)
                   select Fill(length, endsWithReturn, (byte)fill);
        }

        private static byte[] Fill(int length, bool endsWithReturn, byte fill)
        {
            var line = Enumerable.Repeat(fill, length).ToArray();
            if (endsWithReturn && length > 0)
            {
                line[^1] = 13;
            }

            return line;
        }
    }
}
