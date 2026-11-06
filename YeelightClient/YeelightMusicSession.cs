namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A device in music mode: a connection over which the device takes commands without a limit on their number, and without answering them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Get one from <see cref="YeelightDevice.StartMusicModeAsync"/>. In music mode the device connects to this computer, and a command that is
    /// sent over that connection is carried out at once, as fast as they come: a light can follow music, or the colours of a screen. The price is
    /// that the commands are not answered, so a command that the light refuses goes unnoticed, and the device reports no changes to its properties while it
    /// is in music mode.
    /// </para>
    /// <para>
    /// A command here only fails when the connection does: a <see cref="YeelightException"/> of the kind <see cref="YeelightFailureKind.ConnectionLost"/>.
    /// The session ends when it is disposed (the device leaves music mode when the connection closes), when the device closes the connection, or
    /// when the client is disposed. <see cref="Completion"/> tells when.
    /// </para>
    /// </remarks>
    public sealed class YeelightMusicSession : IAsyncDisposable
    {
        private static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(5);

        private readonly YeelightDevice _device;
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new();
        private int _nextId;
        private int _disposed;

        private YeelightMusicSession(YeelightDevice device, TcpClient client)
        {
            this._device = device;
            this._client = client;
            this._stream = client.GetStream();
            _ = Task.Run(this.WatchAsync);
        }

        /// <summary>
        /// Gets a task that ends when the session has ended: it was disposed, or the device closed the connection, or the connection failed.
        /// </summary>
        public Task Completion => this._completion.Task;

        /// <summary>
        /// Gets whether the session is open.
        /// </summary>
        public bool IsConnected => Volatile.Read(ref this._disposed) == 0 && !this._completion.Task.IsCompleted;

        /// <summary>
        /// Switches the light on or off.
        /// </summary>
        /// <param name="on"><c>true</c> to switch on, <c>false</c> to switch off.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetPowerAsync(bool on, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_power", [on ? "on" : "off", effect, duration], cancellationToken);
        }

        /// <summary>
        /// Switches the light off when it is on, and on when it is off.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task ToggleAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("toggle", [], cancellationToken);
        }

        /// <summary>
        /// Sets the brightness.
        /// </summary>
        /// <param name="brightness">The brightness, a percentage from 1 to 100.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetBrightnessAsync(int brightness, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.Brightness(brightness, nameof(brightness));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_bright", [brightness, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour temperature.
        /// </summary>
        /// <param name="kelvin">The colour temperature, from 1700 to 6500.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetColorTemperatureAsync(int kelvin, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.ColorTemperature(kelvin, nameof(kelvin));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_ct_abx", [kelvin, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour.
        /// </summary>
        /// <param name="red">The red part of the colour, 0 to 255.</param>
        /// <param name="green">The green part of the colour, 0 to 255.</param>
        /// <param name="blue">The blue part of the colour, 0 to 255.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetRgbAsync(int red, int green, int blue, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            var rgb = Validate.Rgb(red, green, blue);
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_rgb", [rgb, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour.
        /// </summary>
        /// <param name="color">The colour. Its alpha is ignored.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetRgbAsync(Color color, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            return this.SetRgbAsync(color.R, color.G, color.B, transition, cancellationToken);
        }

        /// <summary>
        /// Sets the colour as a hue and a saturation.
        /// </summary>
        /// <param name="hue">The hue, 0 to 359.</param>
        /// <param name="saturation">The saturation, 0 to 100.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SetHsvAsync(int hue, int saturation, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.Hue(hue, nameof(hue));
            Validate.Saturation(saturation, nameof(saturation));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_hsv", [hue, saturation, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sends any command, for what this class has no method for (the commands of the background light, for one: <c>bg_set_rgb</c>).
        /// The method is sent as it is.
        /// </summary>
        /// <param name="method">The method, as the protocol names it.</param>
        /// <param name="parameters">The parameters: numbers, text and truth values.</param>
        /// <param name="cancellationToken">To stop waiting for the connection.</param>
        /// <returns>A task that ends when the command has been sent.</returns>
        /// <exception cref="ArgumentException"><paramref name="method"/> is empty, or a parameter is not a number, text or a truth value.</exception>
        /// <exception cref="YeelightException">The connection is lost.</exception>
        public Task SendCommandAsync(string method, IReadOnlyList<object?>? parameters = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(method);
            return this.SendAsync(method, parameters ?? [], cancellationToken);
        }

        /// <summary>
        /// Ends the session: the connection is closed, and the device leaves music mode.
        /// </summary>
        /// <returns>A task that ends when the session is closed.</returns>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            this.EndTransport();

            // The device leaves music mode when the connection closes; this tells it on the control connection as well, when there is one.
            await SwitchOffAsync(this._device).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts music mode: listens, tells the device where, and waits for it to connect.
        /// </summary>
        internal static async Task<YeelightMusicSession> StartAsync(YeelightDevice device, IPAddress address, int port, IPAddress peer, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(address, port);
            try
            {
                listener.Start(1);
            }
            catch (SocketException ex)
            {
                // A port that is in use, or an address that this computer does not have.
                throw new YeelightException(
                    YeelightError.Create(YeelightFailureKind.NotConnected, $"This computer cannot listen on {address}:{port} for music mode: {ex.Message}"),
                    "set_music");
            }

            try
            {
                var endpoint = (IPEndPoint)listener.LocalEndpoint;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(AcceptTimeout);
                var accepting = AcceptFromAsync(listener, peer, timeout.Token);
                TcpClient client;
                var mayBeOn = true;
                try
                {
                    var result = await device.ExecuteAsync("set_music", [1, address.ToString(), endpoint.Port], cancellationToken).ConfigureAwait(false);

                    // A light that refused, or a command that never left, is not in music mode. One that did not answer, or that is not
                    // asked any more (the wait was cancelled), may be.
                    mayBeOn = result.Error is null or { Kind: YeelightFailureKind.Timeout or YeelightFailureKind.ConnectionLost };
                    result.EnsureSuccess("set_music");
                    client = await accepting.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await StopWaitingAsync(device, mayBeOn).ConfigureAwait(false);
                    throw new YeelightException(
                        YeelightError.Create(YeelightFailureKind.NotConnected, $"The light did not connect to {address}:{endpoint.Port} for music mode. Does the firewall of this computer let it in?"),
                        "set_music");
                }
                catch
                {
                    // Stop the wait for the device, and see what became of it: nobody else will.
                    timeout.Cancel();
                    try
                    {
                        (await accepting.ConfigureAwait(false)).Dispose();
                    }
                    catch (Exception)
                    {
                        // The wait ended without a connection, as it was meant to.
                    }

                    await StopWaitingAsync(device, mayBeOn).ConfigureAwait(false);
                    throw;
                }

                client.NoDelay = true;
                device.Context.Logger.LogInformation("{Light}: Music mode on, over {Endpoint}.", device.Id, client.Client.RemoteEndPoint);
                return new YeelightMusicSession(device, client);
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Tells the device that there is no music mode after all. A light that was told to connect and never did stays in music mode until
        /// it is told otherwise (or a connection that it made is closed): left alone it would not report changes any more.
        /// </summary>
        private static Task StopWaitingAsync(YeelightDevice device, bool mayBeOn)
        {
            return mayBeOn ? SwitchOffAsync(device) : Task.CompletedTask;
        }

        private static async Task SwitchOffAsync(YeelightDevice device)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await device.ExecuteAsync("set_music", [0], timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or YeelightException)
            {
                device.Context.Logger.LogDebug("{Light}: Music mode was not switched off on the control connection: {Reason}", device.Id, ex.Message);
            }
        }

        private static async Task<TcpClient> AcceptFromAsync(TcpListener listener, IPAddress peer, CancellationToken cancellationToken)
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (remote is { IsIPv4MappedToIPv6: true })
                {
                    remote = remote.MapToIPv4();
                }

                if (peer.Equals(remote))
                {
                    return client;
                }

                // Somebody else on the network found the port first: that is not the device.
                client.Dispose();
            }
        }

        private async Task SendAsync(string method, IReadOnlyList<object?> parameters, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this._disposed) != 0, this);
            if (this._completion.Task.IsCompleted)
            {
                throw new YeelightException(YeelightError.Create(YeelightFailureKind.ConnectionLost, "The music mode connection to the light was closed."), method);
            }

            var request = YeelightProtocol.BuildRequest(this.NextId(), method, parameters);
            try
            {
                await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await this._stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    this._writeLock.Release();
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                this.EndTransport();
                throw new YeelightException(
                    YeelightError.Create(YeelightFailureKind.ConnectionLost, $"The music mode connection to the light was lost: {ex.Message}"),
                    method);
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

        /// <summary>
        /// Ends the connection of the session, whoever ended it: closed by the application, by the device, or by an error. The handle of the
        /// connection is not kept by a session that is over.
        /// </summary>
        private void EndTransport()
        {
            this._lifetime.Cancel();
            this._client.Dispose();
            this._completion.TrySetResult();
        }

        /// <summary>
        /// Reads what the device sends, which is nothing in music mode, to find out when it closes the connection.
        /// </summary>
        private async Task WatchAsync()
        {
            var buffer = new byte[256];
            try
            {
                while (await this._stream.ReadAsync(buffer, this._lifetime.Token).ConfigureAwait(false) > 0)
                {
                    // Not a message of the protocol in music mode; what a device may send is not kept.
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The connection ended.
            }
            finally
            {
                this.EndTransport();
            }
        }
    }
}
