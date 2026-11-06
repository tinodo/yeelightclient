namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The JSON helpers of the test app: the parameters that a person types for a raw command, and the answers that are shown.</summary>
    public sealed class JsonToolsTests
    {
        [Fact]
        public void AJsonArray_BecomesTheParametersOfACommand()
        {
            var ok = JsonTools.TryParseParameters("[50, \"smooth\", 500]", out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Equal(new object?[] { 50, "smooth", 500 }, parameters);
        }

        [Fact]
        public void TheBrackets_CanBeLeftOut()
        {
            var ok = JsonTools.TryParseParameters("\"on\", \"sudden\", 0", out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Equal(new object?[] { "on", "sudden", 0 }, parameters);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[]")]
        [InlineData(" [ ] ")]
        public void NoParameters_AreAnEmptyList(string text)
        {
            var ok = JsonTools.TryParseParameters(text, out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Empty(parameters);
        }

        [Fact]
        public void NumbersTextTruthValuesAndNull_KeepTheirTypes()
        {
            var ok = JsonTools.TryParseParameters("[1, 3000000000, 1.5, 1e3, \"text\", true, false, null]", out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Equal(8, parameters.Count);
            Assert.IsType<int>(parameters[0]);
            Assert.IsType<long>(parameters[1]);
            Assert.Equal(3_000_000_000L, parameters[1]);
            Assert.Equal(1.5, parameters[2]);
            Assert.Equal(1000.0, parameters[3]);
            Assert.Equal("text", parameters[4]);
            Assert.Equal(true, parameters[5]);
            Assert.Equal(false, parameters[6]);
            Assert.Null(parameters[7]);
        }

        [Fact]
        public void ATrailingCommaAndComments_AreForgiven()
        {
            var ok = JsonTools.TryParseParameters("[1, 2, /* the second */ 3,]", out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Equal(new object?[] { 1, 2, 3 }, parameters);
        }

        [Fact]
        public void TextWithEscapes_IsUnescaped()
        {
            var ok = JsonTools.TryParseParameters("[\"a \\\"quoted\\\" name\", \"caf\\u00e9\"]", out var parameters, out var error);

            Assert.True(ok, error);
            Assert.Equal(new object?[] { "a \"quoted\" name", "caf\u00e9" }, parameters);
        }

        [Theory]
        [InlineData("[{\"a\": 1}]", "Parameter 1 is an object")]
        [InlineData("[1, [2, 3]]", "Parameter 2 is a list")]
        [InlineData("[1, 2", "not valid JSON")]
        [InlineData("[1 2]", "not valid JSON")]
        [InlineData("{\"a\": 1}", "Parameter 1 is an object")]
        [InlineData("[\"unterminated]", "not valid JSON")]
        public void ParametersThatCannotBeSent_AreExplained(string text, string expected)
        {
            var ok = JsonTools.TryParseParameters(text, out var parameters, out var error);

            Assert.False(ok);
            Assert.Contains(expected, error, StringComparison.Ordinal);
            Assert.Empty(parameters);
        }

        [Fact]
        public void Parameters_CanBeWrittenAndReadBack()
        {
            var original = new object?[] { 50, "smooth", 500, true, null, 1.5, 3_000_000_000L, "a \"quoted\" name" };

            var text = JsonTools.FormatParameters(original);
            var ok = JsonTools.TryParseParameters(text, out var parsed, out var error);

            Assert.True(ok, error);
            Assert.Equal(original, parsed);
            Assert.Equal("[50, \"smooth\", 500, true, null, 1.5, 3000000000, \"a \\\"quoted\\\" name\"]", text);
        }

        [Fact]
        public void NoParameters_AreWrittenAsAnEmptyList()
        {
            Assert.Equal("[]", JsonTools.FormatParameters([]));
        }

        [Fact]
        public void TheValuesOfAnAnswer_AreWrittenAsTheLightSentThem()
        {
            using var document = JsonDocument.Parse("[\"ok\", 1, {\"delay\": 15}, \"caf\\u00e9\"]");
            var values = new List<JsonElement>(document.RootElement.EnumerateArray());

            Assert.Equal("[\"ok\",1,{\"delay\":15},\"caf\u00e9\"]", JsonTools.ValuesToJson(values));
            Assert.Contains("\n", JsonTools.ValuesToIndentedJson(values), StringComparison.Ordinal);
            Assert.Equal("[]", JsonTools.ValuesToJson([]));
        }

        [Fact]
        public void Pretty_IndentsJson_AndLeavesOtherTextAlone()
        {
            Assert.Equal("{\r\n  \"a\": 1\r\n}".ReplaceLineEndings(), JsonTools.Pretty("{\"a\":1}").ReplaceLineEndings());
            Assert.Equal("this is not json", JsonTools.Pretty("this is not json"));
        }

        [Theory]
        [InlineData("power bright ct", "power,bright,ct")]
        [InlineData("power, bright;ct", "power,bright,ct")]
        [InlineData("  power\tpower  bright\r\nct ", "power,bright,ct")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void ANamesList_IsSplitOnSpacesCommasAndSemicolons_WithoutDuplicates(string text, string expected)
        {
            Assert.Equal(expected, string.Join(',', JsonTools.ParseNames(text)));
        }

        [Fact]
        public void OneLine_CollapsesAMessage_AndCutsALongOne()
        {
            Assert.Equal("a b c", JsonTools.OneLine("a\r\nb   c"));
            var cut = JsonTools.OneLine(new string('x', 500), 50);
            Assert.Equal(51, cut.Length);
            Assert.EndsWith("\u2026", cut, StringComparison.Ordinal);
        }
    }
}
