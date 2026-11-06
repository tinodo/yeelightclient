namespace Yeelight
{
    using System;

    /// <summary>
    /// Thrown when a command does not succeed: the light refused it, or it did not get to the light, or its answer did not come back.
    /// </summary>
    /// <remarks>
    /// The client and the connection are fine after this exception: a failure of one command does not end the connection, and a lost
    /// connection is made again by the client. <see cref="Kind"/> tells which of the two it is. A command that was cancelled throws an
    /// <see cref="OperationCanceledException"/> and not this.
    /// </remarks>
    public class YeelightException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="YeelightException"/> class.
        /// </summary>
        public YeelightException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YeelightException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public YeelightException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YeelightException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public YeelightException(string message, Exception innerException) : base(message, innerException)
        {
        }

        internal YeelightException(YeelightError error, string? method) : base(BuildMessage(error, method))
        {
            this.Error = error;
            this.Method = method;
        }

        /// <summary>
        /// Gets the error that the command ended with, or <c>null</c> for an exception that was not made from one.
        /// </summary>
        public YeelightError? Error { get; }

        /// <summary>
        /// Gets the method of the command that failed (for example <c>set_power</c>), when it is known.
        /// </summary>
        public string? Method { get; }

        /// <summary>
        /// Gets why the command did not succeed.
        /// </summary>
        public YeelightFailureKind Kind => this.Error?.Kind ?? YeelightFailureKind.DeviceRejected;

        private static string BuildMessage(YeelightError error, string? method)
        {
            if (error.IsTransportFailure)
            {
                return error.Message;
            }

            var what = method is null ? "the command" : $"'{method}'";
            return $"The light refused {what}: {error.Message} (code {error.Code}).";
        }
    }
}
