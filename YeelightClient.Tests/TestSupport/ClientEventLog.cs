namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// An event that a client raised about a device.
    /// </summary>
    /// <param name="Sequence">Its place among the events of the log, from 1, in the order in which they were raised.</param>
    /// <param name="Kind"><c>Discovered</c> (DeviceDiscovered), <c>State</c> (DeviceStateChanged) or <c>Property</c> (PropertyChanged of a light).</param>
    /// <param name="Device">The device that it is about.</param>
    /// <param name="Light">The light that changed (<c>null</c> for a discovery).</param>
    /// <param name="Property">The name of the property that changed (<c>null</c> for a discovery).</param>
    /// <param name="ThreadId">The managed thread that the handler ran on.</param>
    internal sealed record DeviceEvent(int Sequence, string Kind, YeelightDevice Device, YeelightLight? Light, string? Property, int ThreadId);

    /// <summary>
    /// Records every event of a client that is about a device (a light that was found, a property of a light that changed), in the
    /// order in which the handlers were called, together with the <c>PropertyChanged</c> of every light of every device that the client finds:
    /// the handlers are put on the lights when the client says that it found them, which is before anything else is raised about them.
    /// </summary>
    internal sealed class ClientEventLog
    {
        private readonly EventRecorder<DeviceEvent> _events = new();
        private int _sequence;

        public ClientEventLog(YeelightClient client)
        {
            client.DeviceDiscovered += (_, e) =>
            {
                this.Add("Discovered", e.Device, null, null);
                e.Device.PropertyChanged += (sender, args) => this.Add("Property", e.Device, (YeelightLight)sender!, args.PropertyName);
                e.Device.Background.PropertyChanged += (sender, args) => this.Add("Property", e.Device, (YeelightLight)sender!, args.PropertyName);
            };
            client.DeviceStateChanged += (_, e) => this.Add("State", e.Device, e.Light, e.PropertyName);
        }

        /// <summary>Gets every event, as it is now.</summary>
        public IReadOnlyList<DeviceEvent> Events => this._events.Items;

        /// <summary>The names of the properties that changed on a light (<c>PropertyChanged</c>), in order.</summary>
        public IReadOnlyList<string> PropertiesOf(YeelightLight light)
        {
            return [.. this.Events.Where(e => e.Kind == "Property" && ReferenceEquals(e.Light, light)).Select(e => e.Property!)];
        }

        /// <summary>The names of the properties that changed on a light, as <see cref="YeelightClient.DeviceStateChanged"/> told it, in order.</summary>
        public IReadOnlyList<string> StatesOf(YeelightLight light)
        {
            return [.. this.Events.Where(e => e.Kind == "State" && ReferenceEquals(e.Light, light)).Select(e => e.Property!)];
        }

        public int CountOf(string kind, string? property = null)
        {
            return this.Events.Count(e => e.Kind == kind && (property is null || e.Property == property));
        }

        public System.Threading.Tasks.Task WaitUntilAsync(Func<IReadOnlyList<DeviceEvent>, bool> condition, string what, TimeSpan? timeout = null)
        {
            return this._events.WaitUntilAsync(condition, what, timeout);
        }

        private void Add(string kind, YeelightDevice device, YeelightLight? light, string? property)
        {
            // The number and the entry are made under one lock, or two threads could put them in the list in the other order.
            lock (this._events)
            {
                this._events.Add(new DeviceEvent(Interlocked.Increment(ref this._sequence), kind, device, light, property, Environment.CurrentManagedThreadId));
            }
        }
    }
}
