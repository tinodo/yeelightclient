namespace Yeelight
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Keeps the commands to one device within what it accepts. A Yeelight device answers "client quota exceeded" to more than about
    /// 60 commands a minute on a connection, so an application that sends a command for every event would soon be refused. This is a token
    /// bucket: a short burst goes through at once, after that a command is let through at the pace of the refill, in order.
    /// </summary>
    internal sealed class YeelightRateLimiter
    {
        private readonly object _gate = new();
        private readonly double _capacity;
        private readonly double _tokensPerSecond;
        private readonly TimeProvider _time;
        private double _tokens;
        private long _timestamp;

        /// <param name="perMinute">The sustained number of commands a minute.</param>
        /// <param name="burst">How many commands can go through at once after a quiet moment.</param>
        /// <param name="time">The clock, to be able to test it.</param>
        public YeelightRateLimiter(int perMinute = 60, int burst = 10, TimeProvider? time = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(perMinute, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);

            this._time = time ?? TimeProvider.System;
            this._capacity = burst;
            this._tokens = burst;
            this._tokensPerSecond = perMinute / 60d;
            this._timestamp = this._time.GetTimestamp();
        }

        /// <summary>
        /// Takes a token when there is one.
        /// </summary>
        /// <returns><c>true</c> when the command may go now.</returns>
        public bool TryTake()
        {
            lock (this._gate)
            {
                this.Refill();
                if (this._tokens < 1)
                {
                    return false;
                }

                this._tokens -= 1;
                return true;
            }
        }

        /// <summary>
        /// Waits for a token, and refuses at once when it would take longer than <paramref name="maxWait"/>. The waiting commands
        /// get their turn in the order they asked.
        /// </summary>
        /// <param name="maxWait">The longest time that the caller is willing to wait.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <returns><c>true</c> when the command may go, <c>false</c> when it is refused because the wait would be too long.</returns>
        public async ValueTask<bool> WaitAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        {
            TimeSpan delay;
            lock (this._gate)
            {
                this.Refill();
                if (this._tokens >= 1)
                {
                    this._tokens -= 1;
                    return true;
                }

                delay = TimeSpan.FromSeconds((1 - this._tokens) / this._tokensPerSecond);
                if (delay > maxWait)
                {
                    return false;
                }

                // Reserve the token now (the balance goes below zero), so that the ones that ask after this one wait longer.
                this._tokens -= 1;
            }

            try
            {
                await Task.Delay(delay, this._time, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                lock (this._gate)
                {
                    this._tokens = Math.Min(this._capacity, this._tokens + 1);
                }

                throw;
            }
        }

        private void Refill()
        {
            var now = this._time.GetTimestamp();
            var elapsed = this._time.GetElapsedTime(this._timestamp, now).TotalSeconds;
            this._timestamp = now;
            this._tokens = Math.Min(this._capacity, this._tokens + (elapsed * this._tokensPerSecond));
        }
    }
}
