namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The bounded lists that hold the log and the events of the test app: they never grow past their capacity, and they number what they hold.</summary>
    public sealed class BoundedBufferTests
    {
        private sealed class Item
        {
            public long Number { get; set; }
        }

        private static BoundedBuffer<Item> Make(int capacity) => new(capacity, (item, sequence) => item.Number = sequence);

        [Fact]
        public void Entries_AreNumberedFromOne()
        {
            var buffer = Make(100);

            var first = new Item();
            var second = new Item();
            Assert.Equal(1, buffer.Add(first));
            Assert.Equal(2, buffer.Add(second));

            Assert.Equal(1, first.Number);
            Assert.Equal(2, second.Number);
            Assert.Equal(2, buffer.Sequence);
            Assert.Equal(1, buffer.OldestSequence);
            Assert.Equal(2, buffer.Count);
            Assert.Equal(new[] { first, second }, buffer.Snapshot());
        }

        [Fact]
        public void AnEmptyList_HasNothing()
        {
            var buffer = Make(100);

            Assert.Equal(0, buffer.Sequence);
            Assert.Equal(1, buffer.OldestSequence);
            Assert.Equal(0, buffer.Count);
            Assert.Empty(buffer.Snapshot());
            Assert.Empty(buffer.Since(0));
        }

        [Fact]
        public void TheOldestEntries_AreLetGoOf_WhenThereAreTooMany()
        {
            var buffer = Make(100);

            for (var index = 0; index < 1000; index++)
            {
                buffer.Add(new Item());
            }

            Assert.Equal(1000, buffer.Sequence);
            Assert.InRange(buffer.Count, 100, 110);
            Assert.Equal(1000 - buffer.Count + 1, buffer.OldestSequence);
            var kept = buffer.Snapshot();
            Assert.Equal(buffer.OldestSequence, kept[0].Number);
            Assert.Equal(1000, kept[^1].Number);
            Assert.Equal(kept.Select(item => item.Number).OrderBy(number => number), kept.Select(item => item.Number));
        }

        [Fact]
        public void Since_GivesWhatIsNew()
        {
            var buffer = Make(1000);
            for (var index = 0; index < 10; index++)
            {
                buffer.Add(new Item());
            }

            Assert.Equal(10, buffer.Since(0).Count);
            Assert.Equal(new long[] { 8, 9, 10 }, buffer.Since(7).Select(item => item.Number));
            Assert.Empty(buffer.Since(10));
            Assert.Empty(buffer.Since(500));
            Assert.Equal(10, buffer.Since(-5).Count);
        }

        [Fact]
        public void Since_StartsAtTheOldestEntryThatIsKept_WhenTheCallerFellBehind()
        {
            var buffer = Make(100);
            for (var index = 0; index < 500; index++)
            {
                buffer.Add(new Item());
            }

            var all = buffer.Since(3);

            Assert.Equal(buffer.Count, all.Count);
            Assert.Equal(buffer.OldestSequence, all[0].Number);
        }

        [Fact]
        public void Clear_LetsGoOfEverything_AndTheNumbersGoOn()
        {
            var buffer = Make(100);
            buffer.Add(new Item());
            buffer.Add(new Item());

            buffer.Clear();

            Assert.Equal(0, buffer.Count);
            Assert.Equal(2, buffer.Sequence);
            Assert.Equal(3, buffer.OldestSequence);
            Assert.Empty(buffer.Since(0));
            var next = new Item();
            Assert.Equal(3, buffer.Add(next));
            Assert.Equal(new[] { next }, buffer.Since(0));
        }

        [Fact]
        public async Task EveryThread_CanAdd_WithoutLosingOrRepeatingANumber()
        {
            var buffer = Make(100_000);

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var index = 0; index < 2_000; index++)
                {
                    buffer.Add(new Item());
                }
            })));

            Assert.Equal(16_000, buffer.Sequence);
            var numbers = buffer.Snapshot().Select(item => item.Number).ToArray();
            Assert.Equal(Enumerable.Range(1, 16_000).Select(number => (long)number), numbers);
        }

        [Fact]
        public void AListThatIsTooSmall_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(5));
            Assert.Throws<ArgumentNullException>(() => Make(100).Add(null!));
        }
    }
}
