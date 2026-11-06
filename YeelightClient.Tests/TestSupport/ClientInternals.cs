namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Net;
    using System.Reflection;

    /// <summary>
    /// Looks inside a <see cref="YeelightClient"/>, for what a test has to see and the client does not show: the discovery that it
    /// started (where it listens, how often its sockets were made). The names of the fields are those of the client; a change of them
    /// fails here, with a message that says so.
    /// </summary>
    internal static class ClientInternals
    {
        /// <summary>Gets the discovery that listens to announcements (<see cref="YeelightClient.StartListening"/>), or <c>null</c>.</summary>
        public static YeelightDiscovery? Listener(YeelightClient client) => Read(client, "_listener");

        /// <summary>Gets the discovery that searches (<see cref="YeelightClient.StartDiscovery"/>), or <c>null</c>.</summary>
        public static YeelightDiscovery? Searcher(YeelightClient client) => Read(client, "_searcher");

        /// <summary>Gets where the client listens for announcements, or <c>null</c> when it does not (yet).</summary>
        public static IPEndPoint? ListenEndPoint(YeelightClient client) => Listener(client)?.ListenEndPoint;

        private static YeelightDiscovery? Read(YeelightClient client, string field)
        {
            var info = typeof(YeelightClient).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"YeelightClient has no field '{field}' any more: update ClientInternals.");
            return (YeelightDiscovery?)info.GetValue(client);
        }
    }
}
