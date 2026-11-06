namespace Yeelight
{
    using System;

    /// <summary>
    /// The arguments of <see cref="YeelightClient.DeviceStateChanged"/>.
    /// </summary>
    public sealed class DeviceStateChangedEventArgs : EventArgs
    {
        internal DeviceStateChangedEventArgs(YeelightDevice device, YeelightLight light, string propertyName)
        {
            this.Device = device;
            this.Light = light;
            this.PropertyName = propertyName;
        }

        /// <summary>
        /// Gets the light whose state changed.
        /// </summary>
        public YeelightDevice Device { get; }

        /// <summary>
        /// Gets the part of the device that changed: the device itself (its main light, and what concerns the device as a whole), or its
        /// <see cref="YeelightDevice.Background"/> light.
        /// </summary>
        public YeelightLight Light { get; }

        /// <summary>
        /// Gets the name of the property that changed, for example <c>Brightness</c> or <c>IsConnected</c>.
        /// </summary>
        public string PropertyName { get; }
    }
}
