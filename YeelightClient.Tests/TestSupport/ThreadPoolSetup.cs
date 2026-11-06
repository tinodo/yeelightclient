namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// A machine with two or four cores runs thousands of tests that all wait for sockets, in parallel. The thread pool starts with as
    /// many threads as there are cores and adds one every half second when everyone is waiting, so a burst of tests that wait for another
    /// task (a sync Dispose, for one) can stall for seconds, which shows up as a timeout of a test that has nothing wrong. A pool that starts
    /// with enough threads does not stall.
    /// </summary>
    internal static class ThreadPoolSetup
    {
        private const int Threads = 128;

        [ModuleInitializer]
        internal static void Run()
        {
            ThreadPool.GetMinThreads(out var workers, out var completion);
            ThreadPool.SetMinThreads(Math.Max(workers, Threads), Math.Max(completion, Threads));
        }
    }
}
