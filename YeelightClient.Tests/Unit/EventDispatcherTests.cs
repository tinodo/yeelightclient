namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The events of the client are raised one at a time, in the order in which they happened, on a thread of the pool that is not the one that
    /// reads from a light; so that a handler can wait for the answer to a command without keeping the reading from delivering it. The
    /// notification of a change of a property is queued once for as long as it waits.
    /// </summary>
    public class EventDispatcherTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        /// <summary>Posts a notification that waits, and returns when it is running: what is posted from now on queues behind it.</summary>
        private static async Task<ManualResetEventSlim> BlockAsync(EventDispatcher dispatcher)
        {
            var gate = new ManualResetEventSlim(false);
            var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(() =>
            {
                running.TrySetResult();
                gate.Wait(Patience);
            });
            await running.Task.WaitAsync(Patience);
            return gate;
        }

        /// <summary>Waits until everything that was posted before has been delivered.</summary>
        private static async Task FlushAsync(EventDispatcher dispatcher)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(() => done.TrySetResult());
            await done.Task.WaitAsync(Patience);
        }

        [Fact]
        public async Task Notifications_AreDeliveredInTheOrderInWhichTheyWerePosted()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var order = new ConcurrentQueue<int>();

            for (var number = 0; number < 500; number++)
            {
                var copy = number;
                dispatcher.Post(() => order.Enqueue(copy));
            }

            await FlushAsync(dispatcher);

            Assert.Equal(Enumerable.Range(0, 500), order);
        }

        [Fact]
        public async Task Notifications_AreDeliveredOnAThreadOfThePool_ThatIsNotTheOneThatPosts()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var threads = new ConcurrentBag<(int Id, bool Pool)>();

            dispatcher.Post(() => threads.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread)));
            await FlushAsync(dispatcher);

            var thread = Assert.Single(threads);
            Assert.NotEqual(Environment.CurrentManagedThreadId, thread.Id);
            Assert.True(thread.Pool);
        }

        [Fact]
        public async Task Notifications_NeverRunAtTheSameTime()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var running = 0;
            var overlap = 0;

            Parallel.For(0, 400, _ => dispatcher.Post(() =>
            {
                if (Interlocked.Increment(ref running) != 1)
                {
                    Interlocked.Increment(ref overlap);
                }

                Thread.Yield();
                Interlocked.Decrement(ref running);
            }));
            await FlushAsync(dispatcher);

            Assert.Equal(0, overlap);
        }

        [Fact]
        public async Task AHandlerThatWaits_DoesNotKeepTheOneThatPostsFromPosting()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var gate = await BlockAsync(dispatcher);
            var order = new ConcurrentQueue<int>();

            // The first notification is still running; these only queue up behind it.
            for (var number = 0; number < 10; number++)
            {
                var copy = number;
                dispatcher.Post(() => order.Enqueue(copy));
            }

            Assert.Empty(order);
            gate.Set();
            await FlushAsync(dispatcher);

            Assert.Equal(Enumerable.Range(0, 10), order);
        }

        [Fact]
        public async Task ANotificationThatThrows_IsLogged_AndTheNextOneIsStillDelivered()
        {
            var logger = new ListLogger();
            using var dispatcher = new EventDispatcher(logger);
            var delivered = false;

            dispatcher.Post(() => throw new InvalidOperationException("The notification is broken."));
            dispatcher.Post(() => delivered = true);
            await FlushAsync(dispatcher);

            Assert.True(delivered);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException { Message: "The notification is broken." });
        }

        [Fact]
        public void Raise_CallsEveryHandlerInTheOrderTheyWereAdded_WithTheSenderAndTheArguments()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var calls = new List<string>();
            var sender = new object();
            var arguments = new EventArgs();
            EventHandler<EventArgs>? handler = null;
            handler += (s, e) => calls.Add($"first:{ReferenceEquals(s, sender)}:{ReferenceEquals(e, arguments)}");
            handler += (_, _) => calls.Add("second");
            handler += (_, _) => calls.Add("third");

            dispatcher.Raise(handler, sender, arguments);

            Assert.Equal(["first:True:True", "second", "third"], calls);
        }

        [Fact]
        public void Raise_ANullHandler_IsNobody()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());

            dispatcher.Raise<EventArgs>(null, new object(), EventArgs.Empty);
            dispatcher.RaisePropertyChanged(null, new object(), new PropertyChangedEventArgs("x"));
        }

        [Fact]
        public void Raise_AHandlerThatThrows_IsLogged_AndTheOthersAreStillCalled()
        {
            var logger = new ListLogger();
            using var dispatcher = new EventDispatcher(logger);
            var calls = new List<string>();
            EventHandler<EventArgs>? handler = null;
            handler += (_, _) => calls.Add("first");
            handler += (_, _) => throw new InvalidOperationException("The handler is broken.");
            handler += (_, _) => calls.Add("third");

            dispatcher.Raise(handler, new object(), EventArgs.Empty);

            Assert.Equal(["first", "third"], calls);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException { Message: "The handler is broken." });
        }

        [Fact]
        public void RaisePropertyChanged_CallsEveryHandlerInOrder_AndAHandlerThatThrowsIsLogged()
        {
            var logger = new ListLogger();
            using var dispatcher = new EventDispatcher(logger);
            var calls = new List<string>();
            PropertyChangedEventHandler? handler = null;
            handler += (_, e) => calls.Add("first:" + e.PropertyName);
            handler += (_, _) => throw new InvalidOperationException("The handler is broken.");
            handler += (_, e) => calls.Add("third:" + e.PropertyName);

            dispatcher.RaisePropertyChanged(handler, new object(), new PropertyChangedEventArgs("Brightness"));

            Assert.Equal(["first:Brightness", "third:Brightness"], calls);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
        }

        [Fact]
        public void Raise_StopsBeforeTheNextHandler_WhenTheDispatcherIsDisposedOnTheWay()
        {
            var dispatcher = new EventDispatcher(new ListLogger());
            var calls = new List<string>();
            EventHandler<EventArgs>? handler = null;
            handler += (_, _) =>
            {
                calls.Add("first");
                dispatcher.Dispose();
            };
            handler += (_, _) => calls.Add("second");

            dispatcher.Raise(handler, new object(), EventArgs.Empty);

            Assert.Equal(["first"], calls);
        }

        [Fact]
        public void RaisePropertyChanged_StopsBeforeTheNextHandler_WhenTheDispatcherIsDisposedOnTheWay()
        {
            var dispatcher = new EventDispatcher(new ListLogger());
            var calls = new List<string>();
            PropertyChangedEventHandler? handler = null;
            handler += (_, _) =>
            {
                calls.Add("first");
                dispatcher.Dispose();
            };
            handler += (_, _) => calls.Add("second");

            dispatcher.RaisePropertyChanged(handler, new object(), new PropertyChangedEventArgs("x"));

            Assert.Equal(["first"], calls);
        }

        [Fact]
        public void Raise_AfterTheDispatcherIsDisposed_CallsNobody()
        {
            var dispatcher = new EventDispatcher(new ListLogger());
            var called = false;
            dispatcher.Dispose();

            dispatcher.Raise<EventArgs>((_, _) => called = true, new object(), EventArgs.Empty);
            dispatcher.RaisePropertyChanged((_, _) => called = true, new object(), new PropertyChangedEventArgs("x"));

            Assert.False(called);
        }

        [Fact]
        public void ADispatcher_IsNotDisposedUntilItIs_AndDisposingTwiceIsFine()
        {
            var dispatcher = new EventDispatcher(new ListLogger());

            Assert.False(dispatcher.IsDisposed);
            dispatcher.Dispose();
            dispatcher.Dispose();

            Assert.True(dispatcher.IsDisposed);
        }

        [Fact]
        public async Task AfterTheDispatcherIsDisposed_NothingIsPostedAnyMore()
        {
            var dispatcher = new EventDispatcher(new ListLogger());
            var delivered = 0;
            dispatcher.Dispose();

            dispatcher.Post(() => Interlocked.Increment(ref delivered));
            dispatcher.Post(new object(), "key", () => Interlocked.Increment(ref delivered));

            await Wait.NotHappensAsync(() => Volatile.Read(ref delivered) > 0, "a notification after the dispatcher was disposed", TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task WhatWasWaiting_WhenTheDispatcherIsDisposed_IsDropped_AndWhatWasRunningIsNotInterrupted()
        {
            var dispatcher = new EventDispatcher(new ListLogger());
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var gate = new ManualResetEventSlim(false);
            var droppedRan = 0;
            dispatcher.Post(() =>
            {
                running.TrySetResult();
                gate.Wait(Patience);
                finished.TrySetResult();
            });
            await running.Task.WaitAsync(Patience);
            dispatcher.Post(() => Interlocked.Increment(ref droppedRan));
            dispatcher.Post(new object(), "key", () => Interlocked.Increment(ref droppedRan));

            dispatcher.Dispose();
            gate.Set();

            await finished.Task.WaitAsync(Patience);
            await Wait.NotHappensAsync(() => Volatile.Read(ref droppedRan) > 0, "a notification that was waiting when the dispatcher was disposed", TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task ANotificationOfAPropertyThatIsAlreadyWaiting_IsNotQueuedAgain()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var gate = await BlockAsync(dispatcher);
            var owner = new object();
            var delivered = new ConcurrentQueue<string>();

            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("first"));
            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("second"));
            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("third"));
            gate.Set();
            await FlushAsync(dispatcher);

            Assert.Equal(["first"], delivered);
        }

        [Fact]
        public async Task NotificationsOfOtherPropertiesOrOfOtherObjects_AreQueuedEachOnce()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var gate = await BlockAsync(dispatcher);
            var first = new object();
            var second = new object();
            var delivered = new ConcurrentQueue<string>();

            dispatcher.Post(first, "Brightness", () => delivered.Enqueue("first:Brightness"));
            dispatcher.Post(first, "Hue", () => delivered.Enqueue("first:Hue"));
            dispatcher.Post(second, "Brightness", () => delivered.Enqueue("second:Brightness"));
            dispatcher.Post(first, "Brightness", () => delivered.Enqueue("first:Brightness again"));
            dispatcher.Post(second, "Brightness", () => delivered.Enqueue("second:Brightness again"));
            gate.Set();
            await FlushAsync(dispatcher);

            Assert.Equal(["first:Brightness", "first:Hue", "second:Brightness"], delivered);
        }

        [Fact]
        public async Task AChangeThatComesWhileItsNotificationIsRunning_IsNewsAgain()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var owner = new object();
            var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var gate = new ManualResetEventSlim(false);
            var delivered = new ConcurrentQueue<string>();

            dispatcher.Post(owner, "Brightness", () =>
            {
                delivered.Enqueue("first");
                running.TrySetResult();
                gate.Wait(Patience);
            });
            await running.Task.WaitAsync(Patience);
            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("second"));
            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("third"));
            gate.Set();
            await FlushAsync(dispatcher);

            // The first one read the state before the change, so the change gets a notification of its own (and one covers all that follow).
            Assert.Equal(["first", "second"], delivered);
        }

        [Fact]
        public async Task ANotificationOfAPropertyThatThrew_DoesNotKeepTheNextOneFromBeingQueued()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var owner = new object();
            var delivered = new ConcurrentQueue<string>();

            dispatcher.Post(owner, "Brightness", () => throw new InvalidOperationException("The notification is broken."));
            await FlushAsync(dispatcher);
            dispatcher.Post(owner, "Brightness", () => delivered.Enqueue("next"));
            await FlushAsync(dispatcher);

            Assert.Equal(["next"], delivered);
        }

        [Fact]
        public async Task ANotificationOfAPropertyAndOtherNotifications_KeepTheOrderOfTheirFirstPost()
        {
            using var dispatcher = new EventDispatcher(new ListLogger());
            var gate = await BlockAsync(dispatcher);
            var owner = new object();
            var delivered = new ConcurrentQueue<string>();

            dispatcher.Post(owner, "x", () => delivered.Enqueue("x"));
            dispatcher.Post(() => delivered.Enqueue("plain"));
            dispatcher.Post(owner, "x", () => delivered.Enqueue("x again"));
            dispatcher.Post(owner, "y", () => delivered.Enqueue("y"));
            gate.Set();
            await FlushAsync(dispatcher);

            Assert.Equal(["x", "plain", "y"], delivered);
        }

        [Fact]
        public async Task TheQueue_OfThePropertiesOfOneObject_NeverGrowsBeyondTheNumberOfProperties()
        {
            var logger = new ListLogger();
            using var dispatcher = new EventDispatcher(logger);
            var gate = await BlockAsync(dispatcher);
            var owner = new object();
            var delivered = 0;

            // A light that reports as fast as it can, to a handler that is slower than it is.
            for (var count = 0; count < 20_000; count++)
            {
                dispatcher.Post(owner, "Property" + (count % 10), () => Interlocked.Increment(ref delivered));
            }

            gate.Set();
            await FlushAsync(dispatcher);

            Assert.Equal(10, Volatile.Read(ref delivered));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task ABacklogOfAThousandNotifications_IsWarnedOfOnce()
        {
            var logger = new ListLogger();
            using var dispatcher = new EventDispatcher(logger);
            var gate = await BlockAsync(dispatcher);

            for (var count = 0; count < 999; count++)
            {
                dispatcher.Post(() => { });
            }

            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);

            for (var count = 0; count < 500; count++)
            {
                dispatcher.Post(() => { });
            }

            gate.Set();
            await FlushAsync(dispatcher);

            var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Contains("1000", warning.Message);
            Assert.Contains("notifications are waiting", warning.Message);
        }
    }
}
