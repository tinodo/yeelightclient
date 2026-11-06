namespace Yeelight.Tests.TestSupport
{
    using System.Net;
    using System.Net.Sockets;
    using Xunit;

    /// <summary>
    /// A second address on the loopback (127.0.0.2), for a test that needs a sender that is not the light: the lights of the tests are
    /// on 127.0.0.1, and a neighbour on the network sends from another address. Windows and Linux have the whole of 127.0.0.0/8 on the
    /// loopback; macOS has only 127.0.0.1 unless somebody adds an alias, and there such a test is skipped. The packets of the tests never leave
    /// the computer: the sender uses this address, the receiver is on 127.0.0.1.
    /// </summary>
    internal static class LoopbackAlias
    {
        public static readonly IPAddress Address = IPAddress.Parse("127.0.0.2");

        public static bool IsAvailable { get; } = Probe();

        private static bool Probe()
        {
            try
            {
                using var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                tcp.Bind(new IPEndPoint(Address, 0));
                using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.Bind(new IPEndPoint(Address, 0));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A fact that needs <see cref="LoopbackAlias"/>, and is skipped where there is none.
    /// </summary>
    internal sealed class FactIfLoopbackAliasAttribute : FactAttribute
    {
        public FactIfLoopbackAliasAttribute()
        {
            if (!LoopbackAlias.IsAvailable)
            {
                this.Skip = "This computer has no address 127.0.0.2 on its loopback (macOS has only 127.0.0.1), and the test needs a second sender.";
            }
        }
    }
}
