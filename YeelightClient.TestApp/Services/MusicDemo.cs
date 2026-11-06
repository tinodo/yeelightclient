namespace Yeelight.TestApp.Services
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>What the music mode demo did.</summary>
    /// <param name="CommandsSent">How many commands were sent over the session.</param>
    /// <param name="Elapsed">How long the demo ran.</param>
    /// <param name="Cancelled">Whether it was stopped before its time was up.</param>
    /// <param name="ConnectionLost">Whether the session ended while the demo ran.</param>
    internal sealed record MusicDemoResult(int CommandsSent, TimeSpan Elapsed, bool Cancelled, bool ConnectionLost)
    {
        /// <summary>Gets how many commands a second were sent, on average.</summary>
        public double CommandsPerSecond => this.Elapsed.TotalSeconds > 0 ? this.CommandsSent / this.Elapsed.TotalSeconds : 0;

        /// <summary>Describes what happened, and what it shows: that a light in music mode takes more than the 60 commands a minute of a control connection.</summary>
        /// <returns>The words.</returns>
        public string Describe()
        {
            var how = this.ConnectionLost ? "The session ended. " : this.Cancelled ? "Stopped. " : string.Empty;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{how}Sent {this.CommandsSent} commands in {this.Elapsed.TotalSeconds:0.0} s ({this.CommandsPerSecond:0.0} a second, {this.CommandsPerSecond * 60:0} a minute). A light takes {YeelightLimits.CommandsPerMinute} a minute on a control connection; music mode has no quota.");
        }
    }

    /// <summary>
    /// The demo of music mode: a colour that goes round the rainbow (a light without colours breathes in brightness), about twenty commands a
    /// second, over the session. It is what music mode is for: no control connection takes that many commands.
    /// </summary>
    internal static class MusicDemo
    {
        /// <summary>The number of commands a second that the demo sends.</summary>
        public const int CommandsPerSecond = 20;

        /// <summary>Runs the demo.</summary>
        /// <param name="session">The music session.</param>
        /// <param name="useColor">Whether the light has colours: the demo then goes round the rainbow, with <c>set_hsv</c> and now and then <c>set_rgb</c>; otherwise it changes the brightness.</param>
        /// <param name="duration">How long it runs.</param>
        /// <param name="rate">The number of commands a second.</param>
        /// <param name="progress">Told how many commands were sent, after each.</param>
        /// <param name="cancellationToken">To stop the demo.</param>
        /// <returns>What was done. Nothing is thrown for a session that ends, or for a stop: that is in the result.</returns>
        public static async Task<MusicDemoResult> RunAsync(
            YeelightMusicSession session,
            bool useColor,
            TimeSpan duration,
            int rate = CommandsPerSecond,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentOutOfRangeException.ThrowIfLessThan(rate, 1);
            var period = TimeSpan.FromSeconds(1.0 / rate);
            var clock = Stopwatch.StartNew();
            var sent = 0;
            var cancelled = false;
            var lost = false;
            try
            {
                while (clock.Elapsed < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seconds = clock.Elapsed.TotalSeconds;
                    if (useColor)
                    {
                        // One round of the rainbow every four seconds; every fifth command says the same colour as RGB.
                        var hue = (int)(seconds / 4 * 360 % 360);
                        if (sent % 5 == 4)
                        {
                            var color = LightFormat.HsvToRgb(hue, 100, 100);
                            await session.SetRgbAsync(color.R, color.G, color.B, null, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await session.SetHsvAsync(hue, 100, null, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var level = 10 + (int)(90 * (0.5 + (0.5 * Math.Sin(seconds * Math.PI))));
                        await session.SetBrightnessAsync(Math.Clamp(level, YeelightLimits.MinBrightness, YeelightLimits.MaxBrightness), null, cancellationToken).ConfigureAwait(false);
                    }

                    sent++;
                    progress?.Report(sent);

                    // Keep to the rate: the next command is due a period after the one before it, whatever the timer of the system rounds to.
                    var wait = (period * sent) - clock.Elapsed;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (YeelightException)
            {
                lost = true;
            }
            catch (ObjectDisposedException)
            {
                lost = true;
            }

            return new MusicDemoResult(sent, clock.Elapsed, cancelled, lost || !session.IsConnected);
        }
    }
}
