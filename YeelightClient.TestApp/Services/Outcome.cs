namespace Yeelight.TestApp.Services
{
    using System;
    using System.Globalization;

    /// <summary>How a command ended, for the colour of the message that tells so.</summary>
    internal enum OutcomeSeverity
    {
        /// <summary>The light accepted the command.</summary>
        Success,

        /// <summary>Nothing is wrong with the app, but the command did not do anything (cancelled).</summary>
        Informational,

        /// <summary>The light or the client refused the command: an unsupported method, a value out of range, a light that is off.</summary>
        Warning,

        /// <summary>The command did not get through, or something that was not expected went wrong.</summary>
        Error,
    }

    /// <summary>
    /// What came of a command: the message that the pages show in their status bar. Every kind of failure of the library is a message with a
    /// title and the words of the one that refused, and never a crash.
    /// </summary>
    /// <param name="Severity">How it ended.</param>
    /// <param name="Title">What was done, and how it ended, in a few words.</param>
    /// <param name="Message">The details: the time it took, or the kind of the failure, the error code and the message.</param>
    /// <param name="Elapsed">How long the command took.</param>
    internal sealed record Outcome(OutcomeSeverity Severity, string Title, string Message, TimeSpan Elapsed)
    {
        /// <summary>Gets a value indicating whether the command was accepted.</summary>
        public bool IsSuccess => this.Severity == OutcomeSeverity.Success;

        /// <summary>Gets the outcome on one line, for a log.</summary>
        public string Summary => this.Message.Length == 0 ? this.Title : this.Title + " \u2014 " + this.Message;

        /// <summary>Makes the outcome of a command that was accepted.</summary>
        /// <param name="what">What was done, for example <c>Set brightness</c>.</param>
        /// <param name="elapsed">How long it took.</param>
        /// <returns>The outcome.</returns>
        public static Outcome Succeeded(string what, TimeSpan elapsed)
        {
            return new Outcome(OutcomeSeverity.Success, $"{what}: succeeded", $"The light answered in {FormatElapsed(elapsed)}.", elapsed);
        }

        /// <summary>Makes the outcome of a command that was accepted, with a note about what came back.</summary>
        /// <param name="what">What was done.</param>
        /// <param name="note">What the light said, or what the app found.</param>
        /// <param name="elapsed">How long it took.</param>
        /// <returns>The outcome.</returns>
        public static Outcome Succeeded(string what, string note, TimeSpan elapsed)
        {
            return new Outcome(OutcomeSeverity.Success, $"{what}: succeeded", $"{note} ({FormatElapsed(elapsed)}).", elapsed);
        }

        /// <summary>Makes the outcome of a command from the exception that it ended with.</summary>
        /// <param name="what">What was being done, for example <c>Set brightness</c>.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="elapsed">How long it took until it failed.</param>
        /// <returns>The outcome. Nothing is thrown here: any exception can be described.</returns>
        public static Outcome Failed(string what, Exception exception, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var time = FormatElapsed(elapsed);
            switch (exception)
            {
                case YeelightException yeelight:
                    {
                        var code = yeelight.Error?.Code;
                        var text = yeelight.Error?.Message ?? yeelight.Message;
                        var method = yeelight.Method is { Length: > 0 } name ? $" '{name}'" : string.Empty;
                        var details = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Kind {yeelight.Kind}, error code {(code is { } value ? value.ToString(CultureInfo.InvariantCulture) : "none")}: {OneLine(text)} (after {time}).");
                        return yeelight.Kind == YeelightFailureKind.DeviceRejected
                            ? new Outcome(OutcomeSeverity.Warning, $"{what}: the light refused{method}", details, elapsed)
                            : new Outcome(OutcomeSeverity.Error, $"{what}: it did not get through", details, elapsed);
                    }

                case NotSupportedException:
                    return new Outcome(OutcomeSeverity.Warning, $"{what}: this light does not support it", OneLine(exception.Message), elapsed);
                case ArgumentException:
                    return new Outcome(OutcomeSeverity.Warning, $"{what}: the client refused the arguments", OneLine(exception.Message), elapsed);
                case OperationCanceledException:
                    return new Outcome(OutcomeSeverity.Informational, $"{what}: cancelled", $"The wait was cancelled after {time}.", elapsed);
                case ObjectDisposedException:
                    return new Outcome(OutcomeSeverity.Warning, $"{what}: the client was closed", "The client of this light was disposed (the settings were applied, or the app is closing): choose the light again.", elapsed);
                default:
                    return new Outcome(OutcomeSeverity.Error, $"{what}: {exception.GetType().Name}", OneLine(exception.Message), elapsed);
            }
        }

        /// <summary>Writes a duration the way a person reads it: milliseconds up to a second, seconds above it.</summary>
        /// <param name="elapsed">The duration.</param>
        /// <returns>For example <c>12 ms</c> or <c>1.5 s</c>.</returns>
        public static string FormatElapsed(TimeSpan elapsed)
        {
            return elapsed.TotalSeconds < 1
                ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, Math.Round(elapsed.TotalMilliseconds)):0} ms")
                : string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:0.0} s");
        }

        private static string OneLine(string text)
        {
            return string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
}
