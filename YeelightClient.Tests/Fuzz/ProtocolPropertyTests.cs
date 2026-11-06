namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;

    /// <summary>
    /// What the functions of the protocol promise for any input: reading a line of a light never throws and only accepts what is a
    /// message, a message that is written is read back as it was written, and what is sent to a light is one line of JSON, whatever the
    /// parameters hold (no parameter can end the line, or add a member to the command).
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class ProtocolPropertyTests(ITestOutputHelper output)
    {
        // A scalar of a message as JSON writes it, which a message can be made of and has to be read back unchanged.
        private static readonly Gen<string> ValidScalar = Gen.Frequency(
            (1, Gen.Constant("null")),
            (1, Gen.Elements("true", "false")),
            (3, Gen.Choose(-1000, 100000).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (2, Gen.Elements(FuzzGenerators.Numbers)),
            (5, FuzzGenerators.CleanText.Select(FuzzGenerators.Quote)));

        private static readonly Gen<object?> Parameter = Gen.Frequency(
            (4, FuzzGenerators.Text.Select(text => (object?)text)),
            (3, Gen.Choose(int.MinValue, int.MaxValue).Select(number => (object?)number)),
            (1, Gen.Elements<object?>(true, false, null)),
            (1, from high in Gen.Choose(int.MinValue, int.MaxValue) from low in Gen.Choose(int.MinValue, int.MaxValue) select (object?)(((long)high << 32) | (uint)low)),
            (1, from number in Gen.Choose(-1_000_000, 1_000_000) from divisor in Gen.Choose(1, 1000) select (object?)(number / (double)divisor)));

        [Fact]
        public void TryParse_NeverThrows_ForAnyLine_AndWhatItAcceptsIsASoundMessage()
        {
            var lines = Gen.Frequency((8, FuzzGenerators.Message()), (2, FuzzGenerators.Text), (1, Gen.Elements<string>(string.Empty, " ", "\t")));

            FuzzCheck.ForAll(
                lines,
                line =>
                {
                    if (YeelightProtocol.TryParse(FuzzGenerators.Unsolicited(line), out var message))
                    {
                        Assert.NotNull(message);
                        switch (message)
                        {
                            case YeelightResponse response:
                                Assert.NotNull(response.Result);
                                Assert.NotNull(response.Result.Values);
                                Assert.Equal(response.Result.Error is null, response.Result.Success);
                                if (response.Result.Error is { } error)
                                {
                                    Assert.NotNull(error.Message);
                                    Assert.Empty(response.Result.Values);
                                }

                                break;
                            case YeelightNotification notification:
                                Assert.NotNull(notification.Method);
                                Assert.NotNull(notification.Parameters);
                                Assert.All(notification.Parameters.Keys, key => Assert.NotNull(key));
                                break;
                            default:
                                Assert.Fail("A message that is neither an answer nor a notification: " + message.GetType().Name);
                                break;
                        }
                    }

                    return true;
                },
                seedOffset: 10);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void TryParse_NeverThrows_ForTextThatIsNotAString_OrNotJson()
        {
            Assert.False(YeelightProtocol.TryParse(null, out _));
            FuzzCheck.ForAll(
                FuzzGenerators.Text,
                text =>
                {
                    _ = YeelightProtocol.TryParse(text, out _);
                    return true;
                },
                seedOffset: 11);
        }

        [Fact]
        public void WhatEveryScalarAccessorDoes_WithAnyJsonValue_IsAnAnswerAndNeverAnException()
        {
            FuzzCheck.ForAll(
                FuzzGenerators.ValidValue(4),
                json =>
                {
                    using var document = JsonDocument.Parse(json);
                    Visit(document.RootElement);
                    return true;
                },
                seedOffset: 12);
        }

        [Fact]
        public void AnAnswer_ThatTheProtocolShows_CanAlwaysBeShown()
        {
            // The text of a result is what a person reads in a log or on a screen: it must not be a reason to throw, whatever the light sent.
            FuzzCheck.ForAll(
                FuzzGenerators.Answer(),
                line =>
                {
                    if (YeelightProtocol.TryParse(FuzzGenerators.Unsolicited(line), out var message) && message is YeelightResponse response)
                    {
                        _ = response.Result.ToString();
                        for (var index = -1; index <= response.Result.Values.Count; index++)
                        {
                            _ = response.Result.GetString(index);
                        }

                        _ = response.Result.Error?.ToString();
                        _ = response.Result.Error?.Message;
                    }

                    return true;
                },
                seedOffset: 13);
        }

        [Fact]
        public void AnAnswerThatIsWritten_IsReadAsItWasWritten()
        {
            var answer =
                from id in Gen.Frequency((5, Gen.Choose(1, 1000)), (1, Gen.Choose(1, int.MaxValue)))
                from values in Gen.ListOf(ValidScalar)
                select (Id: id, Values: values.ToArray());

            FuzzCheck.ForAll(
                answer,
                case1 =>
                {
                    var line = "{\"id\":" + case1.Id.ToString(CultureInfo.InvariantCulture) + ",\"result\":[" + string.Join(",", case1.Values) + "]}";
                    Assert.True(YeelightProtocol.TryParse(line, out var message), line);
                    var response = Assert.IsType<YeelightResponse>(message);
                    Assert.Equal(case1.Id, response.Result.Id);
                    Assert.True(response.Result.Success);
                    Assert.Equal(case1.Values, response.Result.Values.Select(value => value.GetRawText()));
                    return true;
                },
                seedOffset: 14);
        }

        [Fact]
        public void AnError_ThatIsWritten_IsReadAsItWasWritten()
        {
            var error =
                from id in Gen.Choose(1, 100000)
                from code in Gen.Choose(-100000, 100000)
                from text in FuzzGenerators.CleanText
                select (Id: id, Code: code, Text: text);

            FuzzCheck.ForAll(
                error,
                case1 =>
                {
                    var line = "{\"id\":" + case1.Id.ToString(CultureInfo.InvariantCulture) + ",\"error\":{\"code\":" + case1.Code.ToString(CultureInfo.InvariantCulture) + ",\"message\":" + FuzzGenerators.Quote(case1.Text) + "}}";
                    Assert.True(YeelightProtocol.TryParse(line, out var message), line);
                    var response = Assert.IsType<YeelightResponse>(message);
                    Assert.Equal(case1.Id, response.Result.Id);
                    Assert.False(response.Result.Success);
                    Assert.Equal(case1.Code, response.Result.Error!.Code);
                    Assert.Equal(case1.Text, response.Result.Error.Message);
                    return true;
                },
                seedOffset: 15);
        }

        [Fact]
        public void ANotification_ThatIsWritten_IsReadAsItWasWritten()
        {
            var notification =
                from method in Gen.Frequency((6, Gen.Constant("props")), (1, FuzzGenerators.CleanText))
                from members in Gen.ListOf(from key in FuzzGenerators.CleanText from value in ValidScalar select (Key: key, Value: value))
                select (Method: method, Members: members.GroupBy(member => member.Key, StringComparer.Ordinal).Select(sameKey => sameKey.First()).ToArray());

            FuzzCheck.ForAll(
                notification,
                case1 =>
                {
                    var line = "{\"method\":" + FuzzGenerators.Quote(case1.Method) + ",\"params\":{" + string.Join(",", case1.Members.Select(member => FuzzGenerators.Quote(member.Key) + ":" + member.Value)) + "}}";
                    Assert.True(YeelightProtocol.TryParse(line, out var message), line);
                    var read = Assert.IsType<YeelightNotification>(message);
                    Assert.Equal(case1.Method, read.Method);
                    Assert.Equal(case1.Members.Length, read.Parameters.Count);
                    foreach (var (key, value) in case1.Members)
                    {
                        Assert.Equal(value, read.Parameters[key].GetRawText());
                    }

                    return true;
                },
                seedOffset: 16);
        }

        [Fact]
        public void ALineWithAnId_IsAnAnswerOnlyWhenItHasAResultListOrAnErrorObject()
        {
            // The line {"id":5} (or one with a result that is not a list) is not an answer: taking it for one would report a command as done
            // that the light did not say it did.
            var shapes =
                from id in Gen.Choose(1, 1000)
                from result in Gen.Frequency((2, Gen.Constant<string?>(null)), (6, FuzzGenerators.ValidValue(2).Select(value => (string?)value)))
                from error in Gen.Frequency((2, Gen.Constant<string?>(null)), (3, Gen.Constant<string?>("null")), (5, FuzzGenerators.ValidValue(2).Select(value => (string?)value)))
                select (Id: id, Result: result, Error: error);

            FuzzCheck.ForAll(
                shapes,
                shape =>
                {
                    var members = new List<string> { "\"id\":" + shape.Id.ToString(CultureInfo.InvariantCulture) };
                    if (shape.Result is not null)
                    {
                        members.Add("\"result\":" + shape.Result);
                    }

                    if (shape.Error is not null)
                    {
                        members.Add("\"error\":" + shape.Error);
                    }

                    var hasError = shape.Error is not null && KindOf(shape.Error) != JsonValueKind.Null;
                    var expected = hasError
                        ? KindOf(shape.Error!) == JsonValueKind.Object
                        : shape.Result is not null && KindOf(shape.Result) == JsonValueKind.Array;

                    var line = "{" + string.Join(",", members) + "}";
                    Assert.Equal(expected, YeelightProtocol.TryParse(line, out _));
                    return true;
                },
                seedOffset: 17);
        }

        [Fact]
        public void ACommand_IsOneLineOfJson_WhateverItsMethodAndParametersHold()
        {
            var command =
                from id in Gen.Frequency((5, Gen.Choose(1, 1000)), (1, Gen.Choose(1, int.MaxValue)))
                from method in FuzzGenerators.Text.Where(text => text.Length > 0)
                from parameters in Gen.ListOf(Parameter)
                select (Id: id, Method: method, Parameters: parameters.ToArray());

            FuzzCheck.ForAll(
                command,
                case1 =>
                {
                    var bytes = YeelightProtocol.BuildRequest(case1.Id, case1.Method, case1.Parameters);

                    // One line: the break at the end, and no other.
                    Assert.True(bytes.Length > 2 && bytes[^2] == 13 && bytes[^1] == 10, "The command does not end with CR LF.");
                    var body = bytes.AsSpan(0, bytes.Length - 2).ToArray();
                    Assert.DoesNotContain((byte)10, body);
                    Assert.DoesNotContain((byte)13, body);

                    // Text that is valid UTF-8, and JSON of exactly the three members of a command.
                    var text = new UTF8Encoding(false, true).GetString(body);
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;
                    Assert.Equal(JsonValueKind.Object, root.ValueKind);
                    string[] names = ["id", "method", "params"];
                    Assert.Equal(names, root.EnumerateObject().Select(member => member.Name).ToArray());
                    Assert.Equal(case1.Id, root.GetProperty("id").GetInt32());
                    Assert.Equal(FuzzGenerators.ReplaceLoneSurrogates(case1.Method), root.GetProperty("method").GetString());
                    var parameters = root.GetProperty("params");
                    Assert.Equal(JsonValueKind.Array, parameters.ValueKind);
                    Assert.Equal(case1.Parameters.Length, parameters.GetArrayLength());
                    for (var index = 0; index < case1.Parameters.Length; index++)
                    {
                        Assert.True(SameValue(parameters[index], case1.Parameters[index]), $"Parameter {index} ({case1.Parameters[index]}) was written as {parameters[index].GetRawText()}.");
                    }

                    return true;
                },
                seedOffset: 18);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void ACommandWithAnAttackInAParameter_StaysACommandWithThatParameter()
        {
            string[] attacks =
            [
                "\"],\"method\":\"set_power\",\"params\":[\"on",
                "x\r\n{\"id\":99,\"method\":\"set_power\",\"params\":[\"off\"]}",
                "\n",
                "\r",
                "\u2028\u2029\u0085",
                "\\\"}\r\n",
                "\0",
                "\ud800",
                new string('"', 200),
            ];

            foreach (var attack in attacks)
            {
                var bytes = YeelightProtocol.BuildRequest(7, "set_name", [attack]);
                var line = Encoding.UTF8.GetString(bytes);

                Assert.EndsWith("\r\n", line, StringComparison.Ordinal);
                Assert.Equal(1, line.Split('\n').Length - 1);
                using var document = JsonDocument.Parse(line.TrimEnd());
                Assert.Equal(1, document.RootElement.GetProperty("params").GetArrayLength());
                Assert.Equal("set_name", document.RootElement.GetProperty("method").GetString());
            }
        }

        private static JsonValueKind KindOf(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind;
        }

        private static void Visit(JsonElement element)
        {
            _ = YeelightProtocol.ToText(element);
            _ = YeelightProtocol.TryGetString(element);
            _ = YeelightProtocol.TryGetInt32(element, out _);
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Visit(item);
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                    {
                        Visit(member.Value);
                    }

                    break;
            }
        }

        private static bool SameValue(JsonElement element, object? expected)
        {
            return expected switch
            {
                null => element.ValueKind == JsonValueKind.Null,
                string text => element.ValueKind == JsonValueKind.String && element.GetString() == FuzzGenerators.ReplaceLoneSurrogates(text),
                bool flag => element.ValueKind == (flag ? JsonValueKind.True : JsonValueKind.False),
                int number => element.ValueKind == JsonValueKind.Number && element.GetInt32() == number,
                long number => element.ValueKind == JsonValueKind.Number && element.GetInt64() == number,
                double number => element.ValueKind == JsonValueKind.Number && element.GetDouble() == number,
                _ => false,
            };
        }
    }
}
