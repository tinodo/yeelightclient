namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A list that never grows past a capacity, for what the app records while it runs (the log, the events): the oldest entries are let go of
    /// when there are too many, so that a session that runs for days does not run out of memory. Every entry gets a number that only grows
    /// (<see cref="Sequence"/>), which is how a page asks for what is new since it looked last. It is safe to use from every thread.
    /// </summary>
    /// <typeparam name="T">The type of the entries.</typeparam>
    internal sealed class BoundedBuffer<T>
        where T : class
    {
        private readonly object _gate = new();
        private readonly List<T> _items = [];
        private readonly Action<T, long> _number;
        private readonly int _capacity;
        private readonly int _slack;
        private long _last;
        private long _first = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundedBuffer{T}"/> class.
        /// </summary>
        /// <param name="capacity">How many entries are kept, at least 10.</param>
        /// <param name="number">Gives an entry its number, when it is added.</param>
        public BoundedBuffer(int capacity, Action<T, long> number)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 10);
            ArgumentNullException.ThrowIfNull(number);
            this._capacity = capacity;
            this._slack = Math.Max(1, capacity / 10);
            this._number = number;
        }

        /// <summary>Gets the number of the entry that was added last (0 when none was added yet). It never decreases, not even when the entries are cleared.</summary>
        public long Sequence
        {
            get
            {
                lock (this._gate)
                {
                    return this._last;
                }
            }
        }

        /// <summary>Gets the number of the oldest entry that is still kept, or the number that the next entry gets when there are none.</summary>
        public long OldestSequence
        {
            get
            {
                lock (this._gate)
                {
                    return this._first;
                }
            }
        }

        /// <summary>Gets the number of entries that are kept.</summary>
        public int Count
        {
            get
            {
                lock (this._gate)
                {
                    return this._items.Count;
                }
            }
        }

        /// <summary>Adds an entry, and numbers it.</summary>
        /// <param name="item">The entry.</param>
        /// <returns>The number of the entry.</returns>
        public long Add(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            lock (this._gate)
            {
                this._last++;
                this._number(item, this._last);
                this._items.Add(item);

                // Let go of a tenth at a time, so that the cost of removing from the front is not paid for every entry that is added.
                if (this._items.Count > this._capacity + this._slack)
                {
                    var remove = this._items.Count - this._capacity;
                    this._items.RemoveRange(0, remove);
                    this._first += remove;
                }

                return this._last;
            }
        }

        /// <summary>Gets a copy of the entries that are kept, the oldest first.</summary>
        /// <returns>The entries.</returns>
        public IReadOnlyList<T> Snapshot()
        {
            lock (this._gate)
            {
                return [.. this._items];
            }
        }

        /// <summary>Gets the entries that were added after the one with a given number.</summary>
        /// <param name="sequence">The number of the last entry that the caller has seen (0 for none).</param>
        /// <returns>The entries that are newer and still kept, the oldest first.</returns>
        public IReadOnlyList<T> Since(long sequence)
        {
            lock (this._gate)
            {
                var index = (int)Math.Clamp(sequence + 1 - this._first, 0, this._items.Count);
                return index == 0 ? [.. this._items] : this._items.GetRange(index, this._items.Count - index);
            }
        }

        /// <summary>Lets go of every entry. The numbers continue where they were.</summary>
        public void Clear()
        {
            lock (this._gate)
            {
                this._items.Clear();
                this._first = this._last + 1;
            }
        }
    }
}
