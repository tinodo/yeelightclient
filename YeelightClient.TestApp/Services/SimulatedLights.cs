namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>The kinds of light that the app can simulate.</summary>
    internal enum SimulatedKind
    {
        /// <summary>A light that shows colours and white of every colour temperature.</summary>
        Color,

        /// <summary>A white light that can only be dimmed: it lists no colour methods.</summary>
        White,

        /// <summary>A ceiling light that has a second light, the background light.</summary>
        Ceiling,
    }

    /// <summary>A light that the app simulates: the simulator of the tests, which answers on the loopback and nowhere else.</summary>
    internal sealed class SimulatedLight
    {
        /// <summary>Initializes a new instance of the <see cref="SimulatedLight"/> class.</summary>
        /// <param name="kind">The kind of light.</param>
        /// <param name="bulb">The simulator.</param>
        public SimulatedLight(SimulatedKind kind, SimulatedBulb bulb)
        {
            this.Kind = kind;
            this.Bulb = bulb;
        }

        /// <summary>Gets the kind of light.</summary>
        public SimulatedKind Kind { get; }

        /// <summary>Gets the simulator: what it received, and what state it is in.</summary>
        public SimulatedBulb Bulb { get; }

        /// <summary>Gets the id of the light, which is what a client knows it by.</summary>
        public string Id => this.Bulb.Id;

        /// <summary>Gets the UDP port that the light answers a search on.</summary>
        public int DiscoveryPort => this.Bulb.DiscoveryPort;

        /// <summary>Gets the light in words, for a message.</summary>
        public string Description => $"{SimulatedLights.NameOf(this.Kind)} ({this.Id}), searches on UDP 127.0.0.1:{this.DiscoveryPort}, control on TCP {this.Bulb.ControlPort}";
    }

    /// <summary>
    /// The lights that the app simulates, so that anybody can try the app without a light: a colour bulb, a white bulb that cannot show
    /// colours, and a ceiling light with a background light. They are in this process and on the loopback: nothing that they do leaves the
    /// computer, and no real light is asked or answered. A client finds them with a probe (<see cref="YeelightClient.ProbeAsync(System.Net.IPAddress, int, System.Threading.CancellationToken)"/>).
    /// </summary>
    internal sealed class SimulatedLights : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly List<SimulatedLight> _running = [];

        /// <summary>Gets the lights that are running.</summary>
        public IReadOnlyList<SimulatedLight> Running
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._running];
                }
            }
        }

        /// <summary>Gets the name of a kind of light.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The words.</returns>
        public static string NameOf(SimulatedKind kind)
        {
            return kind switch
            {
                SimulatedKind.Color => "Simulated colour bulb",
                SimulatedKind.White => "Simulated white bulb",
                _ => "Simulated ceiling light",
            };
        }

        /// <summary>Gets what a kind of light is made of: its id and name are its own, and the methods it lists are those of such a light.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The options of the simulator.</returns>
        public static SimulatedBulbOptions OptionsFor(SimulatedKind kind)
        {
            return kind switch
            {
                SimulatedKind.Color => new SimulatedBulbOptions
                {
                    Id = "0x00000000000a0001",
                    Model = "color",
                    Name = NameOf(kind),
                },
                SimulatedKind.White => new SimulatedBulbOptions
                {
                    Id = "0x00000000000a0002",
                    Model = "mono",
                    Name = NameOf(kind),
                    Support = "get_prop set_default set_power toggle set_bright start_cf stop_cf set_scene cron_add cron_get cron_del set_adjust adjust_bright set_music set_name",
                },
                _ => new SimulatedBulbOptions
                {
                    Id = "0x00000000000a0003",
                    Model = "ceiling",
                    Name = NameOf(kind),
                    HasBackground = true,
                },
            };
        }

        /// <summary>Starts a simulated light, or returns the one of that kind that is running.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The light.</returns>
        public SimulatedLight Start(SimulatedKind kind)
        {
            lock (this._gate)
            {
                var running = this._running.FirstOrDefault(light => light.Kind == kind);
                if (running is not null)
                {
                    return running;
                }

                var light = new SimulatedLight(kind, new SimulatedBulb(OptionsFor(kind)));
                this._running.Add(light);
                return light;
            }
        }

        /// <summary>Finds a running light by its id.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The light, or <c>null</c> when none of the simulated lights has it.</returns>
        public SimulatedLight? Find(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            lock (this._gate)
            {
                return this._running.FirstOrDefault(light => string.Equals(light.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Stops the simulated lights, and closes their connections.</summary>
        /// <returns>The lights that were stopped.</returns>
        public async Task<IReadOnlyList<SimulatedLight>> StopAllAsync()
        {
            SimulatedLight[] stopping;
            lock (this._gate)
            {
                stopping = [.. this._running];
                this._running.Clear();
            }

            await Task.WhenAll(stopping.Select(light => light.Bulb.DisposeAsync().AsTask())).ConfigureAwait(false);
            return stopping;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await this.StopAllAsync().ConfigureAwait(false);
        }
    }
}
