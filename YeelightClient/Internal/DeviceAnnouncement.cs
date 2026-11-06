namespace Yeelight
{
    using System;
    using System.Collections.Frozen;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// What a device says about itself: in the answer to a search (<c>HTTP/1.1 200 OK</c>) or in the announcement that it sends by
    /// itself (<c>NOTIFY * HTTP/1.1</c>). Anyone on the network can send such a message, so it is read defensively and the caller
    /// has to check <see cref="IsFrom"/> before it trusts the address in it.
    /// </summary>
    internal sealed record DeviceAnnouncement
    {
        private const int MaxMessageLength = 4096;
        private const int MaxIdLength = 64;
        private const int MaxTextLength = 256;

        private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>().ToFrozenDictionary();

        /// <summary>The address of the control service, <c>yeelight://192.168.1.239:55443</c>.</summary>
        public required string Location { get; init; }

        /// <summary>The address in <see cref="Location"/>.</summary>
        public required string Host { get; init; }

        /// <summary>The port in <see cref="Location"/>.</summary>
        public required int Port { get; init; }

        /// <summary>The id of the device: the only thing that names it, whatever its address or name become.</summary>
        public required string Id { get; init; }

        public string Model { get; init; } = string.Empty;

        public string FirmwareVersion { get; init; } = string.Empty;

        /// <summary>The methods that the device supports, as it lists them.</summary>
        public IReadOnlyList<string> Support { get; init; } = [];

        /// <summary>
        /// The properties of the device that the message holds (<c>power</c>, <c>bright</c>, <c>color_mode</c>, <c>ct</c>, <c>rgb</c>,
        /// <c>hue</c>, <c>sat</c>, <c>name</c>), as text: what the message leaves out is not in the list, and keeps its value.
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; init; } = NoProperties;

        /// <summary>Whether the device sent this by itself, as opposed to answering a search.</summary>
        public bool IsAdvertisement { get; init; }

        /// <summary>
        /// Reads a message. Nothing that arrives can make this throw: what is not an announcement of a device is not accepted.
        /// </summary>
        public static bool TryParse(string? message, out DeviceAnnouncement? announcement)
        {
            announcement = null;
            if (string.IsNullOrEmpty(message) || message.Length > MaxMessageLength)
            {
                return false;
            }

            var lines = message.Split('\n');
            var first = lines[0].TrimEnd('\r');
            bool advertisement;
            if (first.StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
            {
                advertisement = false;
            }
            else if (first.StartsWith("NOTIFY * HTTP/1.1", StringComparison.Ordinal))
            {
                advertisement = true;
            }
            else
            {
                return false;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }

            if (headers.TryGetValue("NTS", out var subtype) && !string.Equals(subtype, "ssdp:alive", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!headers.TryGetValue("Location", out var location) || !YeelightLocation.TryParse(location, out var host, out var port))
            {
                return false;
            }

            if (!headers.TryGetValue("id", out var id) || id.Length == 0 || id.Length > MaxIdLength)
            {
                return false;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in YeelightProperties.All)
            {
                if (headers.TryGetValue(name, out var value))
                {
                    properties[name] = TextLimits.Truncate(value, MaxTextLength);
                }
            }

            announcement = new DeviceAnnouncement
            {
                Location = location,
                Host = host,
                Port = port,
                Id = id,
                Model = headers.GetValueOrDefault("model", string.Empty),
                FirmwareVersion = headers.GetValueOrDefault("fw_ver", string.Empty),
                Support = headers.TryGetValue("support", out var support)
                    ? support.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [],
                Properties = properties.ToFrozenDictionary(StringComparer.Ordinal),
                IsAdvertisement = advertisement,
            };
            return true;
        }

        /// <summary>
        /// Whether the address in the message is the address that the message came from. A device announces its own address; a
        /// message that points somewhere else (at another computer, or at a host outside the network) is not from a device and
        /// would make this program connect to whatever the sender names.
        /// </summary>
        public bool IsFrom(IPAddress source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.IsIPv4MappedToIPv6)
            {
                source = source.MapToIPv4();
            }

            return IPAddress.TryParse(this.Host, out var host) &&
                host.AddressFamily == AddressFamily.InterNetwork &&
                host.Equals(source) &&
                IsLocalNetworkAddress(host);
        }

        /// <summary>
        /// Whether the address is one that a device on a local network has: private, link-local, loopback, or the shared range that
        /// some VPNs use.
        /// </summary>
        public static bool IsLocalNetworkAddress(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                bytes[0] == 127 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 169 && bytes[1] == 254) ||
                (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        }
    }
}
