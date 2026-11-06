namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A command that a scripted server received.
    /// </summary>
    /// <param name="Connection">The number of the connection that it came in on, from 1.</param>
    /// <param name="Id">The number of the command.</param>
    /// <param name="Method">The method.</param>
    /// <param name="Raw">The line, as it came in.</param>
    internal sealed record ScriptedRequest(int Connection, int Id, string Method, string Raw);

    /// <summary>
    /// A server on the loopback that speaks the control protocol of a Yeelight light at the level of bytes, so that a test can make it say what
    /// a light never would: answers of the wrong shape, text that is not UTF-8, a message without its line break, a line of a megabyte, or
    /// nothing at all. (The <see cref="SimulatedBulb"/> keeps to the protocol, and can only send lines of text.) It answers every command with
    /// <c>ok</c>, unless <see cref="Reply"/> says otherwise.
    /// </summary>
    internal sealed class ScriptedServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Peer> _peers = [];
        private readonly ConcurrentQueue<ScriptedRequest> _requests = new();
        private readonly ConcurrentQueue<long> _acceptedAt = new();
        private readonly object _gate = new();
        private readonly Task _accepting;
        private int _accepted;

        public ScriptedServer()
        {
            this._listener.Start();
            this.Port = ((IPEndPoint)this._listener.LocalEndpoint).Port;
            this._accepting = Task.Run(this.AcceptAsync);
        }

        /// <summary>Gets the TCP port, on the loopback address.</summary>
        public int Port { get; }

        /// <summary>Gets the number of connections that were made to the server.</summary>
        public int Accepted => Volatile.Read(ref this._accepted);

        /// <summary>Gets the number of connections that are open now.</summary>
        public int Open
        {
            get
            {
                lock (this._gate)
                {
                    return this._peers.Count;
                }
            }
        }

        /// <summary>Gets every command that the server received, in order.</summary>
        public IReadOnlyList<ScriptedRequest> Requests => [.. this._requests];

        /// <summary>Gets when each connection was accepted, as <see cref="Stopwatch.GetTimestamp"/> says.</summary>
        public IReadOnlyList<long> AcceptedAt => [.. this._acceptedAt];

        /// <summary>
        /// Chooses what the server sends back for a command: pieces of bytes, which are written one after the other. <c>null</c> is the
        /// right answer (<c>ok</c>), and an empty list is no answer at all. It runs on the thread that reads the connection.
        /// </summary>
        public Func<ScriptedRequest, IReadOnlyList<byte[]>?>? Reply { get; set; }

        /// <summary>When set, a connection is closed as soon as it is accepted.</summary>
        public bool CloseOnAccept { get; set; }

        /// <summary>The right answer to a command: one line, <c>ok</c>.</summary>
        public static byte[] Ok(int id) => Encoding.UTF8.GetBytes($"{{\"id\":{id},\"result\":[\"ok\"]}}\r\n");

        /// <summary>The bytes of a line of text, with its line break.</summary>
        public static byte[] Line(string text) => Encoding.UTF8.GetBytes(text + "\r\n");

        /// <summary>Sends bytes to every connection that is open.</summary>
        public async Task SendAsync(byte[] data)
        {
            Peer[] peers;
            lock (this._gate)
            {
                peers = [.. this._peers];
            }

            foreach (var peer in peers)
            {
                await peer.WriteAsync(data).ConfigureAwait(false);
            }
        }

        /// <summary>Sends a line of text, with its line break, to every connection that is open.</summary>
        public Task SendLineAsync(string text) => this.SendAsync(Line(text));

        /// <summary>Closes every connection that is open, and keeps listening.</summary>
        /// <param name="reset">Whether the connection is reset, as when a light loses power, instead of closed in an orderly way.</param>
        public void Drop(bool reset = false)
        {
            Peer[] peers;
            lock (this._gate)
            {
                peers = [.. this._peers];
            }

            foreach (var peer in peers)
            {
                peer.Close(reset);
            }
        }

        public Task WaitForOpenAsync(int count, string what, TimeSpan? timeout = null)
        {
            return Wait.UntilAsync(() => this.Open >= count, what, timeout);
        }

        public async ValueTask DisposeAsync()
        {
            this._stop.Cancel();
            this._listener.Stop();
            this.Drop();
            try
            {
                await this._accepting.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The loop ends by itself.
            }
        }

        private async Task AcceptAsync()
        {
            while (!this._stop.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await this._listener.AcceptTcpClientAsync(this._stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var number = Interlocked.Increment(ref this._accepted);
                this._acceptedAt.Enqueue(Stopwatch.GetTimestamp());
                if (this.CloseOnAccept)
                {
                    tcp.Dispose();
                    continue;
                }

                tcp.NoDelay = true;
                var peer = new Peer(tcp, number);
                lock (this._gate)
                {
                    this._peers.Add(peer);
                }

                _ = Task.Run(() => this.ServeAsync(peer));
            }
        }

        private async Task ServeAsync(Peer peer)
        {
            try
            {
                var stream = peer.Tcp.GetStream();
                var buffer = new byte[4096];
                var line = new List<byte>();
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, this._stop.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    for (var index = 0; index < read; index++)
                    {
                        if (buffer[index] != (byte)'\n')
                        {
                            line.Add(buffer[index]);
                            continue;
                        }

                        var text = Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
                        line.Clear();
                        if (text.Length > 0)
                        {
                            await this.HandleAsync(peer, text).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The connection ended.
            }
            finally
            {
                lock (this._gate)
                {
                    this._peers.Remove(peer);
                }

                peer.Close(reset: false);
            }
        }

        private async Task HandleAsync(Peer peer, string text)
        {
            int id;
            string method;
            try
            {
                using var document = JsonDocument.Parse(text);
                id = document.RootElement.GetProperty("id").GetInt32();
                method = document.RootElement.GetProperty("method").GetString() ?? string.Empty;
            }
            catch (Exception)
            {
                // The client of the tests only sends commands.
                return;
            }

            var request = new ScriptedRequest(peer.Number, id, method, text);
            this._requests.Enqueue(request);
            IReadOnlyList<byte[]> pieces = this.Reply?.Invoke(request) ?? [Ok(id)];
            foreach (var piece in pieces)
            {
                await peer.WriteAsync(piece).ConfigureAwait(false);
            }
        }

        private sealed class Peer(TcpClient tcp, int number)
        {
            private readonly SemaphoreSlim _writeLock = new(1, 1);

            public TcpClient Tcp { get; } = tcp;

            public int Number { get; } = number;

            public async Task WriteAsync(byte[] data)
            {
                try
                {
                    await this._writeLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await this.Tcp.GetStream().WriteAsync(data).ConfigureAwait(false);
                    }
                    finally
                    {
                        this._writeLock.Release();
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
                {
                    // The connection was closed.
                }
            }

            public void Close(bool reset)
            {
                try
                {
                    if (reset)
                    {
                        this.Tcp.Client.LingerState = new LingerOption(true, 0);
                    }
                }
                catch (Exception)
                {
                    // Already closed.
                }

                this.Tcp.Dispose();
            }
        }
    }
}
