namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>Helpers of the tests of the test app that start a light.</summary>
    internal static class HarnessExtensions
    {
        /// <summary>
        /// Waits until the client has taken over the answer to the read of all the properties that it makes when it connects. A light
        /// is simulated by code that answers each command on a task of its own, so the answer to that read can overtake what a test
        /// does right after the start: it carries the state from before, and it would undo a change that the test made.
        /// </summary>
        /// <param name="harness">The client and the simulated light.</param>
        /// <returns>A task that ends when the answer has come, and has been taken over.</returns>
        public static async Task SettleAsync(this Harness harness)
        {
            await Wait.UntilAsync(
                () => harness.Logger.Entries.Any(entry => entry.Message.Contains("Received: ", StringComparison.Ordinal) && entry.Message.Contains("\"result\":[\"on\"", StringComparison.Ordinal)),
                "the first read of the properties");

            // The line is logged before the answer is taken over, on the thread that reads the connection, so the state may not be there yet.
            // That thread is done with an answer before it reads the next line: when the answer to a second read is here, the first is taken over.
            await harness.Device.GetPropertiesAsync([YeelightProperties.Power]);
        }
    }
}
