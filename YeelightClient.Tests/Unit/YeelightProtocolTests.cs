namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Xunit;

    /// <summary>
    /// The messages of the control protocol (section 4 of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the line that a command is made into, and the lines that a
    /// light sends back, which are read as defensively as anything that comes in over a network.
    /// </summary>
    public class YeelightProtocolTests
    {
        private static string Build(int id, string method, params object?[]? parameters)
        {
            return Encoding.UTF8.GetString(YeelightProtocol.BuildRequest(id, method, parameters));
        }

        private static YeelightResponse Response(string line)
        {
            Assert.True(YeelightProtocol.TryParse(line, out var message), "The line should be read as a message: " + line);
            return Assert.IsType<YeelightResponse>(message);
        }

        private static YeelightNotification Notification(string line)
        {
            Assert.True(YeelightProtocol.TryParse(line, out var message), "The line should be read as a message: " + line);
            return Assert.IsType<YeelightNotification>(message);
        }

        private static JsonElement Element(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        [Fact]
        public void BuildRequest_TheExampleOfTheSpecification_IsTheExactLine()
        {
            var bytes = YeelightProtocol.BuildRequest(1, "set_power", ["on", "smooth", 500]);

            Assert.Equal("{\"id\":1,\"method\":\"set_power\",\"params\":[\"on\",\"smooth\",500]}\r\n", Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void BuildRequest_EndsWithOneLineBreakAndHasNoOther()
        {
            var text = Build(1, "set_name", "two\r\nlines\nand\rmore");

            Assert.EndsWith("\r\n", text);
            Assert.Equal(1, text.Count(c => c == '\n'));
            Assert.Equal(1, text.Count(c => c == '\r'));
        }

        [Fact]
        public void BuildRequest_WithoutParameters_SendsAnEmptyList()
        {
            Assert.Equal("{\"id\":7,\"method\":\"toggle\",\"params\":[]}\r\n", Encoding.UTF8.GetString(YeelightProtocol.BuildRequest(7, "toggle", null)));
            Assert.Equal("{\"id\":7,\"method\":\"toggle\",\"params\":[]}\r\n", Encoding.UTF8.GetString(YeelightProtocol.BuildRequest(7, "toggle", [])));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-5)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void BuildRequest_TheIdIsANumber(int id)
        {
            Assert.Equal($"{{\"id\":{id},\"method\":\"toggle\",\"params\":[]}}\r\n", Build(id, "toggle"));
        }

        [Theory]
        [InlineData("on", "\"on\"")]
        [InlineData("", "\"\"")]
        [InlineData(5, "5")]
        [InlineData(-5, "-5")]
        [InlineData(0, "0")]
        [InlineData(int.MaxValue, "2147483647")]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        [InlineData(null, "null")]
        [InlineData(5L, "5")]
        [InlineData(long.MaxValue, "9223372036854775807")]
        [InlineData(long.MinValue, "-9223372036854775808")]
        [InlineData(1.5, "1.5")]
        [InlineData(2.0, "2")]
        [InlineData(PowerOnMode.Hsv, "3")]
        [InlineData(ColorFlowAction.TurnOff, "2")]
        [InlineData(ColorMode.ColorTemperature, "2")]
        [InlineData(ColorFlowMode.Sleep, "7")]
        public void BuildRequest_AParameter_IsWrittenAsTheJsonValueItStandsFor(object? parameter, string json)
        {
            Assert.Equal($"{{\"id\":1,\"method\":\"x\",\"params\":[{json}]}}\r\n", Build(1, "x", parameter));
        }

        [Fact]
        public void BuildRequest_SeveralParameters_AreInOrder()
        {
            Assert.Equal("{\"id\":9,\"method\":\"set_hsv\",\"params\":[255,45,\"smooth\",500,true,null]}\r\n", Build(9, "set_hsv", 255, 45, "smooth", 500, true, null));
        }

        [Theory]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData("a\\b", "\"a\\\\b\"")]
        [InlineData("line\nbreak", "\"line\\nbreak\"")]
        [InlineData("cr\rhere", "\"cr\\rhere\"")]
        [InlineData("tab\there", "\"tab\\there\"")]
        [InlineData("\b\f", "\"\\b\\f\"")]
        [InlineData("\u0001\u001f", "\"\\u0001\\u001F\"")]
        [InlineData("\u0000", "\"\\u0000\"")]
        public void BuildRequest_QuotesBackslashesAndControlCharacters_AreEscaped(string text, string json)
        {
            Assert.Equal($"{{\"id\":1,\"method\":\"set_name\",\"params\":[{json}]}}\r\n", Build(1, "set_name", text));
        }

        [Theory]
        [InlineData("a+b")]
        [InlineData("<b>&amp;")]
        [InlineData("it's")]
        [InlineData("a=b?c/d")]
        [InlineData("é")]
        [InlineData("日本語")]
        [InlineData("Ünïcödé")]
        public void BuildRequest_PlusAngleBracketsAmpersandApostropheAndLettersOfAnyLanguage_AreNotEscaped(string text)
        {
            // The relaxed escaping: nothing that a person types into a name is turned into a \u sequence on the way.
            var text2 = Build(1, "set_name", text);

            Assert.Contains($"\"{text}\"", text2);
            Assert.DoesNotContain("\\u", text2);
        }

        [Fact]
        public void BuildRequest_NonAsciiText_IsSentAsUtf8()
        {
            var bytes = YeelightProtocol.BuildRequest(1, "set_name", ["é日"]);

            Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xC3, 0xA9, 0xE6, 0x97, 0xA5 }) > 0);
        }

        [Fact]
        public void BuildRequest_ACharacterOutsideTheBasicMultilingualPlane_SurvivesTheRoundTrip()
        {
            const string bulb = "\U0001F4A1 bulb";

            using var document = JsonDocument.Parse(YeelightProtocol.BuildRequest(1, "set_name", [bulb]));

            Assert.Equal(bulb, document.RootElement.GetProperty("params")[0].GetString());
        }

        [Fact]
        public void BuildRequest_AnyText_IsValidJsonThatReadsBackTheSame()
        {
            var random = new Random(20261106);
            for (var round = 0; round < 300; round++)
            {
                var text = new StringBuilder();
                var length = random.Next(0, 40);
                for (var index = 0; index < length; index++)
                {
                    // Every character of the basic plane, but the surrogates (half of a pair is not text).
                    var c = random.Next(0, 0xFFFE);
                    text.Append(c is >= 0xD800 and <= 0xDFFF ? 'x' : (char)c);
                }

                using var document = JsonDocument.Parse(YeelightProtocol.BuildRequest(round, "set_name", [text.ToString()]));

                Assert.Equal(round, document.RootElement.GetProperty("id").GetInt32());
                Assert.Equal(text.ToString(), document.RootElement.GetProperty("params")[0].GetString());
            }
        }

        [Fact]
        public void BuildRequest_AnEmptyMethod_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentException>(() => YeelightProtocol.BuildRequest(1, string.Empty, []));

            Assert.Equal("method", refusal.ParamName);
        }

        [Fact]
        public void BuildRequest_ANullMethod_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => YeelightProtocol.BuildRequest(1, null!, []));

            Assert.Equal("method", refusal.ParamName);
        }

        [Fact]
        public void BuildRequest_AParameterThatIsNotANumberTextOrTruthValue_IsRefused()
        {
            object[] others = [new object(), new[] { 1, 2 }, Guid.Empty, DateTime.UnixEpoch, TimeSpan.Zero, new List<int>(), Element("1")];

            Assert.All(others, other => Assert.Throws<ArgumentException>(() => YeelightProtocol.BuildRequest(1, "x", [other])));
        }

        [Fact]
        public void BuildRequest_ANumberThatJsonCannotHold_IsRefused()
        {
            Assert.All(
                new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, float.NaN, float.PositiveInfinity, float.NegativeInfinity, Half.NaN, Half.PositiveInfinity, Half.NegativeInfinity },
                number => Assert.Throws<ArgumentException>(() => YeelightProtocol.BuildRequest(1, "x", [number])));
        }

        [Fact]
        public void BuildRequest_EveryNumericTypeOfTheFramework_IsWrittenAsANumber()
        {
            // SendCommandAsync documents its parameters as "numbers, text and truth values", and refuses what is "not a number".
            (object Number, string Json)[] numbers =
            [
                ((short)5, "5"), ((short)-5, "-5"), ((byte)5, "5"), ((sbyte)-5, "-5"), ((ushort)5, "5"), (5u, "5"), (5UL, "5"),
                (ulong.MaxValue, "18446744073709551615"), (1.5f, "1.5"), (1.5m, "1.5"),
                ((Half)1.5, "1.5"), ((Half)(-0.5), "-0.5"), (Half.MaxValue, "65504"),
                ((nint)5, "5"), ((nint)(-5), "-5"), ((nint)int.MaxValue, "2147483647"), ((nuint)5, "5"), ((nuint)uint.MaxValue, "4294967295"),
                ((Int128)5, "5"), (Int128.MaxValue, "170141183460469231731687303715884105727"), (Int128.MinValue, "-170141183460469231731687303715884105728"),
                ((UInt128)5, "5"), (UInt128.MaxValue, "340282366920938463463374607431768211455"),
            ];
            var problems = new List<string>();
            foreach (var (number, json) in numbers)
            {
                try
                {
                    var written = Build(1, "x", number);
                    if (written != $"{{\"id\":1,\"method\":\"x\",\"params\":[{json}]}}\r\n")
                    {
                        problems.Add($"{number.GetType().Name} {number}: wrote {written.Trim()}");
                    }
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{number.GetType().Name} {number}: refused ({ex.Message})");
                }
            }

            Assert.True(problems.Count == 0, "Numbers that were not written as numbers:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        [Theory]
        [InlineData('a', "\"a\"")]
        [InlineData('"', "\"\\\"\"")]
        [InlineData('\\', "\"\\\\\"")]
        [InlineData('\n', "\"\\n\"")]
        [InlineData('+', "\"+\"")]
        [InlineData('é', "\"é\"")]
        [InlineData('日', "\"日\"")]
        public void BuildRequest_ACharacter_IsWrittenAsTextOfOneCharacter(char letter, string json)
        {
            Assert.Equal($"{{\"id\":1,\"method\":\"x\",\"params\":[{json}]}}\r\n", Build(1, "x", letter));
        }

        [Fact]
        public void BuildRequest_AnEnumOfAnyUnderlyingType_IsWrittenAsItsNumber()
        {
            (object Choice, string Json)[] choices =
            [
                (ByteChoice.Big, "200"), (SByteChoice.Small, "-5"), (ShortChoice.Small, "-300"), (UShortChoice.Big, "60000"),
                (IntChoice.Small, "-2000000000"), (UIntChoice.Big, "4000000000"), (LongChoice.Small, "-9223372036854775808"),
                (ULongChoice.Big, "18446744073709551615"), (LongChoice.Big, "9223372036854775807"),
            ];
            var problems = new List<string>();
            foreach (var (choice, json) in choices)
            {
                try
                {
                    var written = Build(1, "x", choice);
                    if (written != $"{{\"id\":1,\"method\":\"x\",\"params\":[{json}]}}\r\n")
                    {
                        problems.Add($"{choice.GetType().Name}.{choice}: wrote {written.Trim()}");
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or OverflowException)
                {
                    problems.Add($"{choice.GetType().Name}.{choice}: {ex.GetType().Name} ({ex.Message})");
                }
            }

            Assert.True(problems.Count == 0, "Enums that were not written as their number:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        private enum ByteChoice : byte
        {
            Big = 200,
        }

        private enum SByteChoice : sbyte
        {
            Small = -5,
        }

        private enum ShortChoice : short
        {
            Small = -300,
        }

        private enum UShortChoice : ushort
        {
            Big = 60000,
        }

        private enum IntChoice
        {
            Small = -2000000000,
        }

        private enum UIntChoice : uint
        {
            Big = 4000000000,
        }

        private enum LongChoice : long
        {
            Small = long.MinValue,
            Big = long.MaxValue,
        }

        private enum ULongChoice : ulong
        {
            Big = ulong.MaxValue,
        }

        [Fact]
        public void TryParse_ASuccessfulResult_IsAResponseThatSucceeded()
        {
            var response = Response("{\"id\":1, \"result\":[\"ok\"]}");

            Assert.Equal(1, response.Result.Id);
            Assert.True(response.Result.Success);
            Assert.Null(response.Result.Error);
            Assert.Equal("ok", Assert.Single(response.Result.Values).GetString());
        }

        [Fact]
        public void TryParse_TheValuesOfGetProp_AreInOrderAndAnUnknownPropertyIsEmpty()
        {
            var response = Response("{\"id\":1, \"result\":[\"on\", \"\", \"100\"]}");

            Assert.Equal(["on", string.Empty, "100"], response.Result.Values.Select(value => value.GetString()));
        }

        [Fact]
        public void TryParse_AnEmptyResult_IsASuccessWithoutValues()
        {
            // The answer of a light that has no timers to cron_get.
            var response = Response("{\"id\":2,\"result\":[]}");

            Assert.True(response.Result.Success);
            Assert.Empty(response.Result.Values);
        }

        [Fact]
        public void TryParse_ACronResult_KeepsTheObject()
        {
            var response = Response("{\"id\":1, \"result\":[{\"type\": 0, \"delay\": 15, \"mix\": 0}]}");

            var timer = Assert.Single(response.Result.Values);
            Assert.Equal(JsonValueKind.Object, timer.ValueKind);
            Assert.Equal(15, timer.GetProperty("delay").GetInt32());
        }

        [Fact]
        public void TryParse_TheValuesOfAResponse_OutliveTheLineTheyWereReadFrom()
        {
            var response = Response("{\"id\":1,\"result\":[\"a\",{\"b\":[1,2]}]}");
            GC.Collect();

            Assert.Equal("a", response.Result.Values[0].GetString());
            Assert.Equal(2, response.Result.Values[1].GetProperty("b")[1].GetInt32());
        }

        [Fact]
        public void TryParse_AnError_IsAFailureWithItsCodeAndMessage()
        {
            var response = Response("{\"id\":2, \"error\":{\"code\":-1, \"message\":\"unsupported method\"}}");

            Assert.Equal(2, response.Result.Id);
            Assert.False(response.Result.Success);
            Assert.Equal(new YeelightError(-1, "unsupported method"), response.Result.Error);
            Assert.Empty(response.Result.Values);
        }

        [Theory]
        [InlineData("{\"id\":2,\"error\":{\"message\":\"x\"}}", -1, "x")]
        [InlineData("{\"id\":2,\"error\":{\"code\":-5}}", -5, "")]
        [InlineData("{\"id\":2,\"error\":{}}", -1, "")]
        [InlineData("{\"id\":2,\"error\":{\"code\":\"-5\",\"message\":\"y\"}}", -5, "y")]
        [InlineData("{\"id\":2,\"error\":{\"code\":\"five\",\"message\":\"y\"}}", -1, "y")]
        [InlineData("{\"id\":2,\"error\":{\"code\":1.5,\"message\":\"y\"}}", -1, "y")]
        [InlineData("{\"id\":2,\"error\":{\"code\":null,\"message\":null}}", -1, "")]
        [InlineData("{\"id\":2,\"error\":{\"code\":-1,\"message\":7}}", -1, "")]
        [InlineData("{\"id\":2,\"error\":{\"code\":-1,\"message\":{\"a\":1}}}", -1, "")]
        public void TryParse_AnErrorThatLeavesOutPartsOfItself_GetsADefaultCodeAndMessage(string line, int code, string message)
        {
            var response = Response(line);

            Assert.Equal(new YeelightError(code, message), response.Result.Error);
        }

        [Theory]
        [InlineData("{\"id\":2,\"error\":\"boom\"}")]
        [InlineData("{\"id\":2,\"error\":5}")]
        [InlineData("{\"id\":2,\"error\":true}")]
        [InlineData("{\"id\":2,\"error\":[]}")]
        [InlineData("{\"id\":2,\"error\":[{\"code\":-1,\"message\":\"x\"}]}")]
        [InlineData("{\"id\":2,\"error\":\"boom\",\"result\":[\"ok\"]}")]
        public void TryParse_AnErrorThatIsNotAnObject_IsNotAnAnswer(string line)
        {
            // Taking it for an answer would report a command as done that the light did not say it did (or as a failure with an invented code).
            Assert.False(YeelightProtocol.TryParse(line, out var message));
            Assert.Null(message);
        }

        [Theory]
        [InlineData("{\"id\":5}")]
        [InlineData("{\"id\":5,\"result\":\"ok\"}")]
        [InlineData("{\"id\":5,\"result\":5}")]
        [InlineData("{\"id\":5,\"result\":true}")]
        [InlineData("{\"id\":5,\"result\":null}")]
        [InlineData("{\"id\":5,\"result\":{\"a\":1}}")]
        [InlineData("{\"id\":5,\"error\":null}")]
        [InlineData("{\"id\":5,\"error\":null,\"result\":null}")]
        [InlineData("{\"id\":5,\"unrelated\":[\"ok\"]}")]
        public void TryParse_AnIdWithNeitherAListOfResultsNorAnError_IsNotAnAnswer(string line)
        {
            Assert.False(YeelightProtocol.TryParse(line, out var message));
            Assert.Null(message);
        }

        [Fact]
        public void TryParse_AnErrorThatIsNull_IsNoError()
        {
            var response = Response("{\"id\":5,\"error\":null,\"result\":[\"ok\"]}");

            Assert.True(response.Result.Success);
            Assert.Equal("ok", Assert.Single(response.Result.Values).GetString());
        }

        [Fact]
        public void TryParse_AnErrorAndAResult_AreAnError()
        {
            var response = Response("{\"id\":2,\"result\":[\"ok\"],\"error\":{\"code\":-1,\"message\":\"no\"}}");

            Assert.False(response.Result.Success);
            Assert.Equal(new YeelightError(-1, "no"), response.Result.Error);
            Assert.Empty(response.Result.Values);
        }

        [Fact]
        public void TryParse_AnErrorAndAResultThatIsNotAList_AreAnError()
        {
            var response = Response("{\"id\":2,\"result\":\"ok\",\"error\":{\"code\":-1,\"message\":\"no\"}}");

            Assert.Equal(new YeelightError(-1, "no"), response.Result.Error);
        }

        [Fact]
        public void TryParse_TheValuesOfAResponse_AreReadOnly()
        {
            var success = Response("{\"id\":1,\"result\":[\"ok\"]}");

            Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<JsonElement>>(success.Result.Values);
            Assert.False(success.Result.Values is JsonElement[]);
        }

        [Theory]
        [InlineData("{\"id\":7,\"result\":[\"ok\"]}", 7)]
        [InlineData("{\"id\":\"7\",\"result\":[\"ok\"]}", 7)]
        [InlineData("{\"id\":\" 7 \",\"result\":[\"ok\"]}", 7)]
        [InlineData("{\"id\":0,\"result\":[\"ok\"]}", 0)]
        [InlineData("{\"id\":-3,\"result\":[\"ok\"]}", -3)]
        [InlineData("{\"id\":2147483647,\"result\":[\"ok\"]}", int.MaxValue)]
        [InlineData("{\"result\":[\"ok\"],\"id\":4}", 4)]
        public void TryParse_TheId_IsANumberOrNumericText(string line, int id)
        {
            Assert.Equal(id, Response(line).Result.Id);
        }

        [Theory]
        [InlineData("{\"id\":7.5,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":\"abc\",\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":\"\",\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":2147483648,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":99999999999999999999,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":null,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":true,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":[1],\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":{\"a\":1},\"result\":[\"ok\"]}")]
        [InlineData("{\"result\":[\"ok\"]}")]
        public void TryParse_AnIdThatIsNotAWholeNumber_IsNotAMessage(string line)
        {
            Assert.False(YeelightProtocol.TryParse(line, out _));
        }

        [Fact]
        public void TryParse_ThePropsNotification_HasItsPropertiesAsTheyWereSent()
        {
            var notification = Notification("{\"method\":\"props\",\"params\":{\"power\":\"on\", \"bright\":\"10\"}}");

            Assert.Equal("props", notification.Method);
            Assert.Equal(2, notification.Parameters.Count);
            Assert.Equal("on", notification.Parameters["power"].GetString());
            Assert.Equal("10", notification.Parameters["bright"].GetString());
        }

        [Fact]
        public void TryParse_ANotificationWithNumbers_KeepsThemAsNumbers()
        {
            // The specification says that every value is text, and some firmware versions send numbers.
            var notification = Notification("{\"method\":\"props\",\"params\":{\"bright\":45,\"ct\":4000}}");

            Assert.Equal(JsonValueKind.Number, notification.Parameters["bright"].ValueKind);
            Assert.Equal(4000, notification.Parameters["ct"].GetInt32());
        }

        [Theory]
        [InlineData("{\"method\":\"props\"}")]
        [InlineData("{\"method\":\"props\",\"params\":[1,2]}")]
        [InlineData("{\"method\":\"props\",\"params\":\"power\"}")]
        [InlineData("{\"method\":\"props\",\"params\":null}")]
        [InlineData("{\"method\":\"props\",\"params\":{}}")]
        public void TryParse_ANotificationWithoutProperties_HasNone(string line)
        {
            Assert.Empty(Notification(line).Parameters);
        }

        [Fact]
        public void TryParse_ANotificationOfAnotherMethod_IsStillANotification()
        {
            // What to do with it is for the connection to decide (the specification: any other method is an invalid notification).
            var notification = Notification("{\"method\":\"other\",\"params\":{\"a\":\"b\"}}");

            Assert.Equal("other", notification.Method);
        }

        [Fact]
        public void TryParse_ARepeatedProperty_KeepsTheLastValue()
        {
            var notification = Notification("{\"method\":\"props\",\"params\":{\"power\":\"on\",\"power\":\"off\"}}");

            Assert.Equal("off", notification.Parameters["power"].GetString());
        }

        [Fact]
        public void TryParse_TheNamesOfTheProperties_AreCaseSensitive()
        {
            var notification = Notification("{\"method\":\"props\",\"params\":{\"Power\":\"on\"}}");

            Assert.False(notification.Parameters.ContainsKey("power"));
            Assert.True(notification.Parameters.ContainsKey("Power"));
        }

        [Fact]
        public void TryParse_AMethodThatIsNotText_IsNotANotification()
        {
            Assert.False(YeelightProtocol.TryParse("{\"method\":5,\"params\":{}}", out _));
        }

        [Theory]
        [InlineData("  {\"id\":1,\"result\":[\"ok\"]}  ")]
        [InlineData("\t{\"id\":1,\"result\":[\"ok\"]}")]
        [InlineData("{ \"id\" : 1 , \"result\" : [ \"ok\" ] }")]
        public void TryParse_WhiteSpaceAroundAndInsideTheMessage_IsFine(string line)
        {
            Assert.Equal(1, Response(line).Result.Id);
        }

        [Fact]
        public void TryParse_Null_IsNotAMessage()
        {
            Assert.False(YeelightProtocol.TryParse(null, out var message));
            Assert.Null(message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t \u00a0")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("[1,2,3]")]
        [InlineData("[{\"id\":1,\"result\":[\"ok\"]}]")]
        [InlineData("5")]
        [InlineData("\"text\"")]
        [InlineData("true")]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("{\"id\":1,")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"]")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"]}}")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"]} trailing")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"]}{\"id\":2,\"result\":[\"ok\"]}")]
        [InlineData("{'id':1,'result':['ok']}")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"],}")]
        [InlineData("/* comment */{\"id\":1,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":1,\"result\":[\"ok\"]} // comment")]
        [InlineData("{\"id\":NaN,\"result\":[\"ok\"]}")]
        [InlineData("{\"id\":1,\"result\":[\"unterminated]}")]
        [InlineData("{\"method\":\"props\",\"params\":{\"power\":}}")]
        [InlineData("\u0000")]
        [InlineData("{\"id\":1,\"result\":[\"\u0001\"]}")]
        public void TryParse_WhatIsNotAMessageOfTheProtocol_IsNotOneAndNeverThrows(string line)
        {
            Assert.False(YeelightProtocol.TryParse(line, out var message));
            Assert.Null(message);
        }

        [Fact]
        public void TryParse_DeepNesting_IsNotAMessageAndNeverOverflowsTheStack()
        {
            var arrays = new string('[', 200_000);
            var objects = string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "1" + new string('}', 100);
            var insideAResult = "{\"id\":1,\"result\":" + string.Concat(Enumerable.Repeat("[", 100)) + string.Concat(Enumerable.Repeat("]", 100)) + "}";

            Assert.False(YeelightProtocol.TryParse(arrays, out _));
            Assert.False(YeelightProtocol.TryParse(objects, out _));
            Assert.False(YeelightProtocol.TryParse(insideAResult, out _));
        }

        [Fact]
        public void TryParse_ALongMessage_IsRead()
        {
            var values = string.Join(",", Enumerable.Range(0, 20_000).Select(number => $"\"{number}\""));

            var response = Response("{\"id\":1,\"result\":[" + values + "]}");

            Assert.Equal(20_000, response.Result.Values.Count);
        }

        [Fact]
        public void TryParse_RandomText_NeverThrows()
        {
            var random = new Random(1982);
            string[] pieces = ["{", "}", "[", "]", ",", ":", "\"", "\\", "id", "result", "error", "method", "params", "props", "1", "-1", "null", "true", "\"ok\"", " ", "\u00e9", "\\u00e9"];
            for (var round = 0; round < 3000; round++)
            {
                var line = new StringBuilder();
                for (var index = random.Next(1, 30); index > 0; index--)
                {
                    line.Append(pieces[random.Next(pieces.Length)]);
                }

                var text = line.ToString();
                var exception = Record.Exception(() => YeelightProtocol.TryParse(text, out _));

                Assert.True(exception is null, $"TryParse threw {exception?.GetType().Name} for: {text}");
            }
        }

        [Fact]
        public void TryParse_ATextWithHalfOfASurrogatePair_NeverThrows()
        {
            // Nothing a device sends can make this throw (its documentation), and this is something that a device (or whoever sits between it
            // and this computer) can send: a \u escape that is half of a pair, as a method, as the name of a property, as a message or as an id.
            string[] lines =
            [
                "{\"method\":\"pr\\ud800ops\",\"params\":{}}",
                "{\"method\":\"props\",\"params\":{\"po\\ud800wer\":\"on\"}}",
                "{\"id\":1,\"error\":{\"code\":-1,\"message\":\"x\\ud800y\"}}",
                "{\"id\":\"\\ud800\",\"result\":[]}",
                "{\"id\":\"\\udc00\",\"result\":[]}",
                "{\"id\":1,\"result\":[\"\\udc00\"]}",
            ];
            var problems = new List<string>();
            foreach (var line in lines)
            {
                var exception = Record.Exception(() => YeelightProtocol.TryParse(line, out _));
                if (exception is not null)
                {
                    problems.Add($"{line}: {exception.GetType().Name}");
                }
            }

            Assert.True(problems.Count == 0, "TryParse threw for:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void TryParse_ALoneSurrogateInTheText_NeverThrows()
        {
            var line = "{\"id\":1,\"result\":[\"" + '\ud800' + "\"]}";

            var exception = Record.Exception(() => YeelightProtocol.TryParse(line, out _));

            Assert.True(exception is null, $"TryParse threw {exception?.GetType().Name}: {exception?.Message}");
        }

        [Theory]
        [InlineData("5", 5, true)]
        [InlineData("-5", -5, true)]
        [InlineData("0", 0, true)]
        [InlineData("2147483647", int.MaxValue, true)]
        [InlineData("-2147483648", int.MinValue, true)]
        [InlineData("\"5\"", 5, true)]
        [InlineData("\"-5\"", -5, true)]
        [InlineData("\"+5\"", 5, true)]
        [InlineData("\" 5 \"", 5, true)]
        [InlineData("\"05\"", 5, true)]
        [InlineData("\"abc\"", 0, false)]
        [InlineData("\"\"", 0, false)]
        [InlineData("\"5.5\"", 0, false)]
        [InlineData("\"0x10\"", 0, false)]
        [InlineData("\"1e3\"", 0, false)]
        [InlineData("\"99999999999\"", 0, false)]
        [InlineData("1.5", 0, false)]
        [InlineData("3000000000", 0, false)]
        [InlineData("-3000000000", 0, false)]
        [InlineData("true", 0, false)]
        [InlineData("false", 0, false)]
        [InlineData("null", 0, false)]
        [InlineData("[5]", 0, false)]
        [InlineData("{\"a\":5}", 0, false)]
        public void TryGetInt32_ReadsAWholeNumberThatWasSentAsANumberOrAsText(string json, int expected, bool success)
        {
            var read = YeelightProtocol.TryGetInt32(Element(json), out var value);

            Assert.Equal(success, read);
            Assert.Equal(expected, value);
        }

        [Fact]
        public void TryGetInt32_ATextWithHalfOfASurrogatePair_IsNotANumber()
        {
            using var document = JsonDocument.Parse("\"\\ud800\"");

            var exception = Record.Exception(() => YeelightProtocol.TryGetInt32(document.RootElement, out _));

            Assert.True(exception is null, $"TryGetInt32 threw {exception?.GetType().Name}: {exception?.Message}");
        }

        [Theory]
        [InlineData("\"on\"", "on")]
        [InlineData("\"\"", "")]
        [InlineData("\"a b\"", "a b")]
        [InlineData("\"\\u00e9\\n\"", "é\n")]
        [InlineData("100", "100")]
        [InlineData("-1", "-1")]
        [InlineData("1.50", "1.50")]
        [InlineData("1e2", "1e2")]
        [InlineData("true", null)]
        [InlineData("false", null)]
        [InlineData("null", null)]
        [InlineData("[\"on\"]", null)]
        [InlineData("{\"a\":\"on\"}", null)]
        public void ToText_AStringIsAsItIsANumberAsItWasWrittenAndAnythingElseIsNoValue(string json, string? expected)
        {
            Assert.Equal(expected, YeelightProtocol.ToText(Element(json)));
        }

        [Fact]
        public void ToText_ATextWithHalfOfASurrogatePair_NeverThrows()
        {
            using var document = JsonDocument.Parse("\"o\\ud800n\"");

            var exception = Record.Exception(() => YeelightProtocol.ToText(document.RootElement));

            Assert.True(exception is null, $"ToText threw {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
