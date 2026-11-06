namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading;

    /// <summary>
    /// A clock that only moves when a test says so, for code that takes a <see cref="TimeProvider"/>.
    /// </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private long _ticks;

        /// <summary>
        /// Moves the clock forward.
        /// </summary>
        public void Advance(TimeSpan time)
        {
            lock (this._gate)
            {
                this._ticks += time.Ticks;
            }
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (this._gate)
            {
                return this._ticks;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (this._gate)
            {
                return new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + this._ticks, TimeSpan.Zero);
            }
        }
    }
}
