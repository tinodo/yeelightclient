namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A client and a device that talk to a simulated light on the loopback, for the tests that need the whole way from the API to the wire.
    /// </summary>
    internal sealed class Harness : IAsyncDisposable
    {
        private Harness(SimulatedBulb bulb, YeelightClient client, YeelightDevice device, ListLogger logger)
        {
            this.Bulb = bulb;
            this.Client = client;
            this.Device = device;
            this.Logger = logger;
        }

        public SimulatedBulb Bulb { get; }

        public YeelightClient Client { get; }

        public YeelightDevice Device { get; }

        public ListLogger Logger { get; }

        /// <summary>
        /// Makes a simulated light, a client, and finds the light with a search that is sent straight to it (so the client never leaves the
        /// loopback). The device is connected when this returns.
        /// </summary>
        /// <param name="bulbOptions">What the simulated light is made of.</param>
        /// <param name="options">Makes the settings of the client, given the port of the discovery of the simulated light (which the settings have to carry).</param>
        public static async Task<Harness> StartAsync(SimulatedBulbOptions? bulbOptions = null, Func<int, YeelightClientOptions>? options = null)
        {
            var bulb = new SimulatedBulb(bulbOptions);
            var logger = new ListLogger();
            var client = TestClients.Create(options?.Invoke(bulb.DiscoveryPort) ?? new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, logger, searchTarget: bulb);
            try
            {
                // A search is a UDP datagram: on a machine that is busy the light can be slow to answer, so a probe that finds nothing is made again.
                YeelightDevice? device = null;
                for (var attempt = 0; attempt < 3 && device is null; attempt++)
                {
                    device = await client.ProbeAsync(IPAddress.Loopback);
                }

                if (device is null)
                {
                    throw new InvalidOperationException("The simulated light did not answer the search.");
                }

                await device.ConnectAsync();
                return new Harness(bulb, client, device, logger);
            }
            catch
            {
                await client.DisposeAsync();
                await bulb.DisposeAsync();
                throw;
            }
        }

        /// <summary>Waits until the light has seen the number of commands (the read of the properties after connecting counts as one).</summary>
        public Task CommandsAsync(int count)
        {
            return Wait.UntilAsync(() => this.Bulb.Requests.Count >= count, $"{count} commands at the light");
        }

        public async ValueTask DisposeAsync()
        {
            await this.Client.DisposeAsync();
            await this.Bulb.DisposeAsync();
        }
    }

    /// <summary>
    /// A logger that keeps what it is told, to look at.
    /// </summary>
    internal sealed class ListLogger : ILogger<YeelightClient>
    {
        private readonly object _gate = new();
        private readonly System.Collections.Generic.List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public System.Collections.Generic.IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (this._gate)
            {
                this._entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
