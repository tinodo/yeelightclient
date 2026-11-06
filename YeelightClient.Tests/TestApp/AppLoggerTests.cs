namespace Yeelight.Tests.TestApp
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The log of the test app: what a client logs, how it is read for the list, how it is filtered, and how it is exported.</summary>
    public sealed class AppLoggerTests : IDisposable
    {
        private const string Light = "0x000000000015243f";

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "yeelight-testapp-log-" + Guid.NewGuid().ToString("N"));
        private readonly BoundedBuffer<LogEntry> _store = LogEntry.NewStore();
        private readonly AppLogger _logger;

        public AppLoggerTests()
        {
            this._logger = new AppLogger(this._store);
        }

        public void Dispose()
        {
            if (Directory.Exists(this._folder))
            {
                Directory.Delete(this._folder, recursive: true);
            }
        }

        [Fact]
        public void ARequestAndItsAnswer_AreEntriesOfTheWire_ThatAreMatchedByTheirNumber()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", Light, "{\"id\":7,\"method\":\"set_bright\",\"params\":[50,\"sudden\",0]}");
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"id\":7,\"result\":[\"ok\"]}");

            var entries = this._store.Snapshot();

            Assert.Equal(2, entries.Count);
            var sent = entries[0];
            Assert.Equal(LogKind.Sent, sent.Kind);
            Assert.Equal(Light, sent.Light);
            Assert.Equal("set_bright", sent.Method);
            Assert.Equal(7, sent.RequestId);
            Assert.Equal("set_bright #7", sent.Title);
            Assert.Equal("{\"id\":7,\"method\":\"set_bright\",\"params\":[50,\"sudden\",0]}", sent.Text);
            Assert.Equal(LogLevel.Debug, sent.Level);
            Assert.Equal("\u2192", sent.Direction);
            var received = entries[1];
            Assert.Equal(LogKind.Received, received.Kind);
            Assert.Equal("set_bright", received.Method);
            Assert.Equal(7, received.RequestId);
            Assert.Equal("set_bright #7 \u2192 ok", received.Title);
            Assert.NotNull(received.ElapsedMs);
            Assert.InRange(received.ElapsedMs!.Value, 0, 5_000);
            Assert.False(received.Failed);
            Assert.False(received.IsProblem);
            Assert.Equal("\u2190", received.Direction);
        }

        [Fact]
        public void AnAnswerWithAnError_IsMarkedAsAProblem_WithItsCodeAndMessage()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", Light, "{\"id\":8,\"method\":\"set_bright\",\"params\":[50,\"sudden\",0]}");
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"id\":8,\"error\":{\"code\":-1,\"message\":\"general error\"}}");

            var failed = this._store.Snapshot()[1];

            Assert.True(failed.Failed);
            Assert.True(failed.IsProblem);
            Assert.Equal("set_bright #8 \u2192 error -1: general error", failed.Title);
        }

        [Fact]
        public void AnAnswerWithValues_ShowsThem()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", Light, "{\"id\":9,\"method\":\"get_prop\",\"params\":[\"power\",\"bright\"]}");
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"id\":9,\"result\":[\"on\",\"80\"]}");

            Assert.Equal("get_prop #9 \u2192 [\"on\",\"80\"]", this._store.Snapshot()[1].Title);
        }

        [Fact]
        public void AnAnswerThatNobodyAskedFor_IsNamedByItsId()
        {
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"id\":55,\"result\":[\"ok\"]}");

            var entry = this._store.Snapshot().Single();

            Assert.Equal("answer #55 \u2192 ok", entry.Title);
            Assert.Null(entry.Method);
            Assert.Null(entry.ElapsedMs);
        }

        [Fact]
        public void ANotificationOfTheLight_NamesTheProperties_ThatChanged()
        {
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"method\":\"props\",\"params\":{\"power\":\"off\",\"bright\":\"10\"}}");

            var entry = this._store.Snapshot().Single();

            Assert.Equal(LogKind.Received, entry.Kind);
            Assert.Equal("props", entry.Method);
            Assert.Equal("props: power, bright", entry.Title);
            Assert.Null(entry.RequestId);
        }

        [Fact]
        public void TheSameNumberOnTwoLights_IsNotMixedUp()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", "0x01", "{\"id\":1,\"method\":\"toggle\",\"params\":[]}");
            this._logger.LogDebug("{Light}: Sending: {Message}", "0x02", "{\"id\":1,\"method\":\"set_default\",\"params\":[]}");
            this._logger.LogDebug("{Light}: Received: {Message}", "0x02", "{\"id\":1,\"result\":[\"ok\"]}");
            this._logger.LogDebug("{Light}: Received: {Message}", "0x01", "{\"id\":1,\"result\":[\"ok\"]}");

            var entries = this._store.Snapshot();

            Assert.Equal("set_default #1 \u2192 ok", entries[2].Title);
            Assert.Equal("toggle #1 \u2192 ok", entries[3].Title);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("[1, 2")]
        [InlineData("42")]
        [InlineData("")]
        public void AMessageThatIsNotAMessageOfTheProtocol_IsKeptAsItCame(string text)
        {
            this._logger.LogDebug("{Light}: Received: {Message}", Light, text);

            var entry = this._store.Snapshot().Single();

            Assert.Equal(LogKind.Received, entry.Kind);
            Assert.Equal(text, entry.Text);
            Assert.Contains("not understood", entry.Title, StringComparison.Ordinal);
        }

        [Fact]
        public void AMessage_IsKeptWhole()
        {
            var long_ = "{\"id\":1,\"result\":[\"" + new string('x', 200_000) + "\"]}";

            this._logger.LogDebug("{Light}: Received: {Message}", Light, long_);

            Assert.Equal(long_, this._store.Snapshot().Single().Text);
        }

        [Fact]
        public void WhatTheClientSaysAboutItself_IsAnEntryOfTheClient_ThatKnowsItsLight()
        {
            this._logger.LogInformation("Found {Light} ({Model}) at {Location}.", Light, "color", "yeelight://127.0.0.1:55443");
            this._logger.LogInformation("{Light}: Connected to {Endpoint}.", Light, "127.0.0.1:55443");
            this._logger.LogInformation("The network addresses changed; the sockets of the discovery are made again.");

            var entries = this._store.Snapshot();

            Assert.All(entries, entry => Assert.Equal(LogKind.Client, entry.Kind));
            Assert.Equal(Light, entries[0].Light);
            Assert.Equal("Found 0x000000000015243f (color) at yeelight://127.0.0.1:55443.", entries[0].Title);
            Assert.Equal(Light, entries[1].Light);
            Assert.Equal(string.Empty, entries[2].Light);
            Assert.Equal("INF", entries[2].LevelText);
        }

        [Fact]
        public void AWarning_IsAProblem_AndAnExceptionIsPartOfTheEntry()
        {
            this._logger.LogWarning(new InvalidOperationException("Broken."), "{Light}: The state that the light reported could not be taken over.", Light);

            var entry = this._store.Snapshot().Single();

            Assert.True(entry.IsProblem);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("0x000000000015243f: The state that the light reported could not be taken over.", entry.Title);
            Assert.Contains("InvalidOperationException: Broken.", entry.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void ANoteOfTheApp_IsAnEntryOfTheClient()
        {
            this._logger.Note(LogLevel.Information, "The settings were applied.");
            this._logger.Note(LogLevel.Warning, "Something about a light.", Light);

            var entries = this._store.Snapshot();

            Assert.Equal(LogKind.Client, entries[0].Kind);
            Assert.Equal(string.Empty, entries[0].Light);
            Assert.Equal(Light, entries[1].Light);
            Assert.True(entries[1].IsProblem);
        }

        [Fact]
        public void TheLoggerAsksForEverything_ExceptNothing()
        {
            Assert.True(this._logger.IsEnabled(LogLevel.Trace));
            Assert.True(this._logger.IsEnabled(LogLevel.Debug));
            Assert.False(this._logger.IsEnabled(LogLevel.None));
            this._logger.Log(LogLevel.None, "nothing");
            Assert.Empty(this._store.Snapshot());
            Assert.Null(this._logger.BeginScope("scope"));
        }

        [Fact]
        public void ALoggerThatCannotFormat_KeepsTheReportOfThat_AndDoesNotThrow()
        {
            this._logger.Log<string>(LogLevel.Debug, default, "state", null, (_, _) => throw new FormatException("bad format"));

            var entry = this._store.Snapshot().Single();

            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Contains("bad format", entry.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EveryThread_CanLog()
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
            {
                for (var index = 0; index < 500; index++)
                {
                    this._logger.LogDebug("{Light}: Sending: {Message}", "0x0" + thread, "{\"id\":" + index + ",\"method\":\"toggle\",\"params\":[]}");
                    this._logger.LogDebug("{Light}: Received: {Message}", "0x0" + thread, "{\"id\":" + index + ",\"result\":[\"ok\"]}");
                }
            })));

            var entries = this._store.Snapshot();

            Assert.Equal(8_000, entries.Count);
            Assert.Equal(4_000, entries.Count(entry => entry.Kind == LogKind.Received && entry.Title.EndsWith("ok", StringComparison.Ordinal) && entry.Method == "toggle"));
        }

        [Fact]
        public async Task ARealClient_LogsEveryMessageItSendsAndGets_WithTheIdOfTheLight()
        {
            var bulb = new SimulatedBulb();
            var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, this._logger, searchTarget: bulb);
            try
            {
                var device = await client.ProbeAsync(IPAddress.Loopback);
                await device!.ConnectAsync();
                await device.SetBrightnessAsync(35);
                await Wait.UntilAsync(
                    () => this._store.Snapshot().Any(entry => entry.Kind == LogKind.Sent && entry.Method == "get_prop")
                        && this._store.Snapshot().Any(entry => entry.Kind == LogKind.Received && entry.Method == "props"),
                    "the read of the properties and the report of the change in the log");

                var entries = this._store.Snapshot();

                Assert.Contains(entries, entry => entry.Kind == LogKind.Client && entry.Title.StartsWith("Found ", StringComparison.Ordinal) && entry.Light == bulb.Id);
                Assert.Contains(entries, entry => entry.Kind == LogKind.Client && entry.Title.Contains("Connected to", StringComparison.Ordinal) && entry.Light == bulb.Id);
                var sent = entries.Single(entry => entry.Kind == LogKind.Sent && entry.Method == "set_bright");
                Assert.Equal(bulb.Id, sent.Light);
                Assert.Contains("\"params\":[35,\"sudden\",0]", sent.Text, StringComparison.Ordinal);
                var answer = entries.Single(entry => entry.Kind == LogKind.Received && entry.Method == "set_bright");
                Assert.Equal(sent.RequestId, answer.RequestId);
                Assert.Equal(bulb.Id, answer.Light);
                Assert.NotNull(answer.ElapsedMs);
                Assert.Contains(entries, entry => entry.Kind == LogKind.Sent && entry.Method == "get_prop");
                Assert.Contains(entries, entry => entry.Kind == LogKind.Received && entry.Method == "props" && entry.Light == bulb.Id);
            }
            finally
            {
                await client.DisposeAsync();
                await bulb.DisposeAsync();
            }
        }

        [Fact]
        public void TheFilter_ChoosesByKindLevelLightAndText()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", "0x01", "{\"id\":1,\"method\":\"toggle\",\"params\":[]}");
            this._logger.LogDebug("{Light}: Received: {Message}", "0x01", "{\"id\":1,\"result\":[\"ok\"]}");
            this._logger.LogDebug("{Light}: Sending: {Message}", "0x02", "{\"id\":1,\"method\":\"set_default\",\"params\":[]}");
            this._logger.LogInformation("{Light}: Connected to {Endpoint}.", "0x01", "127.0.0.1:1");
            this._logger.LogWarning("The discovery failed ({Reason}); trying again in {Seconds:0} seconds.", "no network", 5);
            var all = this._store.Snapshot();

            Assert.Equal(5, all.Count(LogFilter.All.Matches));
            Assert.Equal(1, all.Count(new LogFilter { ShowSent = false, ShowClient = false }.Matches));
            Assert.Equal(2, all.Count(new LogFilter { MinimumLevel = LogLevel.Information }.Matches));
            Assert.Equal(1, all.Count(new LogFilter { MinimumLevel = LogLevel.Warning }.Matches));
            Assert.Equal(3, all.Count(new LogFilter { LightId = "0x01" }.Matches));
            Assert.Equal(1, all.Count(new LogFilter { LightId = "0X02" }.Matches));
            Assert.Equal(2, all.Count(new LogFilter { Text = "TOGGLE" }.Matches));
            Assert.Equal(1, all.Count(new LogFilter { Text = "set_default" }.Matches));
            Assert.Equal(1, all.Count(new LogFilter { Text = "no network" }.Matches));
            Assert.Equal(3, all.Count(new LogFilter { Text = "0x01" }.Matches));
            Assert.Equal(0, all.Count(new LogFilter { ShowSent = false, ShowReceived = false, ShowClient = false }.Matches));
        }

        [Fact]
        public void AnEntryOfTheWire_IsExportedAsTheJsonItIs()
        {
            this._logger.LogDebug("{Light}: Sending: {Message}", Light, "{\"id\":7,\"method\":\"set_bright\",\"params\":[50,\"sudden\",0]}");
            this._logger.LogDebug("{Light}: Received: {Message}", Light, "{\"id\":7,\"error\":{\"code\":-1,\"message\":\"general error\"}}");
            this._logger.LogInformation("{Light}: Connected to {Endpoint}.", Light, "127.0.0.1:55443");

            var lines = this._store.Snapshot().Select(SessionExporter.ToLine).ToArray();

            using var sent = JsonDocument.Parse(lines[0]);
            Assert.Equal("sent", sent.RootElement.GetProperty("kind").GetString());
            Assert.Equal(Light, sent.RootElement.GetProperty("light").GetString());
            Assert.Equal("set_bright", sent.RootElement.GetProperty("method").GetString());
            Assert.Equal(7, sent.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(JsonValueKind.Object, sent.RootElement.GetProperty("frame").ValueKind);
            Assert.Equal(50, sent.RootElement.GetProperty("frame").GetProperty("params")[0].GetInt32());
            using var failed = JsonDocument.Parse(lines[1]);
            Assert.True(failed.RootElement.GetProperty("failed").GetBoolean());
            Assert.Equal(-1, failed.RootElement.GetProperty("frame").GetProperty("error").GetProperty("code").GetInt32());
            using var client = JsonDocument.Parse(lines[2]);
            Assert.Equal("client", client.RootElement.GetProperty("kind").GetString());
            Assert.Equal("127.0.0.1:55443", client.RootElement.GetProperty("text").GetString()!.Split("Connected to ")[1].TrimEnd('.'));
            Assert.All(lines, line => Assert.DoesNotContain('\n', line));
        }

        [Fact]
        public void TheLog_IsWrittenAsOneLineOfJsonForEachEntry()
        {
            for (var index = 0; index < 25; index++)
            {
                this._logger.LogDebug("{Light}: Sending: {Message}", Light, "{\"id\":" + index + ",\"method\":\"toggle\",\"params\":[]}");
            }

            var path = SessionExporter.ExportLog(this._store.Snapshot(), this._folder);

            Assert.StartsWith(this._folder, path, StringComparison.Ordinal);
            Assert.EndsWith(".jsonl", path, StringComparison.Ordinal);
            var lines = File.ReadAllLines(path);
            Assert.Equal(25, lines.Length);
            Assert.All(lines, line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("toggle", document.RootElement.GetProperty("method").GetString());
            });
        }

        [Fact]
        public void TheShortFormOfAnId_IsItsLastDigits()
        {
            Assert.Equal("15243f", LogEntry.ShortId("0x000000000015243f"));
            Assert.Equal("0x01", LogEntry.ShortId("0x01"));
            Assert.Equal(string.Empty, LogEntry.ShortId(string.Empty));
        }
    }
}
