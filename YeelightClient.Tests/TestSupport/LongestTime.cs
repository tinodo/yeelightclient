namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading;

    /// <summary>
    /// Keeps the longest of the times that it is given, from any thread.
    /// </summary>
    internal sealed class LongestTime
    {
        private long _ticks;

        public TimeSpan Value => TimeSpan.FromTicks(Volatile.Read(ref this._ticks));

        public void Add(TimeSpan time)
        {
            long seen;
            do
            {
                seen = Volatile.Read(ref this._ticks);
            }
            while (time.Ticks > seen && Interlocked.CompareExchange(ref this._ticks, time.Ticks, seen) != seen);
        }
    }
}
