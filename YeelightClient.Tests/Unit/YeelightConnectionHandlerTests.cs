namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The connection to a light hands what it hears to code that is not its own (the handler of the device, and the one of the
    /// application behind it). Whatever that code does, the connection goes on reading: a handler that throws is logged, and nothing else.
    /// </summary>
    public class YeelightConnectionHandlerTests
    {
        private static (LogLevel Level, string Message, Exception? Exception)[] BrokenHandlers(ListLogger logger)
        {
            return logger.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException).ToArray();
        }

        [Fact]
        public async Task AHandlerOfThePropsThatThrows_IsLogged_AndTheConnectionKeepsReading()
        {
            await using var bulb = new SimulatedBulb();
            var logger = new ListLogger();
            var connection = Connections.To(bulb.ControlPort, logger: logger);
            var calls = 0;
            connection.PropsReceived += _ =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("The handler is broken.");
            };
            connection.Start();
            await Wait.UntilAsync(() => connection.IsConnected, "the connection to be made");

            await bulb.ChangeAsync(("bright", "10"));
            await bulb.ChangeAsync(("bright", "20"));

            await Wait.UntilAsync(() => Volatile.Read(ref calls) == 2 && BrokenHandlers(logger).Length == 2, "both reports to reach the handler, and to be logged");
            var answer = await connection.SendAsync("get_prop", ["bright"]);
            Assert.True(answer.Success);
            Assert.Equal("20", answer.GetString());
            Assert.All(BrokenHandlers(logger), warning => Assert.Contains("could not be taken over", warning.Message));
            await connection.DisposeAsync();
        }

        [Fact]
        public async Task AHandlerOfTheStateOfTheConnection_ThatThrows_IsLogged_AndTheConnectionIsMadeAgainWhenItIsLost()
        {
            await using var bulb = new SimulatedBulb();
            var logger = new ListLogger();
            var connection = Connections.To(bulb.ControlPort, logger: logger);
            var states = new System.Collections.Concurrent.ConcurrentQueue<bool>();
            connection.ConnectedChanged += connected =>
            {
                states.Enqueue(connected);
                throw new InvalidOperationException("The handler is broken.");
            };
            connection.Start();
            await Wait.UntilAsync(() => connection.IsConnected, "the connection to be made");

            bulb.DropConnections();

            await Wait.UntilAsync(() => states.ToArray().SequenceEqual([true, false, true]) && BrokenHandlers(logger).Length == 3, "the connection to be lost and made again, and the handler to be logged each time");
            await Wait.UntilAsync(() => connection.IsConnected, "the connection to be there again");
            Assert.All(BrokenHandlers(logger), warning => Assert.Contains("change of the connection could not be taken over", warning.Message));
            var answer = await connection.SendAsync("get_prop", ["power"]);
            Assert.True(answer.Success);
            await connection.DisposeAsync();
        }

        [Fact]
        public async Task AHandlerOfAnAnswerThatThrows_IsLogged_AndTheCommandStillGetsItsAnswer()
        {
            await using var bulb = new SimulatedBulb();
            var logger = new ListLogger();
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort, logger: logger);

            var answer = await connection.SendAsync("get_prop", ["power"], onAnswer: _ => throw new InvalidOperationException("The handler is broken."));

            Assert.True(answer.Success);
            Assert.Equal("on", answer.GetString());
            var warning = Assert.Single(BrokenHandlers(logger));
            Assert.Contains("answer of the light could not be taken over", warning.Message);
            var next = await connection.SendAsync("get_prop", ["bright"]);
            Assert.Equal("100", next.GetString());
        }

        [Fact]
        public async Task AnAnswerHandler_IsNotCalledForAFailure_OnlyForTheAnswerOfTheLight()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort, Connections.Fast(commandTimeout: TimeSpan.FromMilliseconds(200)));
            var answers = new System.Collections.Concurrent.ConcurrentQueue<YeelightResult>();
            bulb.Delay = request => request.Method == "toggle" ? TimeSpan.FromMilliseconds(700) : TimeSpan.Zero;

            var failure = await connection.SendAsync("toggle", [], onAnswer: answers.Enqueue);
            var answer = await connection.SendAsync("get_prop", ["power"], onAnswer: answers.Enqueue);

            Assert.Equal(YeelightFailureKind.Timeout, failure.Error!.Kind);
            Assert.True(answer.Success);
            Assert.Equal([answer.Id], answers.Select(result => result.Id).ToArray());
        }

        [Fact]
        public async Task UpdateEndpoint_OfAConnectionThatIsDisposed_ChangesNothing()
        {
            await using var bulb = new SimulatedBulb();
            var connection = await Connections.ConnectedAsync(bulb.ControlPort);
            await connection.DisposeAsync();
            var before = connection.Endpoint;

            connection.UpdateEndpoint("127.0.0.1", FreePort.Tcp());

            Assert.Equal(before, connection.Endpoint);
            Assert.False(connection.IsConnected);
        }

        [Fact]
        public async Task UpdateEndpoint_ToTheSameAddressAndPort_DoesNotDisturbTheConnection()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = await Connections.ConnectedAsync(bulb.ControlPort);

            connection.UpdateEndpoint("127.0.0.1", bulb.ControlPort);

            Assert.True(connection.IsConnected);
            var answer = await connection.SendAsync("get_prop", ["power"]);
            Assert.True(answer.Success);
            Assert.Equal(1, bulb.ConnectionCount);
        }

        [Fact]
        public async Task UpdateEndpoint_ANullOrEmptyHost_IsRefused()
        {
            await using var bulb = new SimulatedBulb();
            await using var connection = Connections.To(bulb.ControlPort);

            Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => connection.UpdateEndpoint(null!, 1)).ParamName);
            Assert.Equal("host", Assert.Throws<ArgumentException>(() => connection.UpdateEndpoint(string.Empty, 1)).ParamName);
        }
    }
}
