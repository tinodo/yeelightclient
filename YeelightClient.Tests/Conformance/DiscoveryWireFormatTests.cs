namespace Yeelight.Tests.Conformance
{
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The search that finds the lights (section 3.1 of https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf).
    /// </summary>
    public class DiscoveryWireFormatTests
    {
        // "The start line must be M-SEARCH * HTTP/1.1", "HOST ... should be 239.255.255.250:1982", the value of MAN is "ssdp:discover" with its
        // double quotes, ST is wifi_bulb, and "each line should be terminated by \r\n".
        private const string SearchOfTheSpecification = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n";

        [Fact]
        public async Task TheSearch_IsTheMessageOfTheSpecification_EveryLineEndedWithCrLf()
        {
            // The probe that finds the light is a search that is sent to it alone.
            await using var h = await Wire.StartAsync();

            var searches = h.Bulb.Searches;

            Assert.NotEmpty(searches);
            Assert.All(searches, search => Assert.Equal(SearchOfTheSpecification, search));
        }
    }
}
