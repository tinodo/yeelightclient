namespace Yeelight.TestApp.Session
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using Microsoft.UI;
    using Microsoft.UI.Dispatching;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Media;
    using Windows.UI;
    using Yeelight.TestApp.Services;

    /// <summary>
    /// A light as the pages show it: the device of the client, and what a list needs to show of it (the name, the connection, the state, a
    /// swatch of the colour that it shows). The client raises its events on a thread of its own, so a change is handed to the thread of the
    /// user interface here: one refresh for all the changes that came in the meantime.
    /// </summary>
    internal sealed class LightItem : INotifyPropertyChanged
    {
        private readonly DispatcherQueue _dispatcher;
        private int _refreshQueued;
        private Brush _swatch = new SolidColorBrush(Colors.Transparent);

        /// <summary>Initializes a new instance of the <see cref="LightItem"/> class; call it on the thread of the user interface.</summary>
        /// <param name="device">The device.</param>
        /// <param name="isSimulated">Whether the device is one that the app simulates.</param>
        /// <param name="dispatcher">The dispatcher of the user interface.</param>
        public LightItem(YeelightDevice device, bool isSimulated, DispatcherQueue dispatcher)
        {
            this.Device = device;
            this.IsSimulated = isSimulated;
            this._dispatcher = dispatcher;
            this.UpdateSwatch();
        }

        /// <summary>Occurs, on the thread of the user interface, for every property of the item that may have changed.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Occurs, on the thread of the user interface, after the state of the light was read again.</summary>
        public event EventHandler? Changed;

        /// <summary>Gets the device of the client.</summary>
        public YeelightDevice Device { get; }

        /// <summary>Gets a value indicating whether the light is one that the app simulates.</summary>
        public bool IsSimulated { get; }

        /// <summary>Gets the id of the device.</summary>
        public string Id => this.Device.Id;

        /// <summary>Gets the name of the device, or its model when it has no name, or else its id.</summary>
        public string Title => this.Device.Name.Length > 0 ? this.Device.Name : this.Device.Model.Length > 0 ? this.Device.Model : this.Device.Id;

        /// <summary>Gets the model, the id and the address on one line.</summary>
        public string Subtitle => string.Create(
            CultureInfo.InvariantCulture,
            $"{(this.Device.Model.Length > 0 ? this.Device.Model : "unknown model")} \u00b7 {this.Device.Id} \u00b7 {this.AddressText}");

        /// <summary>Gets the address and the port of the control service.</summary>
        public string AddressText => this.Device.Address is { } address ? string.Create(CultureInfo.InvariantCulture, $"{address}:{this.Device.Port}") : this.Device.Location;

        /// <summary>Gets a value indicating whether there is a control connection to the device.</summary>
        public bool IsConnected => this.Device.IsConnected;

        /// <summary>Gets a value indicating whether the main light is on.</summary>
        public bool IsOn => this.Device.IsOn;

        /// <summary>Gets the connection in words.</summary>
        public string ConnectionText => this.Device.IsConnected ? "Connected" : "Not connected";

        /// <summary>Gets the state of the main light in words: on and how bright, or off.</summary>
        public string StateText => this.Device.IsOn
            ? (this.Device.Brightness > 0 ? string.Create(CultureInfo.InvariantCulture, $"On \u00b7 {this.Device.Brightness}%") : "On")
            : "Off";

        /// <summary>Gets the colour that the light shows, as a brush for a swatch.</summary>
        public Brush SwatchBrush => this._swatch;

        /// <summary>Gets how visible the swatch is: a light that is off is dim.</summary>
        public double SwatchOpacity => this.Device.IsOn ? 1.0 : 0.3;

        /// <summary>Gets how long ago the device was last heard from.</summary>
        public string LastSeenText => LightFormat.LastSeen(this.Device.LastSeen, DateTimeOffset.Now);

        /// <summary>Gets the name that a screen reader says for the item.</summary>
        public string AutomationName => $"{this.Title}, {this.ConnectionText}, {this.StateText}";

        /// <summary>Gets the visibility of the label that says that the light is simulated.</summary>
        public Visibility SimulatedVisibility => this.IsSimulated ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Asks for the item to be read again on the thread of the user interface. Safe to call from every thread; many calls make one refresh.</summary>
        public void RequestRefresh()
        {
            if (Interlocked.Exchange(ref this._refreshQueued, 1) == 0 && !this._dispatcher.TryEnqueue(this.Refresh))
            {
                Interlocked.Exchange(ref this._refreshQueued, 0);
            }
        }

        /// <summary>Reads the item again: every binding is updated, and <see cref="Changed"/> is raised. Call it on the thread of the user interface.</summary>
        public void Refresh()
        {
            Interlocked.Exchange(ref this._refreshQueued, 0);
            this.UpdateSwatch();
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Updates the time since the light was last heard from, which the library does not raise an event for.</summary>
        public void Tick()
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.LastSeenText)));
        }

        /// <inheritdoc/>
        public override string ToString() => this.Title;

        private void UpdateSwatch()
        {
            var color = LightFormat.Swatch(this.Device);
            this._swatch = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
        }
    }
}
