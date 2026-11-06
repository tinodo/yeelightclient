namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// What the process holds: threads, handles and memory, taken after the garbage was collected, to see whether something is left behind by
    /// the connections that were made and ended (a socket that was not closed, a thread that waits for ever, a task that nothing ends).
    /// </summary>
    internal readonly record struct ResourceSnapshot(int Threads, int Handles, long ManagedBytes)
    {
        /// <summary>
        /// Takes the numbers, after a full collection (twice, so that what a finalizer releases is released too).
        /// </summary>
        public static ResourceSnapshot Take()
        {
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            using var process = Process.GetCurrentProcess();
            return new ResourceSnapshot(process.Threads.Count, process.HandleCount, GC.GetTotalMemory(forceFullCollection: true));
        }

        public override string ToString() => $"{this.Threads} threads, {this.Handles} handles, {this.ManagedBytes / (1024d * 1024d):0.0} MB";
    }
}
