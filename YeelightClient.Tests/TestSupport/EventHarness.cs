namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;

    /// <summary>
    /// A simulated light and a client that is connected to it, with a log of every event that the client raises about the light, from the
    /// moment that it is found. It does not read the properties of the light when it connects (<see cref="YeelightClientOptions.RefreshOnConnect"/>
    /// is off unless the settings say otherwise), so that the only event that the connection causes is the one about the connection
    /// itself, and a test knows what it is looking at.
    /// </summary>
    internal sealed class EventHarness : IAsyncDisposable
    {
        private EventHarness(SimulatedBulb bulb, YeelightClient client, YeelightDevice device, ClientEventLog log, ListLogger logger)
        {
            this.Bulb = bulb;
            this.Client = client;
            this.Device = device;
            this.Log = log;
            this.Logger = logger;
        }

        public SimulatedBulb Bulb { get; }

        public YeelightClient Client { get; }

        public YeelightDevice Device { get; }

        public ClientEventLog Log { get; }

        public ListLogger Logger { get; }

        /// <summary>
        /// Makes the light and the client, finds the light with a search that is sent straight to it, and waits until the event that says that
        /// the device is connected has been raised.
        /// </summary>
        /// <param name="bulbOptions">What the simulated light is made of.</param>
        /// <param name="options">Makes the settings of the client, given the port of the discovery of the light.</param>
        /// <param name="configure">Runs on the client before the light is looked for (to put a handler on its events first).</param>
        public static async Task<EventHarness> StartAsync(SimulatedBulbOptions? bulbOptions = null, Func<int, YeelightClientOptions>? options = null, Action<YeelightClient>? configure = null)
        {
            var bulb = new SimulatedBulb(bulbOptions);
            var logger = new ListLogger();
            var client = TestClients.Create(options?.Invoke(bulb.DiscoveryPort) ?? new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort, RefreshOnConnect = false }, logger, searchTarget: bulb);
            var log = new ClientEventLog(client);
            try
            {
                configure?.Invoke(client);

                // A search is a UDP datagram: on a machine that is busy the light can be slow to answer, so a probe that finds nothing is made again.
                YeelightDevice? found = null;
                for (var attempt = 0; attempt < 3 && found is null; attempt++)
                {
                    found = await client.ProbeAsync(IPAddress.Loopback);
                }

                var device = found ?? throw new InvalidOperationException("The simulated light did not answer the search.");
                await device.ConnectAsync();
                await log.WaitUntilAsync(events => events.Any(e => e.Kind == "State" && e.Property == nameof(YeelightDevice.IsConnected)), "the event that says that the device is connected");

                // The light registers a connection a moment after the client has made it: a report or a drop that comes first would reach nobody.
                await Wait.UntilAsync(() => bulb.ClientCount >= 1, "the light to have registered the connection");
                return new EventHarness(bulb, client, device, log, logger);
            }
            catch
            {
                await client.DisposeAsync();
                await bulb.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        /// Waits until the client has raised what the light did: a change that is made after a change that does nothing is raised once
        /// the first has been dealt with, because everything is in order.
        /// </summary>
        public async Task ChangeAndWaitAsync(string property, string value, string name)
        {
            var before = this.Log.CountOf("State", name);
            await this.Bulb.ChangeAsync((property, value));
            await this.Log.WaitUntilAsync(_ => this.Log.CountOf("State", name) > before, $"the event about {name}");
        }

        public async ValueTask DisposeAsync()
        {
            await this.Client.DisposeAsync();
            await this.Bulb.DisposeAsync();
        }
    }
}
