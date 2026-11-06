namespace Yeelight
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Wraps the logger of the application so that a logger that throws (a sink that is full, a formatter that fails on a value) cannot
    /// stop the client from doing what it does: logging is the one thing that the client never lets fail anything.
    /// </summary>
    internal sealed class SafeLogger : ILogger
    {
        private readonly ILogger _inner;

        private SafeLogger(ILogger inner)
        {
            this._inner = inner;
        }

        /// <summary>
        /// Wraps a logger.
        /// </summary>
        public static ILogger Wrap(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            return logger as SafeLogger ?? new SafeLogger(logger);
        }

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            try
            {
                return this._inner.BeginScope(state);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return this._inner.IsEnabled(logLevel);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                this._inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception)
            {
                // A message that cannot be written is lost; what the client was doing goes on.
            }
        }
    }
}
