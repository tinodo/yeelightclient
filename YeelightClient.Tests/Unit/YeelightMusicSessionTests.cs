namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.Conformance;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The session of music mode, where it is not the light that is looked at.
    /// </summary>
    public class YeelightMusicSessionTests
    {
        private static object Field(YeelightMusicSession session, string name)
        {
            var field = typeof(YeelightMusicSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"YeelightMusicSession has no field '{name}' any more: update this test.");
            return field.GetValue(session)!;
        }

        [Fact]
        public async Task WhenTheLightClosesTheMusicConnection_TheSessionHasClosedItsOwnEndOfIt_ByTheTimeItIsOver()
        {
            await using var h = await Wire.StartAsync();
            var session = await h.Device.StartMusicModeAsync();
            var client = (TcpClient)Field(session, "_client");
            var lifetime = (CancellationTokenSource)Field(session, "_lifetime");

            // The light leaves music mode, and closes the connection that it made.
            await h.Device.SendCommandAsync("set_music", [0]);

            await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<ObjectDisposedException>(() => client.GetStream());
            Assert.True(lifetime.IsCancellationRequested, "The watcher of the session should have been stopped.");
            Assert.False(session.IsConnected);
            await session.DisposeAsync();
        }

        [Fact]
        public async Task ASessionThatTheApplicationDisposes_HasClosedItsOwnEndOfTheConnection_WhenDisposeAsyncReturns()
        {
            await using var h = await Wire.StartAsync();
            var session = await h.Device.StartMusicModeAsync();
            var client = (TcpClient)Field(session, "_client");

            await session.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => client.GetStream());
            Assert.True(session.Completion.IsCompleted);
        }
        [Fact]
        public async Task TheNumbersOfTheCommands_StartAgainAfterTheLargestOne_AndAreNeverTheSameOneTwice()
        {
            await using var h = await Wire.StartAsync();
            await using var session = await h.Device.StartMusicModeAsync();
            var counter = typeof(YeelightMusicSession).GetField("_nextId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("YeelightMusicSession has no field '_nextId' any more: update this test.");
            counter.SetValue(session, int.MaxValue - 1);

            for (var index = 0; index < 5; index++)
            {
                await session.ToggleAsync();
            }

            await Wait.UntilAsync(() => h.Bulb.Requests.Count(request => request.OverMusic) == 5, "5 commands over the music connection");
            var ids = h.Bulb.Requests.Where(request => request.OverMusic).Select(request => request.Id).ToArray();
            Assert.Equal([int.MaxValue, 1, 2, 3, 4], ids);
        }
    }
}
