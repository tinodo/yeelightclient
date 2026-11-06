namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;
    using Xunit.Sdk;
    using Yeelight.Tests.TestSupport;
    using static Yeelight.Tests.Fuzz.FuzzAssertions;

    /// <summary>
    /// Fuzz tests of the connection: a misbehaving server sends what a light could send but should not (messages of the wrong shape and
    /// type, answers for commands that do not exist, answers twice, text that is not UTF-8, lines without a break, lines that are far too long,
    /// messages cut off) and the client has to go on. After every case the connection answers the next command (it connects again when the case
    /// took it down), a command that was answered with what cannot be read does not hang, and nothing is thrown out of the loop that reads.
    /// </summary>
    /// <remarks>
    /// The cases are made from a fixed seed (see <see cref="FuzzSettings"/>), so a run is the same every time, and a failure says the case.
    /// The server is a <see cref="ScriptedServer"/>: no light is needed.
    /// </remarks>
    [Trait("Category", "Fuzz")]
    public class ReceiveFuzzTests(ITestOutputHelper output)
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        private static readonly ReceiveCase Clean = new([], []);

        public static TheoryData<string> RawNames
        {
            get
            {
                var names = new TheoryData<string>();
                foreach (var name in Raw.Keys)
                {
                    names.Add(name);
                }

                return names;
            }
        }

        private static Dictionary<string, byte[]> Raw { get; } = BuildRaw();

        [Fact]
        public async Task WhateverTheLightSends_TheConnectionGoesOnToAnswerTheNextCommand()
        {
            var cases = FuzzGenerators.ReceiveCase().Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 100), 8).ToList();
            await using var server = new ScriptedServer();
            var slot = new CaseSlot();
            server.Reply = request => slot.Take().AnswerFor(request.Id);
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: CommandTimeout, maxLineLength: 4096));
            int answered = 0, refused = 0, lost = 0;

            for (var index = 0; index < cases.Count; index++)
            {
                var item = cases[index];
                string Described() => $"case {index + 1} of {cases.Count} ({FuzzSettings.Description}): {item}";

                slot.Set(item);
                if (item.Unsolicited.Length > 0)
                {
                    await server.SendAsync(item.Unsolicited);
                }

                var watch = Stopwatch.StartNew();
                var result = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync($"the command to end after {Described()}", Deadline);

                if (result.Success)
                {
                    answered++;
                    ReadEveryValue(result);
                }
                else if (result.Error!.Kind == YeelightFailureKind.DeviceRejected)
                {
                    refused++;
                }
                else if (result.Error.Kind is YeelightFailureKind.ConnectionLost or YeelightFailureKind.NotConnected)
                {
                    lost++;
                }
                else
                {
                    throw new XunitException($"The command ended with {result.Error.Kind} ({result.Error.Message}) after {Described()}");
                }

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"The command took {watch.Elapsed.TotalSeconds:0.0} seconds after {Described()}");
                slot.Set(Clean);

                // Whatever the case did to the connection, it is up again (or comes up) and answers.
                await AssertAnswersAsync(connection, Described);
            }

            output.WriteLine($"{cases.Count} cases ({FuzzSettings.Description}): {answered} commands answered, {refused} refused by an answer, {lost} lost with the connection; the connection was made {server.Accepted} times.");
        }

        [Fact]
        public async Task WhateverTheAnswerHolds_ACommandThatIsNotAnsweredRightEndsAtItsTimeoutAndTheConnectionGoesOn()
        {
            // Only what cannot be read is waited for to the end, so only some cases are made.
            var count = Math.Min(FuzzSettings.Cases, 30);
            var cases = FuzzGenerators.Answer().Sample(count, new Rnd(FuzzSettings.Seed + 101), 8).ToList();
            await using var server = new ScriptedServer();
            var line = string.Empty;
            server.Reply = request => [Encoding.UTF8.GetBytes(Volatile.Read(ref line).Replace(FuzzGenerators.IdToken, request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) + "\r\n")];
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: TimeSpan.FromMilliseconds(250)));
            int read = 0, timedOut = 0;

            for (var index = 0; index < cases.Count; index++)
            {
                Volatile.Write(ref line, cases[index]);
                string Described() => $"answer {index + 1} of {cases.Count} ({FuzzSettings.Description}): {FuzzGenerators.Preview(cases[index])}";

                var watch = Stopwatch.StartNew();
                var result = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync($"the command to end after {Described()}", Deadline);

                if (result.Error is { Kind: YeelightFailureKind.Timeout })
                {
                    timedOut++;
                    Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
                }
                else
                {
                    read++;
                    Assert.True(result.Success || result.Error!.Kind == YeelightFailureKind.DeviceRejected, $"The command ended with {result.Error} after {Described()}");
                    ReadEveryValue(result);
                }

                Assert.True(connection.IsConnected || await Eventually(() => connection.IsConnected), $"The connection was lost after {Described()}");
            }

            Volatile.Write(ref line, "{\"id\":@ID@,\"result\":[\"ok\"]}");
            var final = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync("the last command");
            Assert.True(final.Success);
            output.WriteLine($"{cases.Count} answers ({FuzzSettings.Description}): {read} were taken for the answer, {timedOut} were not and the command timed out.");
        }

        [Theory]
        [MemberData(nameof(RawNames))]
        public async Task WhatIsNotAMessage_AsBytes_IsDropped_AndTheConnectionAnswersTheNextCommand(string name)
        {
            await using var server = new ScriptedServer();
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: CommandTimeout, maxLineLength: 4096));

            await server.SendAsync(Raw[name]);

            await AssertAnswersAsync(connection, () => $"the bytes {name}");
        }

        [Fact]
        public async Task AMegabyteLine_EndsTheConnection_AndTheClientConnectsAgainAndAnswers()
        {
            await using var server = new ScriptedServer();
            await using var connection = await Connections.ConnectedAsync(server.Port);
            var megabyte = Enumerable.Repeat((byte)'x', 1024 * 1024).Concat(new byte[] { 13, 10 }).ToArray();

            await server.SendAsync(megabyte);

            await Wait.UntilAsync(() => server.Accepted >= 2, "the connection to be made again after a line that is far too long");
            await AssertAnswersAsync(connection, () => "a megabyte line");
        }

        [Fact]
        public async Task AMegabyteWithoutALineBreak_EndsTheConnection_AndTheClientConnectsAgainAndAnswers()
        {
            await using var server = new ScriptedServer();
            await using var connection = await Connections.ConnectedAsync(server.Port);

            await server.SendAsync(Enumerable.Repeat((byte)'y', 1024 * 1024).ToArray());

            await Wait.UntilAsync(() => server.Accepted >= 2, "the connection to be made again");
            await AssertAnswersAsync(connection, () => "a megabyte without a line break");
        }

        [Fact]
        public async Task AnAnswerCutOffAtEveryByte_IsNotTakenForTheAnswer_AndTheRightOneThatFollowsIs()
        {
            const string answer = "{\"id\":@ID@,\"result\":[\"on\",\"100\",\"4000\"],\"extra\":{\"a\":[1,2,{\"b\":null}]}}";
            var cut = 0;
            await using var server = new ScriptedServer();
            server.Reply = request => [Encoding.UTF8.GetBytes(answer[..cut].Replace(FuzzGenerators.IdToken, request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) + "\r\n"), ScriptedServer.Ok(request.Id)];
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: CommandTimeout));

            for (cut = 0; cut <= answer.Length; cut++)
            {
                var result = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync($"the command with the answer cut off after {cut} characters");

                Assert.True(result.Success, $"The command failed with {result.Error} when the answer was cut off after {cut} characters.");
                if (cut < answer.IndexOf("}", StringComparison.Ordinal) + 1)
                {
                    Assert.Equal("ok", result.GetString(0));
                }
            }

            Assert.Equal(1, server.Accepted);
        }

        [Fact]
        public async Task AMessageCutOffAndThenTheConnectionClosed_IsNotAMessage_AndTheClientConnectsAgain()
        {
            const string message = "{\"method\":\"props\",\"params\":{\"power\":\"on\",\"bright\":\"42\"}}";
            await using var server = new ScriptedServer();
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: CommandTimeout));
            var reported = new HitCounter();
            connection.PropsReceived += _ => reported.Increment();

            foreach (var cut in new[] { 0, 1, 2, 10, 20, message.Length - 2, message.Length - 1 })
            {
                var accepted = server.Accepted;
                await server.SendAsync(Encoding.UTF8.GetBytes(message[..cut]));
                server.Drop();

                await Wait.UntilAsync(() => server.Accepted > accepted && connection.IsConnected, $"the connection to be made again after a message cut off after {cut} characters");
                await AssertAnswersAsync(connection, () => $"a message cut off after {cut} characters");
            }

            Assert.Equal(0, reported.Value);
        }

        [Fact]
        public async Task WhateverTheLightReports_TheStateOfTheLightStaysSound_AndTheLightGoesOn()
        {
            var frames = FuzzGenerators.Notification().Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 102), 8).ToList();
            await using var run = await DeviceRun.StartAsync();

            for (var index = 0; index < frames.Count; index++)
            {
                var frame = FuzzGenerators.Unsolicited(frames[index]);
                string Described() => $"notification {index + 1} of {frames.Count} ({FuzzSettings.Description}): {FuzzGenerators.Preview(frame)}";

                await run.Server.SendAsync(Encoding.UTF8.GetBytes(frame + "\r\n"));
                await run.ConfirmAsync(Described);
                AssertSound(run.Device, Described);
                AssertSound(run.Device.Background, Described);
            }

            await run.AssertEventsAreOfPropertiesAsync();
            output.WriteLine($"{frames.Count} notifications ({FuzzSettings.Description}); {run.Names.Count} changes were reported.");
        }

        [Fact]
        public async Task WhateverTheLightAnswersToGetProp_TheStateIsRefreshedOrTheCallEndsAtOnce()
        {
            var answers = Gen.ListOf(FuzzGenerators.ValidPropertyValue).Select(values => values.Take(30).ToArray())
                .Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 103), 20).ToList();
            await using var run = await DeviceRun.StartAsync();
            var current = string.Empty;
            run.Server.Reply = request => [Encoding.UTF8.GetBytes("{\"id\":" + request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"result\":[" + Volatile.Read(ref current) + "]}\r\n")];

            for (var index = 0; index < answers.Count; index++)
            {
                Volatile.Write(ref current, string.Join(",", answers[index]));
                string Described() => $"answer {index + 1} of {answers.Count} ({FuzzSettings.Description}) to get_prop: [{FuzzGenerators.Preview(Volatile.Read(ref current))}]";

                await run.Device.RefreshAsync().WithTimeoutAsync($"the refresh to end after {Described()}", Deadline);

                AssertSound(run.Device, Described);
                AssertSound(run.Device.Background, Described);
            }

            await run.AssertEventsAreOfPropertiesAsync();
        }

        [Fact]
        public async Task EveryPrefixOfAMessage_ChangesNothing_AndTheWholeMessageDoes()
        {
            const string message = "{\"method\":\"props\",\"params\":{\"power\":\"on\",\"bright\":\"42\",\"bg_power\":\"on\",\"name\":\"fuzzed\"}}";
            await using var run = await DeviceRun.StartAsync();
            var name = run.Device.Name;

            for (var cut = 0; cut < message.Length; cut++)
            {
                await run.Server.SendAsync(Encoding.UTF8.GetBytes(message[..cut] + "\r\n"));
                await run.ConfirmAsync(() => $"a message cut off after {cut} characters");

                Assert.False(run.Device.IsOn, $"The light is on after a message cut off after {cut} characters.");
                Assert.Equal(0, run.Device.Brightness);
                Assert.False(run.Device.Background.IsOn);
                Assert.Equal(name, run.Device.Name);
            }

            await run.Server.SendAsync(Encoding.UTF8.GetBytes(message + "\r\n"));
            await run.ConfirmAsync(() => "the whole message");

            Assert.True(run.Device.IsOn);
            Assert.Equal(42, run.Device.Brightness);
            Assert.True(run.Device.Background.IsOn);
            Assert.Equal("fuzzed", run.Device.Name);
        }

        [Fact]
        public async Task AResult_WithAStringThatIsNotText_CanBeShown_WhenItComesFromTheLight()
        {
            await using var server = new ScriptedServer();
            server.Reply = request => [ScriptedServer.Line("{\"id\":" + request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"result\":[\"\\ud800\",\"ok\"]}")];
            await using var connection = await Connections.ConnectedAsync(server.Port);

            var result = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync("the answer");

            Assert.True(result.Success);
            Assert.NotEmpty(result.ToString());
            Assert.Null(result.GetString(0));
            Assert.Equal("ok", result.GetString(1));
        }

        [Fact]
        public async Task AnError_WhoseMessageIsNotText_StillFailsItsCommandAtOnce_WithTheCodeOfTheLight()
        {
            await using var server = new ScriptedServer();
            server.Reply = request => [ScriptedServer.Line("{\"id\":" + request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"error\":{\"code\":-1,\"message\":\"\\ud800 refused\"}}")];
            await using var connection = await Connections.ConnectedAsync(server.Port, Connections.Fast(commandTimeout: TimeSpan.FromSeconds(3)));
            var watch = Stopwatch.StartNew();

            var result = await connection.SendAsync("set_power", ["on"]).WithTimeoutAsync("the answer");

            Assert.False(result.Success);
            Assert.Equal(YeelightFailureKind.DeviceRejected, result.Error!.Kind);
            Assert.Equal(-1, result.Error.Code);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"The refusal of the light was taken {watch.Elapsed.TotalSeconds:0.0} seconds after the command: an answer that the client cannot read is waited for until the timeout.");
        }

        [Theory]
        [InlineData(-100, YeelightFailureKind.NotConnected)]
        [InlineData(-101, YeelightFailureKind.Timeout)]
        [InlineData(-102, YeelightFailureKind.RateLimited)]
        [InlineData(-103, YeelightFailureKind.ConnectionLost)]
        public async Task ARefusalOfTheLight_IsARefusalOfTheLight_WhateverItsCodeIs(int code, YeelightFailureKind notThis)
        {
            // The codes from -100 down are the client's own, but a light chooses its own, and says it in an answer: the command was refused.
            await using var server = new ScriptedServer();
            server.Reply = request => [ScriptedServer.Line("{\"id\":" + request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"error\":{\"code\":" + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"message\":\"no\"}}")];
            await using var connection = await Connections.ConnectedAsync(server.Port);

            var result = await connection.SendAsync("set_power", ["on"]).WithTimeoutAsync("the answer");

            Assert.False(result.Success);
            Assert.NotEqual(notThis, result.Error!.Kind);
            Assert.Equal(YeelightFailureKind.DeviceRejected, result.Error.Kind);
            Assert.False(result.Error.IsTransportFailure, "The light answered: the way to it did not fail.");
        }

        private static Dictionary<string, byte[]> BuildRaw()
        {
            var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["a line of invalid UTF-8"] = [0xFF, 0xFE, 0xFD, 0xC0, 0x80, 0xED, 0xA0, 0x80, 13, 10],
                ["valid JSON with invalid UTF-8 in a string"] = [.. "{\"method\":\"props\",\"params\":{\"name\":\""u8, 0xFF, 0xFE, .. "\"}}\r\n"u8],
                ["only NULs"] = [.. new byte[100], 13, 10],
                ["a NUL in the middle of a message"] = [.. "{\"method\":\"props\",\"params\":{\"bright\":\"4"u8, 0, .. "2\"}}\r\n"u8],
                ["a byte order mark and a message"] = [0xEF, 0xBB, 0xBF, .. "{\"method\":\"props\",\"params\":{\"bright\":\"42\"}}\r\n"u8],
                ["only carriage returns"] = [13, 13, 13, 13, 13, 13, 10],
                ["only line feeds"] = [10, 10, 10, 10, 10, 10, 10, 10],
                ["a lone carriage return"] = [13],
                ["no line break at all, and then one"] = [.. "{\"method\":\"props\",\"params\":{\"bright\":\"1\"}}"u8, .. "{\"method\":\"props\",\"params\":{\"bright\":\"2\"}}\r\n"u8],
                ["two messages in one line"] = [.. "{\"id\":1,\"result\":[\"ok\"]}{\"id\":2,\"result\":[\"ok\"]}\r\n"u8],
                ["3000 messages in one write"] = [.. Enumerable.Repeat("{\"method\":\"props\",\"params\":{\"bright\":\"7\"}}\r\n", 3000).SelectMany(line => Encoding.ASCII.GetBytes(line))],
                ["3000 empty lines"] = [.. Enumerable.Repeat((byte)10, 3000)],
                ["a line of exactly the limit and one more"] = [.. Enumerable.Repeat((byte)'a', 4096), 13, 10, .. Enumerable.Repeat((byte)'b', 4097), 13, 10],
                ["deeply nested JSON"] = [.. Encoding.ASCII.GetBytes(new string('[', 2000) + new string(']', 2000) + "\r\n")],
                ["a huge number"] = [.. Encoding.ASCII.GetBytes("{\"id\":" + new string('9', 3000) + ",\"result\":[\"ok\"]}\r\n")],
                ["a huge exponent"] = [.. "{\"id\":1e999999999,\"result\":[\"ok\"]}\r\n"u8],
                ["a string with every escape"] = [.. "{\"method\":\"props\",\"params\":{\"name\":\"\\u0000\\b\\f\\n\\r\\t\\\"\\\\\\/\\ud83d\\ude00\\ud83d\"}}\r\n"u8],
                ["a notification of an unknown method"] = [.. "{\"method\":\"nothing_like_it\",\"params\":[1,2,3]}\r\n"u8],
                ["an HTTP request"] = [.. "GET / HTTP/1.1\r\nHost: light\r\n\r\n"u8],
                ["a TLS hello"] = [0x16, 0x03, 0x01, 0x02, 0x00, 0x01, 0x00, 0x01, 0xFC, 0x03, 0x03, 13, 10],
            };
            return raw;
        }

        private static void ReadEveryValue(YeelightResult result)
        {
            for (var index = -1; index <= result.Values.Count; index++)
            {
                _ = result.GetString(index);
            }
        }

        private static async Task<bool> Eventually(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.Elapsed < Deadline)
            {
                await Task.Delay(10);
            }

            return condition();
        }

        // The check after each case: a command that is answered the right way is answered. The command is asked again while the connection is
        // being made again, which is what a case that took it down leaves to do.
        private static async Task AssertAnswersAsync(YeelightConnection connection, Func<string> described)
        {
            var watch = Stopwatch.StartNew();
            YeelightResult result;
            do
            {
                result = await connection.SendAsync("get_prop", ["power"]).WithTimeoutAsync($"the command after {described()}", Deadline);
                if (result.Success)
                {
                    Assert.Equal("ok", result.GetString(0));
                    return;
                }

                await Task.Delay(20);
            }
            while (result.Error is { Kind: YeelightFailureKind.NotConnected or YeelightFailureKind.ConnectionLost } && watch.Elapsed < Deadline);

            throw new XunitException($"The client did not go on after {described()}: the command ended with {result.Error}.");
        }

        // What waits for a command and is changed from its slot: the case that the next command is answered with, once.
        private sealed class CaseSlot
        {
            private ReceiveCase _current = Clean;

            public void Set(ReceiveCase item) => Volatile.Write(ref this._current, item);

            public ReceiveCase Take() => Interlocked.Exchange(ref this._current, Clean);
        }

        // A client with a light that is a scripted server, which has connected and has not asked for the state (it says nothing about the light).
        private sealed class DeviceRun : IAsyncDisposable
        {
            private readonly EventRecorder<string> _names = new();

            private DeviceRun(ScriptedServer server, YeelightClient client, YeelightDevice device)
            {
                this.Server = server;
                this.Client = client;
                this.Device = device;
                device.PropertyChanged += (_, e) => this._names.Add("device:" + e.PropertyName);
                device.Background.PropertyChanged += (_, e) => this._names.Add("background:" + e.PropertyName);
            }

            public ScriptedServer Server { get; }

            public YeelightClient Client { get; }

            public YeelightDevice Device { get; }

            public IReadOnlyList<string> Names => this._names.Items;

            public static async Task<DeviceRun> StartAsync()
            {
                var server = new ScriptedServer();

                // The client keeps to what a light accepts (a command a second): the cases are made faster than that.
                var client = TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false, CommandTimeout = CommandTimeout, CommandsPerMinute = 6_000_000 });
                var device = client.Apply(Announcements.Of("0xFUZZ", Announcements.LocationOf(server.Port))) ?? throw new XunitException("The client did not take the light.");
                await Wait.UntilAsync(() => device.IsConnected, "the light to be connected");
                return new DeviceRun(server, client, device);
            }

            /// <summary>A command that is answered: everything that the server sent before it has been read when the answer is.</summary>
            public async Task ConfirmAsync(Func<string> described)
            {
                var watch = Stopwatch.StartNew();
                YeelightResult result;
                do
                {
                    result = await this.Device.SendCommandAsync("toggle", []).WithTimeoutAsync($"the command after {described()}", Deadline);
                    if (result.Success)
                    {
                        return;
                    }

                    await Task.Delay(20);
                }
                while (result.Error is { Kind: YeelightFailureKind.NotConnected or YeelightFailureKind.ConnectionLost } && watch.Elapsed < Deadline);

                throw new XunitException($"The light did not go on after {described()}: the command ended with {result.Error}.");
            }

            /// <summary>Every change that was reported is of a property of the light that there is.</summary>
            public async Task AssertEventsAreOfPropertiesAsync()
            {
                // The events are raised one at a time on a task of their own: give the last ones a moment.
                await Task.Delay(300);
                var device = typeof(YeelightDevice).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                var background = typeof(YeelightBackgroundLight).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var name in this._names.Items)
                {
                    var known = name.StartsWith("device:", StringComparison.Ordinal) ? device.Contains(name["device:".Length..]) : background.Contains(name["background:".Length..]);
                    Assert.True(known, $"{name} was reported as changed, and is not a property.");
                }
            }

            public async ValueTask DisposeAsync()
            {
                await this.Client.DisposeAsync();
                await this.Server.DisposeAsync();
            }
        }
    }
}
