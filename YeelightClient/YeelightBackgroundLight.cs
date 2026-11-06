namespace Yeelight
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The background light of a device that has one (some ceiling lights): its state, and the same commands as the main light.
    /// </summary>
    /// <remarks>
    /// Get it from <see cref="YeelightDevice.Background"/>. Every command is the background twin of that of the main light: the protocol
    /// names them <c>bg_set_power</c>, <c>bg_set_bright</c>, and so on. The commands share the connection of the device, and so its limit of
    /// 60 commands a minute. A device that has no background light does not list these methods, and the commands throw a
    /// <see cref="System.NotSupportedException"/>.
    /// </remarks>
    public sealed class YeelightBackgroundLight : YeelightLight
    {
        private readonly YeelightDevice _device;

        internal YeelightBackgroundLight(YeelightDevice device)
            : base(device.Context, YeelightProperties.BackgroundPrefix)
        {
            this._device = device;
        }

        /// <inheritdoc/>
        internal override YeelightDevice Owner => this._device;

        /// <summary>
        /// Takes over a property of the background light that the device reported (<c>bg_power</c>, <c>bg_lmode</c>, ...).
        /// </summary>
        /// <returns>Whether the property is one of the background light.</returns>
        internal bool ApplyBackgroundProperty(string key, string value)
        {
            if (!key.StartsWith(YeelightProperties.BackgroundPrefix, System.StringComparison.Ordinal))
            {
                return false;
            }

            // The mode of the background light is bg_lmode, where the main light has color_mode.
            var logical = key == YeelightProperties.BackgroundColorMode
                ? YeelightProperties.ColorMode
                : key[YeelightProperties.BackgroundPrefix.Length..];
            return this.ApplyState(logical, value);
        }

        /// <inheritdoc/>
        internal override Task<YeelightResult> ExecuteAsync(string method, IReadOnlyList<object?> parameters, CancellationToken cancellationToken)
        {
            return this._device.ExecuteAsync(method, parameters, cancellationToken);
        }

        /// <inheritdoc/>
        internal override bool AllowsMethod(string method)
        {
            return this._device.AllowsMethod(method);
        }
    }
}
