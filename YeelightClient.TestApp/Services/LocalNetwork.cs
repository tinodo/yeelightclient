namespace Yeelight.TestApp.Services
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>What the app needs to know about the networks of this computer, without sending anything to them.</summary>
    internal static class LocalNetwork
    {
        /// <summary>
        /// Reads what a person typed as the address of a light: an IP address, with a port after it when the light is behind a forwarded
        /// port or is a simulator (<c>192.168.1.50</c>, <c>127.0.0.1:55000</c>, <c>::1</c>, <c>[::1]:55000</c>).
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="defaultPort">The port when the text has none.</param>
        /// <param name="address">The address, when the text could be read.</param>
        /// <param name="port">The port, when the text could be read.</param>
        /// <param name="error">What is wrong with the text, in words, when it could not be read.</param>
        /// <returns><c>true</c> when the text could be read.</returns>
        public static bool TryParseEndpoint(string? text, int defaultPort, out IPAddress? address, out int port, out string error)
        {
            address = null;
            port = defaultPort;
            error = string.Empty;
            var trimmed = text?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                error = "Type the IP address of a light, for example 192.168.1.50.";
                return false;
            }

            var portText = string.Empty;
            var hasPort = false;
            var addressText = trimmed;
            if (trimmed.StartsWith('['))
            {
                var close = trimmed.IndexOf(']');
                if (close < 0)
                {
                    error = $"'{trimmed}' has an opening bracket and no closing one.";
                    return false;
                }

                addressText = trimmed[1..close];
                var rest = trimmed[(close + 1)..];
                if (rest.Length > 0)
                {
                    if (!rest.StartsWith(':'))
                    {
                        error = $"'{rest}' follows the address: a port is written after a colon, [::1]:55000.";
                        return false;
                    }

                    portText = rest[1..];
                    hasPort = true;
                }
            }
            else if (trimmed.Count(character => character == ':') == 1)
            {
                var colon = trimmed.IndexOf(':');
                addressText = trimmed[..colon];
                portText = trimmed[(colon + 1)..];
                hasPort = true;
            }

            if (!IPAddress.TryParse(addressText, out var parsed))
            {
                error = $"'{addressText}' is not an IP address. A light is asked by its address, not by its name.";
                return false;
            }

            if (hasPort && (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            {
                error = portText.Length == 0 ? "There is a colon and no port after it." : $"'{portText}' is not a port: 1 to 65535.";
                return false;
            }

            address = parsed;
            return true;
        }

        /// <summary>
        /// Finds the address of this computer that the operating system uses to reach another address: for music mode, the address that a
        /// light has to connect to. A UDP socket that is connected only tells the operating system where its packets go, so that it can pick
        /// the network that they leave by; no packet is sent, and nothing is asked of the other computer.
        /// </summary>
        /// <param name="remote">The address of the light.</param>
        /// <returns>The address, always an IPv4 address when the light's is one; <c>null</c> when there is no way to the light.</returns>
        public static IPAddress? LocalAddressFor(IPAddress remote)
        {
            ArgumentNullException.ThrowIfNull(remote);
            try
            {
                var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
                using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(address, 9);
                var local = (socket.LocalEndPoint as IPEndPoint)?.Address;
                return local is { IsIPv4MappedToIPv6: true } ? local.MapToIPv4() : local;
            }
            catch (SocketException)
            {
                return null;
            }
        }
    }
}
