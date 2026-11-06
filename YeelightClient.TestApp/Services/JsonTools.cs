namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    /// <summary>Reading, writing and cleaning up JSON for the screen and for exports.</summary>
    internal static class JsonTools
    {
        private static readonly JsonWriterOptions IndentedWriter = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static readonly JsonWriterOptions CompactWriter = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>Indents JSON; text that is not JSON is returned as it is.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The indented text.</returns>
        public static string Pretty(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return Write(document.RootElement, IndentedWriter);
            }
            catch (JsonException)
            {
                return json;
            }
        }

        /// <summary>Writes the values of the answer to a command as a JSON array on one line, as the light sent them.</summary>
        /// <param name="values">The values of <see cref="YeelightResult.Values"/>.</param>
        /// <returns>For example <c>["ok"]</c>.</returns>
        public static string ValuesToJson(IReadOnlyList<JsonElement> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return ArrayToJson(values, indented: false);
        }

        /// <summary>Writes the values of the answer to a command as an indented JSON array.</summary>
        /// <param name="values">The values of <see cref="YeelightResult.Values"/>.</param>
        /// <returns>The text.</returns>
        public static string ValuesToIndentedJson(IReadOnlyList<JsonElement> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return ArrayToJson(values, indented: true);
        }

        /// <summary>Writes parameters as the JSON array that the parameters box of the Raw page holds.</summary>
        /// <param name="parameters">The parameters: numbers, text, truth values and <c>null</c>.</param>
        /// <returns>For example <c>[50, "smooth", 500]</c>.</returns>
        public static string FormatParameters(IEnumerable<object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            return "[" + string.Join(", ", parameters.Select(FormatValue)) + "]";
        }

        /// <summary>
        /// Reads the parameters of a command from what a person typed: a JSON array (<c>[50, "smooth", 500]</c>), or the same without the
        /// brackets, or nothing at all for a command that has no parameters. Whole numbers become <see cref="int"/> (or <see cref="long"/>
        /// when they are too large), other numbers <see cref="double"/>, text <see cref="string"/>, and <c>true</c> and <c>false</c> <see cref="bool"/>.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="parameters">The parameters, when the text could be read.</param>
        /// <param name="error">What is wrong with the text, in words, when it could not be read.</param>
        /// <returns><c>true</c> when the text could be read.</returns>
        public static bool TryParseParameters(string? text, out IReadOnlyList<object?> parameters, out string error)
        {
            parameters = [];
            error = string.Empty;
            var trimmed = text?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return true;
            }

            var json = trimmed.StartsWith('[') ? trimmed : "[" + trimmed + "]";
            try
            {
                using var document = JsonDocument.Parse(json, Lenient);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    error = "The parameters are a list of values between brackets, such as [50, \"smooth\", 500].";
                    return false;
                }

                var list = new List<object?>();
                var position = 0;
                foreach (var element in root.EnumerateArray())
                {
                    position++;
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Number:
                            if (element.TryGetInt32(out var small))
                            {
                                list.Add(small);
                            }
                            else if (element.TryGetInt64(out var large))
                            {
                                list.Add(large);
                            }
                            else
                            {
                                list.Add(element.GetDouble());
                            }

                            break;
                        case JsonValueKind.String:
                            list.Add(element.GetString());
                            break;
                        case JsonValueKind.True:
                            list.Add(true);
                            break;
                        case JsonValueKind.False:
                            list.Add(false);
                            break;
                        case JsonValueKind.Null:
                            list.Add(null);
                            break;
                        default:
                            error = $"Parameter {position} is {Describe(element.ValueKind)}: a command takes numbers, text, true, false and null.";
                            return false;
                    }
                }

                parameters = list;
                return true;
            }
            catch (JsonException exception)
            {
                error = $"The parameters are not valid JSON ({exception.Message.TrimEnd('.')}). Write a list such as [50, \"smooth\", 500].";
                return false;
            }
        }

        /// <summary>Splits a list of names that a person typed: separated by spaces, commas or semicolons, without duplicates.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The names, in the order in which they were typed.</returns>
        public static IReadOnlyList<string> ParseNames(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return [.. text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];
        }

        /// <summary>Collapses a message to one line, for a list.</summary>
        /// <param name="text">The text.</param>
        /// <param name="maximum">The most characters to keep; more are replaced by an ellipsis.</param>
        /// <returns>The text on one line.</returns>
        public static string OneLine(string text, int maximum = 160)
        {
            ArgumentNullException.ThrowIfNull(text);
            var line = string.Join(' ', text.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
            return line.Length <= maximum ? line : line[..maximum] + "\u2026";
        }

        private static string ArrayToJson(IReadOnlyList<JsonElement> values, bool indented)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, indented ? IndentedWriter : CompactWriter))
            {
                writer.WriteStartArray();
                foreach (var value in values)
                {
                    value.WriteTo(writer);
                }

                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string Write(JsonElement element, JsonWriterOptions options)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                element.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null => "null",
                string text => JsonSerializer.Serialize(text, StringOptions),
                bool flag => flag ? "true" : "false",
                Enum choice => Convert.ToInt32(choice, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => JsonSerializer.Serialize(value.ToString(), StringOptions),
            };
        }

        private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static string Describe(JsonValueKind kind)
        {
            return kind switch
            {
                JsonValueKind.Object => "an object",
                JsonValueKind.Array => "a list",
                _ => "not a value that a command takes",
            };
        }
    }
}
