namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// The answer to a command, as the light gave it (or the failure that took its place).
    /// </summary>
    /// <remarks>
    /// This is what <see cref="YeelightLight.SendCommandAsync"/> returns.
    /// It does not throw for a command that did not succeed: look at <see cref="Success"/>, or call <see cref="EnsureSuccess"/>.
    /// </remarks>
    public sealed class YeelightResult
    {
        internal YeelightResult(int id, YeelightError? error, IReadOnlyList<JsonElement> values)
        {
            this.Id = id;
            this.Error = error;
            this.Values = values;
        }

        /// <summary>
        /// Gets the number of the command that this answers, which is unique on the connection. It is <c>0</c> for a failure that has no answer.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the error, or <c>null</c> when the light accepted the command.
        /// </summary>
        public YeelightError? Error { get; }

        /// <summary>
        /// Gets the values of the answer: <c>"ok"</c> for a command that was carried out, the values of the properties for <c>get_prop</c>,
        /// the timers for <c>cron_get</c>. A valid answer can be empty (the list of timers of a light that has none).
        /// </summary>
        public IReadOnlyList<JsonElement> Values { get; }

        /// <summary>
        /// Gets whether the light accepted the command, which is when it did not answer with an error.
        /// </summary>
        public bool Success => this.Error is null;

        /// <summary>
        /// Gets a value of the answer as text.
        /// </summary>
        /// <param name="index">The place of the value in <see cref="Values"/>.</param>
        /// <returns>The text, or <c>null</c> when there is no such value or it is not a string or a number.</returns>
        public string? GetString(int index = 0)
        {
            if (index < 0 || index >= this.Values.Count)
            {
                return null;
            }

            return YeelightProtocol.ToText(this.Values[index]);
        }

        /// <summary>
        /// Throws a <see cref="YeelightException"/> when the command did not succeed.
        /// </summary>
        /// <param name="method">The method of the command, for the message.</param>
        /// <returns>This result, so that a call can be chained.</returns>
        /// <exception cref="YeelightException">The light refused the command, or it did not get an answer.</exception>
        public YeelightResult EnsureSuccess(string? method = null)
        {
            if (this.Error is { } error)
            {
                throw new YeelightException(error, method);
            }

            return this;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return this.Error is { } error
                ? $"Failed {this.Id}: {error.Message} ({error.Code})"
                : $"Result {this.Id}: [{string.Join(", ", this.Values.Select(value => YeelightProtocol.ToText(value) ?? value.GetRawText()))}]";
        }

        internal static YeelightResult Failed(YeelightFailureKind kind, string message)
        {
            return new YeelightResult(0, YeelightError.Create(kind, message), []);
        }
    }
}
