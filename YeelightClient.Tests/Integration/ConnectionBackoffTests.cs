namespace Yeelight.Tests.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Xunit;

    /// <summary>
    /// How long the connection waits between two attempts to reach a light that is gone: the wait grows with every failure, up to a limit, and
    /// starts from the shortest again once a connection has stayed up for a while. The times are short (the connection takes them as settings),
    /// except the one that a connection has to stay up to count as stable, which is ten seconds.
    /// </summary>
    public class ConnectionBackoffTests
    {
        // Timers can fire a little early, and the machine a little late: this is how far from the nominal wait a measured gap may be below it.
        private static readonly TimeSpan Early = TimeSpan.FromMilliseconds(25);

        [Fact]
        public async Task TheWaitBetweenAttempts_GrowsWithEveryFailure_UpToTheLimit()
        {
            await using var server = new ScriptedServer { CloseOnAccept = true };
            var minimum = TimeSpan.FromMilliseconds(60);
            var maximum = TimeSpan.FromMilliseconds(480);
            await using var connection = Connections.To(server.Port, Connections.Fast(reconnectMin: minimum, reconnectMax: maximum));

            connection.Start();
            await Wait.UntilAsync(() => server.Accepted >= 9, "nine attempts");

            var times = server.AcceptedAt.Take(9).ToArray();
            var gaps = times.Zip(times.Skip(1), (before, after) => Stopwatch.GetElapsedTime(before, after)).ToArray();
            var expected = new[] { 60, 120, 240, 480, 480, 480, 480, 480 };
            for (var index = 0; index < gaps.Length; index++)
            {
                Assert.True(gaps[index] >= TimeSpan.FromMilliseconds(expected[index]) - Early, $"The wait before attempt {index + 2} was {gaps[index].TotalMilliseconds:0} ms: it should be at least {expected[index]} ms.");
            }

            // Not a wait that went on growing past the limit: the machine may add a little to a gap, but not what another doubling would.
            var longest = gaps.Skip(4).Max();
            Assert.True(longest < TimeSpan.FromMilliseconds(480 + 1500), $"The wait went on growing past its limit: {longest.TotalMilliseconds:0} ms.");
        }

        [Fact]
        public async Task TheWait_StartsFromTheShortestAgain_AfterAConnectionThatStayedUp()
        {
            await using var server = new ScriptedServer { CloseOnAccept = true };
            var minimum = TimeSpan.FromMilliseconds(100);
            await using var connection = Connections.To(server.Port, Connections.Fast(reconnectMin: minimum, reconnectMax: TimeSpan.FromSeconds(3)));
            connection.Start();

            // Four attempts that fail: the waits are 100, 200, 400 and 800 ms, and the next would be 1600 ms.
            await Wait.UntilAsync(() => server.Accepted >= 4, "four attempts that fail");
            server.CloseOnAccept = false;
            await Wait.UntilAsync(() => connection.IsConnected && server.Open == 1, "a connection that stays", TimeSpan.FromSeconds(10));

            // The connection stays up for longer than the time that makes it stable (ten seconds).
            await Task.Delay(TimeSpan.FromSeconds(10.5));
            Assert.True(connection.IsConnected);
            var accepted = server.Accepted;
            var dropped = Stopwatch.GetTimestamp();
            server.Drop();

            await Wait.UntilAsync(() => server.Accepted > accepted, "the next attempt", TimeSpan.FromSeconds(10));
            var wait = Stopwatch.GetElapsedTime(dropped, server.AcceptedAt[accepted]);
            Assert.True(wait < TimeSpan.FromMilliseconds(1000), $"The connection waited {wait.TotalMilliseconds:0} ms after a connection that was stable, and not the shortest wait (100 ms).");
        }

        [Fact]
        public async Task TheWait_KeepsGrowing_AfterAConnectionThatDidNotStayUp()
        {
            await using var server = new ScriptedServer { CloseOnAccept = true };
            await using var connection = Connections.To(server.Port, Connections.Fast(reconnectMin: TimeSpan.FromMilliseconds(100), reconnectMax: TimeSpan.FromSeconds(3)));
            connection.Start();
            await Wait.UntilAsync(() => server.Accepted >= 4, "four attempts that fail");
            server.CloseOnAccept = false;
            await Wait.UntilAsync(() => connection.IsConnected && server.Open == 1, "a connection that stays", TimeSpan.FromSeconds(10));
            await Task.Delay(300);
            var accepted = server.Accepted;
            var dropped = Stopwatch.GetTimestamp();

            server.Drop();

            await Wait.UntilAsync(() => server.Accepted > accepted, "the next attempt", TimeSpan.FromSeconds(10));
            var wait = Stopwatch.GetElapsedTime(dropped, server.AcceptedAt[accepted]);
            Assert.True(wait >= TimeSpan.FromMilliseconds(1600) - Early, $"The connection waited {wait.TotalMilliseconds:0} ms after a connection that did not stay up: the wait should go on growing (1600 ms).");
        }

        [Fact]
        public async Task AnAddressThatChanges_IsReachedAtOnce_WhateverTheWaitHasGrownTo()
        {
            await using var gone = new ScriptedServer { CloseOnAccept = true };
            await using var bulb = new SimulatedBulb();
            await using var connection = Connections.To(gone.Port, Connections.Fast(reconnectMin: TimeSpan.FromSeconds(5), reconnectMax: TimeSpan.FromSeconds(30)));
            connection.Start();
            await Wait.UntilAsync(() => gone.Accepted >= 1, "the first attempt");

            // The connection waits five seconds for the next attempt; a new address is not made to wait.
            var moved = Stopwatch.StartNew();
            connection.UpdateEndpoint("127.0.0.1", bulb.ControlPort);

            await Wait.UntilAsync(() => connection.IsConnected && bulb.ClientCount == 1, "the connection to the new address", TimeSpan.FromSeconds(4));
            Assert.True(moved.Elapsed < TimeSpan.FromSeconds(4));
        }
    }
}
