namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Waiting for something that happens on another thread.
    /// </summary>
    internal static class Wait
    {
        public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Timed out waiting for: " + what);
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        /// <summary>Waits for something that must not happen: the time passes and the condition stays false.</summary>
        public static async Task NotHappensAsync(Func<bool> condition, string what, TimeSpan? time = null)
        {
            var deadline = DateTime.UtcNow + (time ?? TimeSpan.FromMilliseconds(300));
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    Assert.Fail("Happened, but it should not have: " + what);
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
