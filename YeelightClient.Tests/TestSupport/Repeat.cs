namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Doing something again and again until it has had its effect: for what is sent over UDP, which can be lost when the receiver is busy.
    /// </summary>
    internal static class Repeat
    {
        /// <summary>
        /// Does <paramref name="action"/> until <paramref name="done"/> holds, with a pause in between.
        /// </summary>
        /// <param name="action">What is done (a datagram is sent).</param>
        /// <param name="done">Whether it had its effect.</param>
        /// <param name="what">What is waited for, for the message of the failure.</param>
        /// <param name="every">The pause between two times.</param>
        /// <param name="timeout">How long to go on.</param>
        public static async Task UntilAsync(Func<Task> action, Func<bool> done, string what, TimeSpan? every = null, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!done())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Timed out waiting for: " + what);
                }

                await action().ConfigureAwait(false);
                await Task.Delay(every ?? TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
            }
        }
    }
}
