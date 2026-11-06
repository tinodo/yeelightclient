namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Bounds for waiting: no test waits for ever, and a wait that runs out says what it was for.
    /// </summary>
    internal static class AsyncExtensions
    {
        /// <summary>How long a test waits for something that has to happen. It only matters when something is wrong: a busy machine needs a few seconds, not more.</summary>
        public static readonly TimeSpan Generous = TimeSpan.FromSeconds(15);

        public static async Task<T> WithTimeoutAsync<T>(this Task<T> task, string what, TimeSpan? timeout = null)
        {
            await ((Task)task).WithTimeoutAsync(what, timeout).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        public static async Task WithTimeoutAsync(this Task task, string what, TimeSpan? timeout = null)
        {
            var limit = timeout ?? Generous;
            using var delay = new CancellationTokenSource();
            var finished = await Task.WhenAny(task, Task.Delay(limit, delay.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(finished, task))
            {
                Assert.Fail($"Did not finish within {limit.TotalSeconds:0.#} seconds: {what}");
            }

            delay.Cancel();
            await task.ConfigureAwait(false);
        }
    }
}
