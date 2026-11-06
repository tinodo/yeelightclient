namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Keeps what a handler was told, in the order in which it was told, for a test to look at: handlers run on other threads, so a test
    /// cannot simply read a list that they add to.
    /// </summary>
    internal sealed class EventRecorder<T>
    {
        private readonly object _gate = new();
        private readonly List<T> _items = [];

        /// <summary>Gets what was recorded, as it is now.</summary>
        public IReadOnlyList<T> Items
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._items];
                }
            }
        }

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

        public void Add(T item)
        {
            lock (this._gate)
            {
                this._items.Add(item);
            }
        }

        public Task WaitForCountAsync(int count, string what, TimeSpan? timeout = null)
        {
            return Wait.UntilAsync(() => this.Count >= count, $"{count} x {what}", timeout);
        }

        public Task WaitUntilAsync(Func<IReadOnlyList<T>, bool> condition, string what, TimeSpan? timeout = null)
        {
            return Wait.UntilAsync(() => condition(this.Items), what, timeout);
        }
    }

    /// <summary>
    /// Makes an event of the code under test into something that can be awaited, the first time that it is raised.
    /// </summary>
    internal sealed class EventAwaiter<TEventArgs>
    {
        private readonly TaskCompletionSource<TEventArgs> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EventAwaiter()
        {
            this.Handler = (_, e) => this._source.TrySetResult(e);
        }

        public EventHandler<TEventArgs> Handler { get; }

        public Task<TEventArgs> Task => this._source.Task;
    }

    /// <summary>
    /// Counts how often something happened, from any thread.
    /// </summary>
    internal sealed class HitCounter
    {
        private int _count;

        public int Value => Volatile.Read(ref this._count);

        public int Increment() => Interlocked.Increment(ref this._count);
    }
}
