namespace Yeelight
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The times and limits of a <see cref="YeelightConnection"/>.
    /// </summary>
    internal sealed class YeelightConnectionOptions
    {
        /// <summary>How long a connect may take before it is given up (the operating system waits about 21 seconds).</summary>
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>How long a command waits for its answer.</summary>
        public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>How long a command waits for a connection that is being made (after a loss, or at start).</summary>
        public TimeSpan ConnectionWait { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>The longest that a command waits for its turn under the limit of the device; after that it is refused.</summary>
        public TimeSpan RateLimitWait { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>The wait before the first new attempt to connect; it doubles after every failure.</summary>
        public TimeSpan ReconnectDelayMin { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>The longest wait between two attempts to connect.</summary>
        public TimeSpan ReconnectDelayMax { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>When nothing was heard from the device for this long, it is asked something. Zero turns this off.</summary>
        public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>The number of commands a minute that the device accepts on a connection.</summary>
        public int CommandsPerMinute { get; init; } = YeelightLimits.CommandsPerMinute;

        /// <summary>The number of commands that go through at once after a quiet moment.</summary>
        public int CommandBurst { get; init; } = 10;

        /// <summary>The longest message that is accepted.</summary>
        public int MaxLineLength { get; init; } = YeelightLineReader.DefaultMaxLineLength;

        /// <summary>The clock of the limit on the number of commands, to be able to test it.</summary>
        public TimeProvider Time { get; init; } = TimeProvider.System;
    }

    /// <summary>
    /// The control connection to one Yeelight device: a TCP connection that is kept up, with commands that wait for their answer.
    /// </summary>
    /// <remarks>
    /// A supervisor loop connects, reads and reconnects for as long as the connection lives. A close by the device, a failed write,
    /// a device that stops answering and a new address all end in the same way: the pending commands get a failure at once (they
    /// do not sit out their time limit), and the supervisor connects again after a wait that grows with every failure.
    /// </remarks>
    internal sealed class YeelightConnection : IAsyncDisposable
    {
        private static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(10);

        private readonly YeelightConnectionOptions _options;
        private readonly YeelightRateLimiter _limiter;
        private readonly ILogger _logger;
        private readonly string _name;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _gate = new();

        private string _host;
        private int _port;
        private CancellationTokenSource? _session;
        private TaskCompletionSource<LiveConnection> _ready = NewReady();
        private TaskCompletionSource _wake = NewWake();
        private Task? _supervisor;
        private int _nextId;
        private int _epoch;
        private long _lastReceived;
        private volatile bool _connected;
        private volatile IPAddress? _localAddress;
        private bool _started;
        private bool _disposed;

        /// <summary>A connection that is up: its stream, and the token that ends it (the one of its own session, not of a later one).</summary>
        private sealed record LiveConnection(NetworkStream Stream, CancellationTokenSource Session);

        /// <summary>A command that waits for its answer, and what is done with the answer on the thread that reads it.</summary>
        private sealed record PendingCommand(TaskCompletionSource<YeelightResult> Answer, Action<YeelightResult>? OnAnswer);

        public YeelightConnection(string host, int port, YeelightConnectionOptions options, ILogger logger, string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);

            this._host = host;
            this._port = port;
            this._options = options;
            this._logger = logger;
            this._name = name;
            // A token bucket lets a burst through at once and then the pace, so a minute can hold the burst on top of the pace. The quota of a
            // light is for any minute that there is: the pace is the quota less the burst, and that keeps every minute within it.
            var burst = Math.Clamp(options.CommandBurst, 1, Math.Max(1, options.CommandsPerMinute / 2));
            var pace = Math.Max(1, options.CommandsPerMinute - burst);
            this._limiter = new YeelightRateLimiter(pace, burst, options.Time);
        }

        /// <summary>Raised when the connection is made or lost, on the thread of the supervisor.</summary>
        public event Action<bool>? ConnectedChanged;

        /// <summary>Raised when the device reports that its state changed, on the thread that reads the connection.</summary>
        public event Action<IReadOnlyDictionary<string, JsonElement>>? PropsReceived;

        public bool IsConnected => this._connected;

        /// <summary>The number of connections that were made: it changes with every one, so that work for a connection is not done for the next.</summary>
        public int Epoch => Volatile.Read(ref this._epoch);

        /// <summary>The address of this computer on the network of the device, as long as there is a connection (and after it ended).</summary>
        public IPAddress? LocalAddress => this._localAddress;

        /// <summary>The address of the device that the connection is made to.</summary>
        public string Endpoint
        {
            get
            {
                lock (this._gate)
                {
                    return $"{this._host}:{this._port}";
                }
            }
        }

        /// <summary>
        /// Starts connecting, and keeps the connection up until the object is disposed.
        /// </summary>
        public void Start()
        {
            lock (this._gate)
            {
                if (this._started || this._disposed)
                {
                    return;
                }

                this._started = true;
                this._supervisor = Task.Run(() => this.RunAsync(this._lifetime.Token));
            }
        }

        /// <summary>
        /// Points the connection at another address (the device was given a new one by the router). A connection to the old address
        /// is closed and the new one is made at once.
        /// </summary>
        public void UpdateEndpoint(string host, int port)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            lock (this._gate)
            {
                if (this._disposed || (this._host == host && this._port == port))
                {
                    return;
                }

                this._host = host;
                this._port = port;
                this._wake.TrySetResult();
                this._session?.Cancel();
            }
        }

        /// <summary>
        /// Points the connection at another address, but only when it is not connected: a connection that works says where the device is,
        /// and an announcement from another address is then not to be believed. Deciding and moving are one step, so that a connection that
        /// comes up in between is not cancelled.
        /// </summary>
        /// <returns><c>false</c> when the connection is up (and was left alone); <c>true</c> when it was moved, or the address is the same.</returns>
        public bool TryUpdateEndpoint(string host, int port)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            lock (this._gate)
            {
                if (this._disposed)
                {
                    return false;
                }

                if (this._host == host && this._port == port)
                {
                    return true;
                }

                if (this._connected)
                {
                    return false;
                }

                this._host = host;
                this._port = port;
                this._wake.TrySetResult();
                this._session?.Cancel();
                return true;
            }
        }

        /// <summary>
        /// Waits until there is a connection.
        /// </summary>
        /// <returns><c>true</c> when the connection is there, <c>false</c> when it did not come in time.</returns>
        public async Task<bool> WaitConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            this.Start();
            try
            {
                await this.Ready().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a command and waits for the answer.
        /// </summary>
        /// <param name="method">The method, for example <c>set_power</c>.</param>
        /// <param name="parameters">The parameters.</param>
        /// <param name="cancellationToken">To stop waiting.</param>
        /// <param name="onAnswer">What to do with the answer on the thread that reads the connection, before anything that the light sent after it
        /// is read: for an answer that holds state (<c>get_prop</c>), which must not be taken over after a notification that is newer than it. It is
        /// only called for an answer of the light, not for a failure, and it must not wait for anything.</param>
        /// <returns>The answer of the device, or a failure that says why there is none (not connected, no answer in time, the
        /// connection was lost, too many commands). This does not throw for what goes wrong on the way to the device.</returns>
        /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
        public async Task<YeelightResult> SendAsync(string method, IReadOnlyList<object?>? parameters, CancellationToken cancellationToken = default, Action<YeelightResult>? onAnswer = null)
        {
            if (this._disposed)
            {
                return YeelightResult.Failed(YeelightFailureKind.NotConnected, "The connection was closed.");
            }

            this.Start();

            LiveConnection live;
            try
            {
                live = await this.Ready().WaitAsync(this._options.ConnectionWait, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return YeelightResult.Failed(YeelightFailureKind.NotConnected, $"The light is not connected ({this.Endpoint}).");
            }

            if (!await this._limiter.WaitAsync(this._options.RateLimitWait, cancellationToken).ConfigureAwait(false))
            {
                return YeelightResult.Failed(
                    YeelightFailureKind.RateLimited,
                    $"Too many commands for the light: it accepts about {this._options.CommandsPerMinute} a minute on a connection.");
            }

            var answer = new TaskCompletionSource<YeelightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCommand(answer, onAnswer);
            int id;
            do
            {
                id = this.NextId();
            }
            while (!this._pending.TryAdd(id, pending));

            try
            {
                var request = YeelightProtocol.BuildRequest(id, method, parameters);
                if (this._logger.IsEnabled(LogLevel.Debug))
                {
                    this._logger.LogDebug("{Light}: Sending: {Message}", this._name, Encoding.UTF8.GetString(request).TrimEnd());
                }

                await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await live.Stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    this._writeLock.Release();
                }

                return await answer.Task.WaitAsync(this._options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return YeelightResult.Failed(
                    YeelightFailureKind.Timeout,
                    $"The light did not answer '{method}' within {this._options.CommandTimeout.TotalSeconds:0.#} seconds.");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // The write failed: this connection is dead, so do not wait for the answer. The supervisor makes a new one. Only the
                // connection that was written to is ended: this command may have waited for its turn while that one was replaced.
                this.EndSession(live.Session);
                return YeelightResult.Failed(YeelightFailureKind.ConnectionLost, $"The connection to the light was lost: {ex.Message}");
            }
            finally
            {
                this._pending.TryRemove(id, out _);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task? supervisor;
            lock (this._gate)
            {
                if (this._disposed)
                {
                    return;
                }

                this._disposed = true;
                supervisor = this._supervisor;
            }

            // Pending commands fail first, as closed: the end of the reading that follows would fail them as lost.
            this.FailPending(YeelightFailureKind.NotConnected, "The connection was closed.");
            this._lifetime.Cancel();
            if (supervisor != null)
            {
                try
                {
                    await supervisor.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    // The supervisor ends by itself: nothing waits for it.
                }
            }
        }

        private static TaskCompletionSource<LiveConnection> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// The socket of the connection takes both IPv4 and IPv6, and says an IPv4 address as <c>::ffff:192.168.1.5</c>: that is not an address
        /// that a light, or a listener for a light, can use.
        /// </summary>
        private static IPAddress? Unmap(IPAddress? address)
        {
            return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
        }

        private Task<LiveConnection> Ready()
        {
            lock (this._gate)
            {
                return this._ready.Task;
            }
        }

        private int NextId()
        {
            while (true)
            {
                var current = Volatile.Read(ref this._nextId);

                // After two billion commands the numbers start again.
                var next = current == int.MaxValue ? 1 : current + 1;
                if (Interlocked.CompareExchange(ref this._nextId, next, current) == current)
                {
                    return next;
                }
            }
        }

        private void EndSession(CancellationTokenSource session)
        {
            lock (this._gate)
            {
                // A connection that was replaced is over already, and its token is disposed (this only runs under the lock that replaces it).
                if (ReferenceEquals(this._session, session))
                {
                    session.Cancel();
                }
            }
        }

        private async Task RunAsync(CancellationToken lifetime)
        {
            var delay = this._options.ReconnectDelayMin;
            while (!lifetime.IsCancellationRequested)
            {
                string host;
                int port;
                CancellationTokenSource session;
                TaskCompletionSource wake;
                lock (this._gate)
                {
                    host = this._host;
                    port = this._port;
                    this._wake = NewWake();
                    wake = this._wake;
                    this._session?.Dispose();
                    this._session = session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                }

                var connectedAt = 0L;
                TcpClient? client = null;
                try
                {
                    client = new TcpClient { NoDelay = true };
                    EnableKeepAlive(client.Client);
                    using (var connect = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                    {
                        connect.CancelAfter(this._options.ConnectTimeout);
                        await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
                    }

                    var stream = client.GetStream();
                    this._localAddress = Unmap((client.Client.LocalEndPoint as IPEndPoint)?.Address);
                    connectedAt = Environment.TickCount64;
                    this.SetConnected(new LiveConnection(stream, session));
                    await this.ReadLoopAsync(stream, session).ConfigureAwait(false);
                    this._logger.LogDebug("{Light}: The light closed the connection.", this._name);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // The connect took too long, or the address changed, or the light stopped answering.
                }
                catch (Exception ex)
                {
                    this._logger.LogDebug("{Light}: Connection problem: {Reason}", this._name, ex.Message);
                }
                finally
                {
                    this.SetDisconnected();
                    if (lifetime.IsCancellationRequested)
                    {
                        this.FailPending(YeelightFailureKind.NotConnected, "The connection was closed.");
                    }
                    else
                    {
                        this.FailPending(YeelightFailureKind.ConnectionLost, "The connection to the light was lost.");
                    }

                    client?.Dispose();
                }

                // A connection that stayed up for a while starts again from the shortest wait, one that failed straight away waits longer.
                if (connectedAt != 0 && TimeSpan.FromMilliseconds(Environment.TickCount64 - connectedAt) >= StableConnection)
                {
                    delay = this._options.ReconnectDelayMin;
                }

                if (!wake.Task.IsCompleted)
                {
                    this._logger.LogDebug("{Light}: Connecting again in {Seconds:0.#} seconds.", this._name, delay.TotalSeconds);
                    using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime))
                    {
                        // Ends at the end of the wait, at a new address (the wake-up) or at disposal. Nothing here throws: the wait
                        // that is cancelled is only observed through the task that won.
                        await Task.WhenAny(Task.Delay(delay, waiting.Token), wake.Task).ConfigureAwait(false);
                        waiting.Cancel();
                    }

                    delay = TimeSpan.FromTicks(Math.Min(this._options.ReconnectDelayMax.Ticks, delay.Ticks * 2));
                }
                else
                {
                    delay = this._options.ReconnectDelayMin;
                }
            }
        }

        private async Task ReadLoopAsync(NetworkStream stream, CancellationTokenSource session)
        {
            Volatile.Write(ref this._lastReceived, Environment.TickCount64);
            var keepAlive = this._options.KeepAliveInterval > TimeSpan.Zero
                ? this.KeepAliveAsync(session)
                : Task.CompletedTask;
            try
            {
                var reader = new YeelightLineReader(stream, this._options.MaxLineLength);
                while (await reader.ReadLineAsync(session.Token).ConfigureAwait(false) is { } line)
                {
                    Volatile.Write(ref this._lastReceived, Environment.TickCount64);
                    if (line.Length > 0)
                    {
                        this.Handle(line);
                    }
                }
            }
            finally
            {
                session.Cancel();
                try
                {
                    await keepAlive.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this._logger.LogDebug("{Light}: The keep-alive failed: {Reason}", this._name, ex.Message);
                }
            }
        }

        private void Handle(string line)
        {
            if (this._logger.IsEnabled(LogLevel.Debug))
            {
                this._logger.LogDebug("{Light}: Received: {Message}", this._name, line);
            }

            if (!YeelightProtocol.TryParse(line, out var message))
            {
                this._logger.LogDebug("{Light}: Ignored a message that is not understood: {Message}", this._name, line.Length > 80 ? TextLimits.Truncate(line, 80) + "..." : line);
                return;
            }

            switch (message)
            {
                case YeelightResponse response:
                    if (this._pending.TryRemove(response.Result.Id, out var pending))
                    {
                        if (pending.OnAnswer is { } onAnswer)
                        {
                            try
                            {
                                onAnswer(response.Result);
                            }
                            catch (Exception ex)
                            {
                                this._logger.LogWarning(ex, "{Light}: The answer of the light could not be taken over.", this._name);
                            }
                        }

                        pending.Answer.TrySetResult(response.Result);
                    }

                    break;
                case YeelightNotification { Method: "props" } notification:
                    try
                    {
                        this.PropsReceived?.Invoke(notification.Parameters);
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogWarning(ex, "{Light}: The state that the light reported could not be taken over.", this._name);
                    }

                    break;
            }
        }

        private async Task KeepAliveAsync(CancellationTokenSource session)
        {
            var interval = this._options.KeepAliveInterval;
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(session.Token).ConfigureAwait(false))
                {
                    var quiet = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref this._lastReceived));
                    if (quiet < interval)
                    {
                        continue;
                    }

                    // The connection may be half open (the device lost power): only a question that is not answered shows that.
                    var result = await this.SendAsync("get_prop", [YeelightProperties.Power], session.Token).ConfigureAwait(false);
                    if (result.Error is { IsTransportFailure: true })
                    {
                        this._logger.LogInformation("{Light}: The light does not answer any more; connecting again.", this._name);
                        session.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The connection ended.
            }
        }

        private void SetConnected(LiveConnection live)
        {
            // The flag and the connection that is ready change together under the lock that a move of the endpoint takes: a move that sees
            // "not connected" is one that cancels a session that is still being made, never one that has just come up.
            lock (this._gate)
            {
                this._ready.TrySetResult(live);
                this._epoch++;
                this._connected = true;
            }

            this._logger.LogInformation("{Light}: Connected to {Endpoint}.", this._name, this.Endpoint);
            this.RaiseConnectedChanged(true);
        }

        private void SetDisconnected()
        {
            bool wasConnected;
            lock (this._gate)
            {
                wasConnected = this._connected;
                this._connected = false;
                if (this._ready.Task.IsCompleted)
                {
                    this._ready = NewReady();
                }
            }

            if (wasConnected)
            {
                this._logger.LogInformation("{Light}: The connection to {Endpoint} ended.", this._name, this.Endpoint);
                this.RaiseConnectedChanged(false);
            }
        }

        private void RaiseConnectedChanged(bool connected)
        {
            try
            {
                this.ConnectedChanged?.Invoke(connected);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "{Light}: The change of the connection could not be taken over.", this._name);
            }
        }

        private void FailPending(YeelightFailureKind kind, string message)
        {
            foreach (var pair in this._pending)
            {
                pair.Value.Answer.TrySetResult(YeelightResult.Failed(kind, message));
            }
        }

        private static void EnableKeepAlive(Socket socket)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException or NotSupportedException)
            {
                // A platform without these options still has the question of the keep-alive of the connection itself.
            }
        }
    }
}
