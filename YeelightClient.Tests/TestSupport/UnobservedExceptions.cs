namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Collects the exceptions of tasks that nobody looked at: a task that fails and is never awaited reports it when it is finalized, and
    /// nothing else says so. A soak test collects them while it runs and looks after it (the garbage is collected first, which is when they
    /// are reported). Only one soak test runs at a time, so what is collected is the work of that test.
    /// </summary>
    internal sealed class UnobservedExceptions : IDisposable
    {
        private readonly ConcurrentQueue<Exception> _exceptions = new();

        public UnobservedExceptions()
        {
            TaskScheduler.UnobservedTaskException += this.OnUnobserved;
        }

        /// <summary>
        /// Collects the garbage, so that the tasks that were not observed report their exceptions, and gives them.
        /// </summary>
        public IReadOnlyList<Exception> Collect()
        {
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            var all = new List<Exception>();
            foreach (var exception in this._exceptions)
            {
                if (exception is AggregateException aggregate)
                {
                    all.AddRange(aggregate.Flatten().InnerExceptions);
                }
                else
                {
                    all.Add(exception);
                }
            }

            return all;
        }

        public void Dispose()
        {
            TaskScheduler.UnobservedTaskException -= this.OnUnobserved;
        }

        private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            this._exceptions.Enqueue(e.Exception);
            e.SetObserved();
        }
    }

    /// <summary>
    /// Counts what happened, by name, from any thread.
    /// </summary>
    internal sealed class Tally
    {
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int Total => this._counts.Values.Sum();

        public int this[string name] => this._counts.TryGetValue(name, out var count) ? count : 0;

        public void Add(string name) => this._counts.AddOrUpdate(name, 1, (_, count) => count + 1);

        public override string ToString() => string.Join(", ", this._counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
    }
}
