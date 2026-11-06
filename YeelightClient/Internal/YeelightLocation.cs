namespace Yeelight
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The location of a Yeelight device: <c>yeelight://192.168.1.239:55443</c>, the address and the port of its control service.
    /// </summary>
    internal static class YeelightLocation
    {
        private const string Scheme = "yeelight://";

        /// <summary>
        /// Splits a location into the address and the port.
        /// </summary>
        /// <returns><c>false</c> for a text that is not a location (no scheme, no address, a port that is not 1 to 65535).</returns>
        public static bool TryParse(string? location, out string host, out int port)
        {
            host = string.Empty;
            port = 0;
            if (location == null || !location.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var address = location[Scheme.Length..].TrimEnd('/');
            var colon = address.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(address[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort is < 1 or > 65535)
            {
                return false;
            }

            port = parsedPort;
            host = address[..colon];
            return host.Length > 0;
        }

        /// <summary>
        /// Makes the location of an address and a port.
        /// </summary>
        public static string Format(string host, int port)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Scheme}{host}:{port}");
        }
    }
}
