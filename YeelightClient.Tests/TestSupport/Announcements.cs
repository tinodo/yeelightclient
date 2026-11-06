namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using Xunit;

    /// <summary>
    /// The messages that a light sends over UDP (the answer to a search, and the announcement that it makes by itself), as text and as
    /// what the client reads of them.
    /// </summary>
    internal static class Announcements
    {
        /// <summary>The methods that a light lists, as a light that is not a ceiling light does.</summary>
        public const string Support = "get_prop set_default set_power toggle set_bright start_cf stop_cf set_scene cron_add cron_get cron_del set_ct_abx set_rgb set_hsv set_adjust set_music set_name adjust_bright adjust_ct adjust_color";

        /// <summary>
        /// Makes the text of a message, field by field, for the cases that a <see cref="SimulatedBulb"/> does not make (another id,
        /// a value that is not right, a field that is missing).
        /// </summary>
        /// <param name="id">The id of the light.</param>
        /// <param name="location">The location, <c>yeelight://127.0.0.1:55443</c>.</param>
        /// <param name="advertisement"><c>true</c> for the announcement that a light makes by itself, <c>false</c> for the answer to a search.</param>
        /// <param name="headers">The other fields, in this order.</param>
        public static string Message(string id, string location, bool advertisement = true, params (string Name, string Value)[] headers)
        {
            var text = new StringBuilder(advertisement
                ? "NOTIFY * HTTP/1.1\r\nHost: 239.255.255.250:1982\r\nCache-Control: max-age=3600\r\n"
                : "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: \r\nExt: \r\n");
            text.Append("Location: ").Append(location).Append("\r\n");
            if (advertisement)
            {
                text.Append("NTS: ssdp:alive\r\n");
            }

            text.Append("Server: POSIX UPnP/1.0 YGLC/1\r\n");
            text.Append("id: ").Append(id).Append("\r\n");
            foreach (var (name, value) in headers)
            {
                text.Append(name).Append(": ").Append(value).Append("\r\n");
            }

            return text.ToString();
        }

        /// <summary>
        /// Reads a message that has to be an announcement of a light.
        /// </summary>
        public static DeviceAnnouncement Parse(string message)
        {
            Assert.True(DeviceAnnouncement.TryParse(message, out var announcement), "The message should be read as the announcement of a light: " + message);
            return announcement!;
        }

        /// <summary>
        /// Makes the announcement of a light, with a model, the methods of an ordinary light and the state that the headers say. A header
        /// of the same name as one of those replaces it.
        /// </summary>
        public static DeviceAnnouncement Of(string id, string location, params (string Name, string Value)[] headers)
        {
            var fields = new List<(string Name, string Value)> { ("model", "color"), ("fw_ver", "18"), ("support", Support) };
            foreach (var header in headers)
            {
                var index = fields.FindIndex(field => string.Equals(field.Name, header.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    fields[index] = header;
                }
                else
                {
                    fields.Add(header);
                }
            }

            return Parse(Message(id, location, true, [.. fields]));
        }

        /// <summary>
        /// Reads what a simulated light says about itself now.
        /// </summary>
        /// <param name="bulb">The light.</param>
        /// <param name="location">Where the light says its control service is, when it is not where it really is (to point the client at a proxy).</param>
        /// <param name="advertisement">Whether this is the announcement of the light, or the answer to a search.</param>
        public static DeviceAnnouncement From(SimulatedBulb bulb, string? location = null, bool advertisement = true)
        {
            return Parse(bulb.Message(advertisement, location));
        }

        /// <summary>The location of a control service on the loopback.</summary>
        public static string LocationOf(int port) => $"yeelight://127.0.0.1:{port}";

        /// <summary>
        /// Sends a datagram from a socket of its own, on the given address (0 is a port of the system's choosing), to an end point.
        /// </summary>
        public static async Task SendFromAsync(IPAddress source, IPEndPoint target, byte[] datagram)
        {
            using var client = new UdpClient(new IPEndPoint(source, 0));
            await client.SendAsync(datagram, datagram.Length, target).ConfigureAwait(false);
        }
    }
}
