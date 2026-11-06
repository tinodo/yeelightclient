namespace Yeelight
{
    using System;

    /// <summary>
    /// The error that a command ended with: what a light answered in the <c>error</c> member of its answer, or why the answer never came.
    /// </summary>
    /// <param name="Code">
    /// The code. A light answers with <c>-1</c> for everything that it refuses; the codes from <c>-100</c> down are the client's own, for
    /// what went wrong on the way to the light (see <see cref="Kind"/>).
    /// </param>
    /// <param name="Message">The message of the light (for example <c>unsupported method</c> or <c>client quota exceeded</c>), or of the client.</param>
    public sealed record YeelightError(int Code, string Message)
    {
        internal const int NotConnectedCode = -100;

        internal const int TimeoutCode = -101;

        internal const int RateLimitedCode = -102;

        internal const int ConnectionLostCode = -103;

        /// <summary>
        /// Gets whether the light said this: it chooses its own codes, and says what it likes. Whatever the number is, a light that answers
        /// has refused the command, and the way to it did not fail.
        /// </summary>
        private bool IsFromLight { get; init; }

        /// <summary>
        /// Gets what kind of failure this is: a refusal of the light, or a failure of the way to it.
        /// </summary>
        public YeelightFailureKind Kind => this.IsFromLight
            ? YeelightFailureKind.DeviceRejected
            : this.Code switch
            {
                NotConnectedCode => YeelightFailureKind.NotConnected,
                TimeoutCode => YeelightFailureKind.Timeout,
                RateLimitedCode => YeelightFailureKind.RateLimited,
                ConnectionLostCode => YeelightFailureKind.ConnectionLost,
                _ => YeelightFailureKind.DeviceRejected,
            };

        /// <summary>
        /// Gets whether the way to the light failed, as opposed to the light refusing the command.
        /// </summary>
        public bool IsTransportFailure => this.Kind != YeelightFailureKind.DeviceRejected;

        /// <inheritdoc/>
        public bool Equals(YeelightError? other)
        {
            return other is not null && this.Code == other.Code && string.Equals(this.Message, other.Message, StringComparison.Ordinal) && this.Kind == other.Kind;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Code, this.Message, this.Kind);
        }

        internal static YeelightError Create(YeelightFailureKind kind, string message)
        {
            var code = kind switch
            {
                YeelightFailureKind.NotConnected => NotConnectedCode,
                YeelightFailureKind.Timeout => TimeoutCode,
                YeelightFailureKind.RateLimited => RateLimitedCode,
                YeelightFailureKind.ConnectionLost => ConnectionLostCode,
                _ => -1,
            };
            return new YeelightError(code, message);
        }

        /// <summary>
        /// Makes the error of an answer of a light, which is a refusal whatever its code is: a light that answers with -101 did not time out.
        /// </summary>
        internal static YeelightError FromLight(int code, string message)
        {
            return new YeelightError(code, message) { IsFromLight = true };
        }
    }
}
