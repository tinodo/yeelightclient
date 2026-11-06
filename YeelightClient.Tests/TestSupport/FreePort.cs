namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// A port where nothing listens: it was free a moment ago. The system does not hand the same port to somebody else at once, so it is
    /// good for a test that needs a light that is not there.
    /// </summary>
    internal static class FreePort
    {
        public static int Tcp()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public static int Udp()
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        }
    }

    /// <summary>
    /// Makes the connections of the tests that look at <see cref="YeelightConnection"/> by itself: with the times of a test, which are short.
    /// </summary>
    internal static class Connections
    {
        /// <summary>
        /// The settings of a connection that is made to go fast: it reconnects after a few milliseconds, and is not limited in the number of
        /// commands (unless a test says so).
        /// </summary>
        public static YeelightConnectionOptions Fast(
            TimeSpan? commandTimeout = null,
            TimeSpan? keepAlive = null,
            int perMinute = 600_000,
            int burst = 100_000,
            TimeSpan? connectTimeout = null,
            TimeSpan? reconnectMin = null,
            TimeSpan? reconnectMax = null,
            TimeSpan? connectionWait = null,
            TimeSpan? rateLimitWait = null,
            int? maxLineLength = null)
        {
            return new YeelightConnectionOptions
            {
                ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2),
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(3),
                ConnectionWait = connectionWait ?? TimeSpan.FromSeconds(5),
                RateLimitWait = rateLimitWait ?? TimeSpan.FromSeconds(2),
                ReconnectDelayMin = reconnectMin ?? TimeSpan.FromMilliseconds(20),
                ReconnectDelayMax = reconnectMax ?? TimeSpan.FromMilliseconds(100),
                KeepAliveInterval = keepAlive ?? TimeSpan.Zero,
                CommandsPerMinute = perMinute,
                CommandBurst = burst,
                MaxLineLength = maxLineLength ?? YeelightLineReader.DefaultMaxLineLength,
            };
        }

        /// <summary>A connection to a light on the loopback. It is not started: the first command, or <c>Start</c>, does that.</summary>
        public static YeelightConnection To(int port, YeelightConnectionOptions? options = null, ILogger? logger = null)
        {
            return new YeelightConnection("127.0.0.1", port, options ?? Fast(), logger ?? NullLogger.Instance, "test");
        }

        /// <summary>A connection that is started, and waits until it is made.</summary>
        public static async Task<YeelightConnection> ConnectedAsync(int port, YeelightConnectionOptions? options = null, ILogger? logger = null)
        {
            var connection = To(port, options, logger);
            connection.Start();
            await Wait.UntilAsync(() => connection.IsConnected, "the connection to be made");
            return connection;
        }
    }
}
