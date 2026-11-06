namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// How a command is framed on the TCP connection: "{ id_pair, method_pair, params_pair}\r\n" (section 4.1 of
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf), one JSON object per line, every line ended with CR LF. A plain socket
    /// receives the commands here, so that the bytes can be looked at as they came.
    /// </summary>
    public class RequestFramingTests
    {
        private static readonly Regex Line = new("^\\{\"id\":(?<id>[0-9]+),\"method\":\"(?<method>[a-z_]+)\",\"params\":(?<params>\\[.*\\])\\}$", RegexOptions.None, TimeSpan.FromSeconds(5));

        [Fact]
        public async Task EveryCommand_IsOneJsonObjectOnALineOfItsOwn_EndedWithCrLf_InUtf8_WithoutAByteOrderMark()
        {
            using var light = new RawLight();
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, RefreshOnConnect = false, KeepAliveInterval = TimeSpan.Zero });
            var device = client.Apply(UnitMessages.Parse(UnitMessages.Response(location: $"yeelight://127.0.0.1:{light.Port}", support: SimulatedBulbOptions.AllMethods)));
            Assert.NotNull(device);

            await device.SetPowerAsync(true, TimeSpan.FromMilliseconds(500));
            await device.ToggleAsync();
            await device.SetNameAsync("Wohnzimmer Ü & Co");
            await device.SetBrightnessAsync(40);

            var bytes = light.Received;
            Assert.Equal((byte)'{', bytes[0]);
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            var lines = text.Split("\r\n");
            Assert.Equal(5, lines.Length);
            Assert.Equal(string.Empty, lines[4]);
            Assert.DoesNotContain('\r', string.Concat(lines));
            Assert.DoesNotContain('\n', string.Concat(lines));
            var matches = lines.Take(4).Select(line => Line.Match(line)).ToArray();
            Assert.All(matches, match => Assert.True(match.Success, "A line is not the object of a command: " + match.Value));
            Assert.Equal(["set_power", "toggle", "set_name", "set_bright"], matches.Select(match => match.Groups["method"].Value).ToArray());
            Assert.Equal(["[\"on\",\"smooth\",500]", "[]", "[\"Wohnzimmer Ü & Co\"]", "[40,\"sudden\",0]"], matches.Select(match => match.Groups["params"].Value).ToArray());
            var ids = matches.Select(match => int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(ids.Distinct().ToArray(), ids);
            Assert.Equal(ids.Order().ToArray(), ids);
        }

        [Fact]
        public async Task ACommandWithANonAsciiName_IsSentAsUtf8Bytes_NotAsAnEscape()
        {
            using var light = new RawLight();
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, RefreshOnConnect = false, KeepAliveInterval = TimeSpan.Zero });
            var device = client.Apply(UnitMessages.Parse(UnitMessages.Response(location: $"yeelight://127.0.0.1:{light.Port}", support: SimulatedBulbOptions.AllMethods)));
            Assert.NotNull(device);

            await device.SetNameAsync("Küche");

            var bytes = light.Received;
            Assert.True(bytes.AsSpan().IndexOf("Küche"u8) >= 0, "The name should be in the line as UTF-8: " + Encoding.UTF8.GetString(bytes));
        }
    }
}
