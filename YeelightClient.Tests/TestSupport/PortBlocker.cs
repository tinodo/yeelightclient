namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// Holds a UDP port on the loopback so that the program under test cannot bind it, and tells whether a UDP end point is free: the
    /// socket that was bound to an end point is closed when the end point can be bound again.
    /// </summary>
    internal sealed class PortBlocker : IDisposable
    {
        private readonly Socket _socket;

        private PortBlocker(Socket socket)
        {
            this._socket = socket;
        }

        /// <summary>Gets the end point that is held.</summary>
        public IPEndPoint EndPoint => (IPEndPoint)this._socket.LocalEndPoint!;

        /// <summary>
        /// Takes a port (0 is one that the system chooses) and holds it.
        /// </summary>
        public static PortBlocker Hold(int port = 0) => new(Bind(new IPEndPoint(IPAddress.Loopback, port)));

        /// <summary>
        /// Whether nothing is bound to an end point (a socket that was closed a moment ago may still be going away: wait for it).
        /// </summary>
        public static bool IsFree(IPEndPoint endPoint)
        {
            try
            {
                using var socket = Bind(endPoint);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            this._socket.Dispose();
        }

        private static Socket Bind(IPEndPoint endPoint)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                // Windows lets a socket that asks for it share a port with one that did not ask: only an exclusive bind keeps it out.
                // Elsewhere a socket that did not ask for sharing keeps the others out already.
                if (OperatingSystem.IsWindows())
                {
                    socket.ExclusiveAddressUse = true;
                }

                socket.Bind(endPoint);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
