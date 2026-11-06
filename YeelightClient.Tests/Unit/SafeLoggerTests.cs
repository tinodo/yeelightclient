namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// Logging is the one thing that the client never lets fail anything: whatever the logger of the application does (a sink that is full,
    /// a formatter that throws on a value), the client goes on.
    /// </summary>
    public class SafeLoggerTests
    {
        [Fact]
        public void Wrap_ANullLogger_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => SafeLogger.Wrap(null!));

            Assert.Equal("logger", refusal.ParamName);
        }

        [Fact]
        public void Wrap_GivesALoggerThatIsNotTheOneOfTheApplication_AndWrapsOnlyOnce()
        {
            var inner = new FakeLogger();

            var wrapped = SafeLogger.Wrap(inner);

            Assert.NotSame(inner, wrapped);
            Assert.IsType<SafeLogger>(wrapped);
            Assert.Same(wrapped, SafeLogger.Wrap(wrapped));
        }

        [Fact]
        public void Log_HandsEverythingToTheLoggerOfTheApplication()
        {
            var inner = new FakeLogger();
            var wrapped = SafeLogger.Wrap(inner);
            var exception = new InvalidOperationException("boom");

            wrapped.Log(LogLevel.Warning, new EventId(7, "seven"), "the state", exception, (state, error) => $"{state} / {error?.Message}");

            var entry = Assert.Single(inner.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal(new EventId(7, "seven"), entry.EventId);
            Assert.Equal("the state / boom", entry.Message);
            Assert.Same(exception, entry.Exception);
        }

        [Fact]
        public void Log_AMessageThatTheExtensionsFormat_ArrivesFormatted()
        {
            var inner = new FakeLogger();
            var wrapped = SafeLogger.Wrap(inner);

            wrapped.LogInformation("{Light}: {Count} lights", "0x1", 3);

            Assert.Equal("0x1: 3 lights", Assert.Single(inner.Entries).Message);
        }

        [Fact]
        public void Log_ALoggerThatThrows_DoesNotThrow()
        {
            var inner = new FakeLogger { ThrowOnLog = true };
            var wrapped = SafeLogger.Wrap(inner);

            wrapped.LogError(new InvalidOperationException("the real problem"), "Something failed: {Reason}", "a reason");
            wrapped.LogDebug("Something else");

            Assert.Equal(2, inner.LogCalls);
        }

        [Fact]
        public void Log_AFormatterThatThrows_DoesNotThrow()
        {
            var wrapped = SafeLogger.Wrap(new FakeLogger());

            wrapped.Log<string>(LogLevel.Information, default, "state", null, (_, _) => throw new FormatException("a value that cannot be formatted"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsEnabled_SaysWhatTheLoggerOfTheApplicationSays(bool enabled)
        {
            var wrapped = SafeLogger.Wrap(new FakeLogger { Enabled = enabled });

            Assert.Equal(enabled, wrapped.IsEnabled(LogLevel.Debug));
        }

        [Fact]
        public void IsEnabled_ALoggerThatThrows_IsNotEnabled()
        {
            var wrapped = SafeLogger.Wrap(new FakeLogger { ThrowOnIsEnabled = true });

            Assert.False(wrapped.IsEnabled(LogLevel.Critical));
        }

        [Fact]
        public void BeginScope_GivesTheScopeOfTheLoggerOfTheApplication()
        {
            var inner = new FakeLogger();
            var wrapped = SafeLogger.Wrap(inner);

            using var scope = wrapped.BeginScope("a scope");

            Assert.NotNull(scope);
            Assert.Equal(["a scope"], inner.Scopes.Select(state => state.ToString()!).ToArray());
        }

        [Fact]
        public void BeginScope_ALoggerThatThrows_GivesNoScope()
        {
            var wrapped = SafeLogger.Wrap(new FakeLogger { ThrowOnBeginScope = true });

            Assert.Null(wrapped.BeginScope("a scope"));
        }

        [Fact]
        public async Task AClientWithALoggerThatThrowsAtEveryCall_GoesOnDoingWhatItDoes()
        {
            var logger = new FakeLogger { ThrowOnLog = true, ThrowOnIsEnabled = true, ThrowOnBeginScope = true };
            await using var client = UnitDevices.Client(logger: logger, maxDevices: 1);
            var raised = new List<string>();
            client.DeviceDiscovered += (_, args) =>
            {
                lock (raised)
                {
                    raised.Add(args.Device.Id);
                }
            };
            client.DeviceDiscovered += (_, _) => throw new InvalidOperationException("The handler is broken, and logging that fails too.");

            var first = UnitDevices.Add(client, UnitMessages.Response(id: "0x1"));
            var refused = client.Apply(UnitMessages.Parse(UnitMessages.Response(id: "0x2")));
            await UnitDevices.FlushAsync(first);
            var later = UnitDevices.Add(client, UnitMessages.Response(id: "0x1", name: "renamed"));

            Assert.Same(first, later);
            Assert.Null(refused);
            Assert.Equal("renamed", first.Name);
            Assert.Equal(["0x1"], raised.ToArray());
        }

        private sealed class FakeLogger : ILogger<YeelightClient>
        {
            private readonly object _gate = new();
            private readonly List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> _entries = [];
            private readonly List<object> _scopes = [];
            private int _logCalls;

            public bool Enabled { get; init; } = true;

            public bool ThrowOnLog { get; init; }

            public bool ThrowOnIsEnabled { get; init; }

            public bool ThrowOnBeginScope { get; init; }

            public int LogCalls => Volatile.Read(ref this._logCalls);

            public IReadOnlyList<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries
            {
                get
                {
                    lock (this._gate)
                    {
                        return [.. this._entries];
                    }
                }
            }

            public IReadOnlyList<object> Scopes
            {
                get
                {
                    lock (this._gate)
                    {
                        return [.. this._scopes];
                    }
                }
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                if (this.ThrowOnBeginScope)
                {
                    throw new InvalidOperationException("The scopes of this logger are broken.");
                }

                lock (this._gate)
                {
                    this._scopes.Add(state);
                }

                return new Scope();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                if (this.ThrowOnIsEnabled)
                {
                    throw new InvalidOperationException("The levels of this logger are broken.");
                }

                return this.Enabled;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Interlocked.Increment(ref this._logCalls);
                if (this.ThrowOnLog)
                {
                    throw new InvalidOperationException("The sink of this logger is full.");
                }

                lock (this._gate)
                {
                    this._entries.Add((logLevel, eventId, formatter(state, exception), exception));
                }
            }

            private sealed class Scope : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
