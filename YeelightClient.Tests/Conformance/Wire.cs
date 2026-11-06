namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What the conformance tests share: a client that is connected to a simulated light and does nothing by itself (so that what the light
    /// received is what a test sent), and the comparison of a request that the light received with what the specification says.
    /// </summary>
    internal static class Wire
    {
        /// <summary>
        /// Starts a simulated light and a client with a connected device. Nothing is sent on connecting (the properties are not read, and no
        /// keep-alive is asked), the limit of commands a minute is out of the way, and the light accepts a command whatever its state is, so
        /// that the commands of the background light, which is off, are not turned down for that: this is about what is sent.
        /// </summary>
        /// <param name="bulb">What the light is made of.</param>
        /// <param name="refreshOnConnect">Whether the client reads the properties on connecting, as it does by default.</param>
        public static async Task<Harness> StartAsync(SimulatedBulbOptions? bulb = null, bool refreshOnConnect = false)
        {
            var harness = await Harness.StartAsync(
                (bulb ?? new SimulatedBulbOptions()) with { CommandsPerMinute = 60_000 },
                port => new YeelightClientOptions
                {
                    DiscoveryPort = port,
                    RefreshOnConnect = refreshOnConnect,
                    KeepAliveInterval = TimeSpan.Zero,
                    CommandsPerMinute = 60_000,
                });
            harness.Bulb.RequireOnForChanges = false;
            return harness;
        }

        /// <summary>Gets the parameters of a request as the JSON list that was sent: every one as it was written, so <c>500</c> is not <c>500.0</c>.</summary>
        public static string ParametersOf(SimulatedRequest request)
        {
            return "[" + string.Join(",", request.Parameters.Select(parameter => parameter.GetRawText())) + "]";
        }

        /// <summary>Gets the line of a command as the specification writes it: an object with the id, the method and the list of parameters, in that order.</summary>
        public static string LineOf(int id, string method, string parameters)
        {
            return "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parameters + "}";
        }

        /// <summary>Shows that a request is the method and the parameters, down to the last character of the line.</summary>
        public static void Matches(SimulatedRequest request, string method, string parameters)
        {
            Assert.Equal(method, request.Method);
            Assert.Equal(parameters, ParametersOf(request));
            Assert.Equal(LineOf(request.Id, method, parameters), request.Raw);
        }

        /// <summary>Shows that the light received one request, and that it is the method and the parameters.</summary>
        public static SimulatedRequest Sent(SimulatedBulb bulb, string method, string parameters)
        {
            var request = Assert.Single(bulb.Requests);
            Matches(request, method, parameters);
            return request;
        }

        /// <summary>
        /// Shows that a call is refused with an exception, for the argument that is wrong, and that it sent nothing: commands go over one
        /// connection in the order in which they are sent, so when the command that comes after the refused one has reached the light,
        /// whatever the refused one sent would have too.
        /// </summary>
        /// <param name="bulb">The light.</param>
        /// <param name="exceptionType">The type of the exception, exactly.</param>
        /// <param name="paramName">The argument that is wrong.</param>
        /// <param name="refused">The call that is refused.</param>
        /// <param name="next">A command that the light takes.</param>
        /// <param name="nextMethod">The method that it sends.</param>
        public static async Task RefusedAsync(SimulatedBulb bulb, Type exceptionType, string paramName, Func<Task> refused, Func<Task> next, string nextMethod)
        {
            var before = bulb.Requests.Count;

            var exception = await Record.ExceptionAsync(refused);

            Assert.NotNull(exception);
            Assert.Equal(exceptionType, exception.GetType());
            Assert.Equal(paramName, Assert.IsAssignableFrom<ArgumentException>(exception).ParamName);
            await next();
            Assert.Equal([nextMethod], bulb.Requests.Skip(before).Select(request => request.Method).ToArray());
        }

        /// <summary>
        /// Like <see cref="RefusedAsync"/>, for a session of music mode: a command there is not answered, so the light is waited for.
        /// </summary>
        public static async Task RefusedOverMusicAsync(SimulatedBulb bulb, YeelightMusicSession session, Type exceptionType, string paramName, Func<Task> refused)
        {
            var before = bulb.Requests.Count;

            var exception = await Record.ExceptionAsync(refused);

            Assert.NotNull(exception);
            Assert.Equal(exceptionType, exception.GetType());
            Assert.Equal(paramName, Assert.IsAssignableFrom<ArgumentException>(exception).ParamName);
            await session.SendCommandAsync("set_default");
            await Wait.UntilAsync(() => bulb.Requests.Skip(before).Any(request => request.Method == "set_default"), "the command after the refused one, in music mode");
            Assert.Equal([("set_default", true)], bulb.Requests.Skip(before).Select(request => (request.Method, request.OverMusic)).ToArray());
        }
    }
}
