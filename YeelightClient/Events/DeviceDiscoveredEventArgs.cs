namespace Yeelight
{
    using System;

    /// <summary>
    /// The arguments of <see cref="YeelightClient.DeviceDiscovered"/>.
    /// </summary>
    public sealed class DeviceDiscoveredEventArgs : EventArgs
    {
        internal DeviceDiscoveredEventArgs(YeelightDevice device)
        {
            this.Device = device;
        }

        /// <summary>
        /// Gets the light that was found. It is in <see cref="YeelightClient.Devices"/> by now.
        /// </summary>
        public YeelightDevice Device { get; }
    }
}
