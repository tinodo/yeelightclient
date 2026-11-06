namespace Yeelight.Tests.TestSupport
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A UDP socket on the loopback that answers the searches that it gets with what a test says, to be the light that answers wrongly, or in
    /// pieces, or not at all (the <see cref="SimulatedBulb"/> only answers as a light does). It keeps every datagram that it received, and
    /// with whom.
    /// </summary>
    internal sealed class ScriptedResponder : IAsyncDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<ReceivedDatagram> _received = new();
        private readonly long _origin = Stopwatch.GetTimestamp();
        private readonly Task _answering;

        public ScriptedResponder()
        {
            this.EndPoint = (IPEndPoint)this._udp.Client.LocalEndPoint!;
            this._answering = Task.Run(this.AnswerAsync);
        }

        /// <summary>A datagram that was received: its text, where it came from, and when (the time since this responder was made).</summary>
        public sealed record ReceivedDatagram(string Text, IPEndPoint From, TimeSpan At);

        /// <summary>Gets where a search is sent to reach this responder.</summary>
        public IPEndPoint EndPoint { get; }

        public int Port => this.EndPoint.Port;

        /// <summary>Gets every datagram that was received, in order, with the end point that it came from.</summary>
        public IReadOnlyList<(string Text, IPEndPoint From)> Received => [.. this._received.Select(datagram => (datagram.Text, datagram.From))];

        /// <summary>Gets every datagram that was received, in order, with when it was received.</summary>
        public IReadOnlyList<ReceivedDatagram> Datagrams => [.. this._received];

        /// <summary>The number of searches that were received (the datagrams that start with <c>M-SEARCH</c>).</summary>
        public int SearchCount
        {
            get
            {
                var count = 0;
                foreach (var datagram in this._received)
                {
                    if (datagram.Text.StartsWith("M-SEARCH", StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Chooses what is sent back for a datagram that was received (given its text and where it came from): the text of a datagram, or
        /// <c>null</c> for no answer.
        /// </summary>
        public Func<string, IPEndPoint, string?>? Reply { get; set; }

        /// <summary>Sends a datagram, from the end point of this responder.</summary>
        public async Task SendAsync(IPEndPoint target, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await this._udp.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
        }

        /// <summary>Sends bytes, from the end point of this responder.</summary>
        public async Task SendAsync(IPEndPoint target, byte[] bytes)
        {
            await this._udp.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            this._stop.Cancel();
            this._udp.Dispose();
            try
            {
                await this._answering.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The loop ends by itself.
            }
        }

        private async Task AnswerAsync()
        {
            while (!this._stop.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await this._udp.ReceiveAsync(this._stop.Token).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue;
                }
                catch (Exception)
                {
                    return;
                }

                var text = Encoding.UTF8.GetString(received.Buffer);
                this._received.Enqueue(new ReceivedDatagram(text, received.RemoteEndPoint, Stopwatch.GetElapsedTime(this._origin)));
                var answer = this.Reply?.Invoke(text, received.RemoteEndPoint);
                if (answer is null)
                {
                    continue;
                }

                try
                {
                    await this.SendAsync(received.RemoteEndPoint, answer).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    // The other side went away.
                }
            }
        }
    }
}
