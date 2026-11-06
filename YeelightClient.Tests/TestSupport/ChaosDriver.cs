namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// How often and how hard the chaos strikes: it comes in bursts (a few cuts, stalls and refusals close together) with calm in between.
    /// </summary>
    internal sealed record ChaosStyle
    {
        /// <summary>The calm between two bursts.</summary>
        public TimeSpan CalmMin { get; init; } = TimeSpan.FromMilliseconds(300);

        public TimeSpan CalmMax { get; init; } = TimeSpan.FromMilliseconds(1500);

        /// <summary>The number of things that happen in a burst.</summary>
        public int ActionsMin { get; init; } = 1;

        public int ActionsMax { get; init; } = 4;

        /// <summary>The time between two things of a burst.</summary>
        public TimeSpan GapMin { get; init; } = TimeSpan.FromMilliseconds(20);

        public TimeSpan GapMax { get; init; } = TimeSpan.FromMilliseconds(200);

        /// <summary>How long a stall lasts.</summary>
        public TimeSpan StallMin { get; init; } = TimeSpan.FromMilliseconds(20);

        public TimeSpan StallMax { get; init; } = TimeSpan.FromMilliseconds(300);

        /// <summary>How long a light refuses connections (as it does while it starts).</summary>
        public TimeSpan RefuseMin { get; init; } = TimeSpan.FromMilliseconds(20);

        public TimeSpan RefuseMax { get; init; } = TimeSpan.FromMilliseconds(400);

        /// <summary>The style of a client that waits a second or more to connect again: bursts now and then, so that it is up in between.</summary>
        public static ChaosStyle Gentle(bool longRun) => longRun
            ? new ChaosStyle { CalmMin = TimeSpan.FromSeconds(2), CalmMax = TimeSpan.FromSeconds(12) }
            : new ChaosStyle();

        /// <summary>The style for a connection that connects again after a few milliseconds: something happens all the time.</summary>
        public static ChaosStyle Fierce() => new()
        {
            CalmMin = TimeSpan.FromMilliseconds(2),
            CalmMax = TimeSpan.FromMilliseconds(30),
            ActionsMin = 1,
            ActionsMax = 3,
            GapMin = TimeSpan.FromMilliseconds(1),
            GapMax = TimeSpan.FromMilliseconds(12),
            StallMin = TimeSpan.FromMilliseconds(2),
            StallMax = TimeSpan.FromMilliseconds(40),
            RefuseMin = TimeSpan.FromMilliseconds(5),
            RefuseMax = TimeSpan.FromMilliseconds(60),
        };
    }

    /// <summary>
    /// Makes a <see cref="ChaosProxy"/> misbehave at random, from a seed: the same seed makes the same sequence of things (how they fall in
    /// time depends on the machine). Cuts, stalls and refusals are counted.
    /// </summary>
    internal sealed class ChaosDriver : IAsyncDisposable
    {
        private readonly ChaosProxy _proxy;
        private readonly Random _random;
        private readonly ChaosStyle _style;
        private readonly CancellationTokenSource _stop = new();
        private Task? _running;
        private int _cuts;
        private int _stalls;
        private int _refusals;
        private int _bursts;

        public ChaosDriver(ChaosProxy proxy, int seed, ChaosStyle style)
        {
            this._proxy = proxy;
            this._random = new Random(seed);
            this._style = style;
        }

        /// <summary>Gets the number of times that the connections were cut.</summary>
        public int Cuts => Volatile.Read(ref this._cuts);

        public int Stalls => Volatile.Read(ref this._stalls);

        public int Refusals => Volatile.Read(ref this._refusals);

        public int Bursts => Volatile.Read(ref this._bursts);

        public void Start() => this._running = Task.Run(this.RunAsync);

        /// <summary>
        /// Ends the chaos, and lets everything through again: no stall, no refusal.
        /// </summary>
        public async Task StopAsync()
        {
            this._stop.Cancel();
            if (this._running is not null)
            {
                await this._running.ConfigureAwait(false);
            }

            this._proxy.Refuse(false);
            this._proxy.EndStall();
        }

        public async ValueTask DisposeAsync()
        {
            await this.StopAsync().ConfigureAwait(false);
            this._stop.Dispose();
        }

        private async Task RunAsync()
        {
            var token = this._stop.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(this.Between(this._style.CalmMin, this._style.CalmMax), token).ConfigureAwait(false);
                    Interlocked.Increment(ref this._bursts);
                    var actions = this._random.Next(this._style.ActionsMin, this._style.ActionsMax + 1);
                    for (var index = 0; index < actions; index++)
                    {
                        await this.ActAsync(token).ConfigureAwait(false);
                        await Task.Delay(this.Between(this._style.GapMin, this._style.GapMax), token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The end of the chaos.
            }
        }

        private async Task ActAsync(CancellationToken token)
        {
            switch (this._random.Next(10))
            {
                case < 5:
                    this._proxy.Cut();
                    Interlocked.Increment(ref this._cuts);
                    break;
                case < 7:
                    this._proxy.Stall(this.Between(this._style.StallMin, this._style.StallMax));
                    Interlocked.Increment(ref this._stalls);
                    break;
                case < 9:
                    // A light that is starting: the connection goes, and the new ones are reset until it is up.
                    this._proxy.Refuse(true);
                    this._proxy.Cut();
                    Interlocked.Increment(ref this._cuts);
                    Interlocked.Increment(ref this._refusals);
                    try
                    {
                        await Task.Delay(this.Between(this._style.RefuseMin, this._style.RefuseMax), token).ConfigureAwait(false);
                    }
                    finally
                    {
                        this._proxy.Refuse(false);
                    }

                    break;
                default:
                    this._proxy.Cut();
                    this._proxy.Cut();
                    Interlocked.Increment(ref this._cuts);
                    break;
            }
        }

        private TimeSpan Between(TimeSpan minimum, TimeSpan maximum)
        {
            return minimum >= maximum ? minimum : TimeSpan.FromMilliseconds(this._random.Next((int)minimum.TotalMilliseconds, (int)maximum.TotalMilliseconds + 1));
        }
    }
}
