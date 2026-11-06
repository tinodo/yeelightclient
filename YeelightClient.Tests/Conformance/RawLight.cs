namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>
    /// A light that is nothing but a TCP socket on the loopback: it keeps the bytes that it receives exactly as they came (the simulated
    /// light reads lines, so it cannot tell how a line ended), and answers every command with <c>ok</c>.
    /// </summary>
    internal sealed class RawLight : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly object _gate = new();
        private readonly List<byte> _received = [];
        private readonly Task _serving;

        public RawLight()
        {
            this._listener.Start();
            this._serving = Task.Run(this.ServeAsync);
        }

        /// <summary>Gets the TCP port that the light listens on.</summary>
        public int Port => ((IPEndPoint)this._listener.LocalEndpoint).Port;

        /// <summary>Gets every byte received so far.</summary>
        public byte[] Received
        {
            get
            {
                lock (this._gate)
                {
                    return [.. this._received];
                }
            }
        }

        public void Dispose()
        {
            this._listener.Stop();
            this._serving.Wait(TimeSpan.FromSeconds(5));
        }

        private async Task ServeAsync()
        {
            try
            {
                using var tcp = await this._listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var stream = tcp.GetStream();
                var buffer = new byte[4096];
                var line = new List<byte>();
                while (true)
                {
                    var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    for (var index = 0; index < read; index++)
                    {
                        lock (this._gate)
                        {
                            this._received.Add(buffer[index]);
                        }

                        line.Add(buffer[index]);
                        if (buffer[index] != (byte)'\n')
                        {
                            continue;
                        }

                        var text = Encoding.UTF8.GetString([.. line]);
                        line.Clear();
                        var id = Regex.Match(text, "\"id\":(\\d+)").Groups[1].Value;
                        await stream.WriteAsync(Encoding.UTF8.GetBytes($"{{\"id\":{id},\"result\":[\"ok\"]}}\r\n")).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The client closed the connection, or the light was stopped.
            }
        }
    }
}
