namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Raises the events of the client, one at a time and in the order in which they happened, on a thread of the thread pool that is
    /// never the thread that reads from a light.
    /// </summary>
    /// <remarks>
    /// The code that reads the connection to a light only adds to the queue, so that a handler can wait for the answer to a command (which the
    /// reading has to do) without keeping it from doing so. Once the dispatcher is disposed nothing new is started: what was waiting is
    /// dropped, and a notification that is under way stops before its next handler. A handler that is running is neither interrupted nor
    /// waited for. The notification of a change of a property carries only the name of the property, so it is queued once for as long as it
    /// waits: a handler that is slower than a light changes its properties still sees every property that changed, with its latest value,
    /// and the queue stays as small as the number of properties that there are, whatever a light sends.
    /// </remarks>
    internal sealed class EventDispatcher : IDisposable
    {
        private const int BacklogWarning = 1000;

        private readonly Channel<Item> _queue = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        private readonly HashSet<(object Owner, string Key)> _waitingKeys = new(KeyComparer.Instance);
        private readonly ILogger _logger;
        private volatile bool _disposed;
        private int _waiting;
        private bool _warned;

        public EventDispatcher(ILogger logger)
        {
            this._logger = logger;
            _ = Task.Run(this.RunAsync);
        }

        /// <summary>
        /// Gets whether the dispatcher was disposed: nothing is raised any more.
        /// </summary>
        public bool IsDisposed => this._disposed;

        /// <summary>
        /// Queues a notification.
        /// </summary>
        public void Post(Action notification)
        {
            this.Enqueue(new Item(notification, null, null));
        }

        /// <summary>
        /// Queues the notification of a change of one thing (a property of an object), unless the same one is already waiting: the notification
        /// reads what the thing is when it runs, so one that waits already covers the changes that come after it.
        /// </summary>
        public void Post(object owner, string key, Action notification)
        {
            if (this._disposed)
            {
                return;
            }

            lock (this._waitingKeys)
            {
                if (!this._waitingKeys.Add((owner, key)))
                {
                    return;
                }
            }

            this.Enqueue(new Item(notification, owner, key));
        }

        /// <summary>
        /// Calls every handler of an event, one after the other. A handler that throws is reported to the log, and the others are still called.
        /// </summary>
        public void Raise<TEventArgs>(EventHandler<TEventArgs>? handler, object sender, TEventArgs args)
        {
            if (handler is null)
            {
                return;
            }

            foreach (var item in handler.GetInvocationList())
            {
                if (this._disposed)
                {
                    return;
                }

                try
                {
                    ((EventHandler<TEventArgs>)item)(sender, args);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "A handler of an event of the client threw an exception.");
                }
            }
        }

        /// <summary>
        /// Calls every handler of a <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/> event, one after the other.
        /// A handler that throws is reported to the log, and the others are still called.
        /// </summary>
        public void RaisePropertyChanged(PropertyChangedEventHandler? handler, object sender, PropertyChangedEventArgs args)
        {
            if (handler is null)
            {
                return;
            }

            foreach (var item in handler.GetInvocationList())
            {
                if (this._disposed)
                {
                    return;
                }

                try
                {
                    ((PropertyChangedEventHandler)item)(sender, args);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "A handler of PropertyChanged threw an exception.");
                }
            }
        }

        public void Dispose()
        {
            this._disposed = true;
            this._queue.Writer.TryComplete();
            lock (this._waitingKeys)
            {
                this._waitingKeys.Clear();
            }
        }

        private void Enqueue(Item item)
        {
            if (this._disposed)
            {
                this.Forget(item);
                return;
            }

            var waiting = Interlocked.Increment(ref this._waiting);
            if (waiting >= BacklogWarning && !this._warned)
            {
                this._warned = true;
                this._logger.LogWarning("{Count} notifications are waiting: the handlers of the application are slower than the lights are.", waiting);
            }

            if (!this._queue.Writer.TryWrite(item))
            {
                Interlocked.Decrement(ref this._waiting);
                this.Forget(item);
            }
        }

        private void Forget(Item item)
        {
            if (item.Owner is not null && item.Key is not null)
            {
                lock (this._waitingKeys)
                {
                    this._waitingKeys.Remove((item.Owner, item.Key));
                }
            }
        }

        private async Task RunAsync()
        {
            var reader = this._queue.Reader;
            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        Interlocked.Decrement(ref this._waiting);

                        // From here on a change of the same thing is news again: it happens after the state that this notification reads.
                        this.Forget(item);
                        if (this._disposed)
                        {
                            continue;
                        }

                        try
                        {
                            item.Notification();
                        }
                        catch (Exception ex)
                        {
                            this._logger.LogError(ex, "A notification of the client could not be delivered.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "The delivery of the notifications of the client stopped.");
            }
        }

        private readonly record struct Item(Action Notification, object? Owner, string? Key);

        /// <summary>The thing that changed is the object itself, whatever its type says about when two objects are equal.</summary>
        private sealed class KeyComparer : IEqualityComparer<(object Owner, string Key)>
        {
            public static readonly KeyComparer Instance = new();

            public bool Equals((object Owner, string Key) x, (object Owner, string Key) y)
            {
                return ReferenceEquals(x.Owner, y.Owner) && string.Equals(x.Key, y.Key, StringComparison.Ordinal);
            }

            public int GetHashCode((object Owner, string Key) value)
            {
                return HashCode.Combine(RuntimeHelpers.GetHashCode(value.Owner), StringComparer.Ordinal.GetHashCode(value.Key));
            }
        }
    }
}
