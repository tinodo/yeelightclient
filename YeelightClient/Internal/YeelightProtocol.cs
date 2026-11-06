namespace Yeelight
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Encodings.Web;
    using System.Text.Json;

    /// <summary>A message that a device sends over the control connection.</summary>
    internal abstract record YeelightMessage;

    /// <summary>The answer to a command.</summary>
    internal sealed record YeelightResponse(YeelightResult Result) : YeelightMessage;

    /// <summary>A message that the device sends by itself, such as <c>props</c> when its state changed.</summary>
    internal sealed record YeelightNotification(string Method, IReadOnlyDictionary<string, JsonElement> Parameters) : YeelightMessage;

    /// <summary>
    /// The messages of the Yeelight control protocol: one JSON object per line, ended with CR LF.
    /// </summary>
    internal static class YeelightProtocol
    {
        private static readonly JsonWriterOptions WriterOptions = new()
        {
            // Nothing that a person types is changed on the way: no <c>\u002B</c> for a plus in a name.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Makes the line for a command.
        /// </summary>
        /// <param name="id">The number that the answer will carry, unique on the connection.</param>
        /// <param name="method">The method, for example <c>set_power</c>.</param>
        /// <param name="parameters">The parameters (numbers, text and truth values), possibly none.</param>
        /// <returns>The bytes to send.</returns>
        public static byte[] BuildRequest(int id, string method, IReadOnlyList<object?>? parameters)
        {
            ArgumentException.ThrowIfNullOrEmpty(method);

            var buffer = new ArrayBufferWriter<byte>(128);
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                writer.WriteStartArray("params");
                if (parameters is not null)
                {
                    foreach (var parameter in parameters)
                    {
                        WriteValue(writer, parameter);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            var span = buffer.GetSpan(2);
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            buffer.Advance(2);
            return buffer.WrittenSpan.ToArray();
        }

        /// <summary>
        /// Reads a line that a device sent. Nothing a device sends can make this throw.
        /// </summary>
        /// <param name="line">The line, without its line break.</param>
        /// <param name="message">The message, when the line is one that is understood.</param>
        /// <returns><c>false</c> for a line that is not a message of the protocol.</returns>
        public static bool TryParse(string? line, out YeelightMessage message)
        {
            message = null!;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
                {
                    var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (root.TryGetProperty("params", out var values) && values.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var value in values.EnumerateObject())
                        {
                            parameters[value.Name] = value.Value.Clone();
                        }
                    }

                    message = new YeelightNotification(method.GetString() ?? string.Empty, parameters);
                    return true;
                }

                if (root.TryGetProperty("id", out var idElement) && TryGetInt32(idElement, out var id))
                {
                    // An answer is a list of results or an error. A line with an id and neither of those (or with an error that is not an
                    // object) is not an answer: taking it for one would report a command as done that the light did not say it did.
                    var hasError = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null;
                    if (hasError)
                    {
                        if (errorElement.ValueKind != JsonValueKind.Object)
                        {
                            return false;
                        }

                        var code = errorElement.TryGetProperty("code", out var codeElement) && TryGetInt32(codeElement, out var parsedCode) ? parsedCode : -1;
                        var text = errorElement.TryGetProperty("message", out var messageElement) ? TryGetString(messageElement) : null;
                        message = new YeelightResponse(new YeelightResult(id, YeelightError.FromLight(code, text ?? string.Empty), []));
                        return true;
                    }

                    if (root.TryGetProperty("result", out var resultElement) && resultElement.ValueKind == JsonValueKind.Array)
                    {
                        var result = new List<JsonElement>();
                        foreach (var item in resultElement.EnumerateArray())
                        {
                            result.Add(item.Clone());
                        }

                        message = new YeelightResponse(new YeelightResult(id, null, result.AsReadOnly()));
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                // Not JSON (or a text that JSON allows and that is not text: a lone half of a surrogate pair in an escape, such as "\ud800"): not a message.
            }

            return false;
        }

        /// <summary>
        /// Reads a whole number that a device sent as a number or, as some firmware versions do, as text (<c>"bright":"45"</c>).
        /// </summary>
        public static bool TryGetInt32(JsonElement element, out int value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.TryGetInt32(out value);
                case JsonValueKind.String:
                    return int.TryParse(TryGetString(element), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    value = 0;
                    return false;
            }
        }

        /// <summary>
        /// Reads a value that a device sent as the text that it stands for: a string as it is, a number as it was written; anything else
        /// (an object, a list, <c>null</c>, a string that is not text) is not a value of a property.
        /// </summary>
        public static string? ToText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => TryGetString(element),
                JsonValueKind.Number => element.GetRawText(),
                _ => null,
            };
        }

        /// <summary>
        /// Reads a string that a device sent. JSON allows an escape for half of a surrogate pair (<c>"\ud800"</c>), which is not text: reading it
        /// throws, so a device (or anyone who sends as one) could break whatever reads it. That is not a string here.
        /// </summary>
        /// <returns>The text, or <c>null</c> when the value is not a string, or not one that is text.</returns>
        public static string? TryGetString(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            try
            {
                return element.GetString();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case short or byte or sbyte or ushort:
                    writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;
                case uint number:
                    writer.WriteNumberValue(number);
                    break;
                case ulong number:
                    writer.WriteNumberValue(number);
                    break;
                case float number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case Half number:
                    writer.WriteNumberValue((double)number);
                    break;
                case nint number:
                    writer.WriteNumberValue((long)number);
                    break;
                case nuint number:
                    writer.WriteNumberValue((ulong)number);
                    break;
                case Int128 number:
                    writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case UInt128 number:
                    writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case char letter:
                    writer.WriteStringValue(letter.ToString());
                    break;
                case Enum choice:
                    if (Enum.GetUnderlyingType(choice.GetType()) == typeof(ulong))
                    {
                        writer.WriteNumberValue(Convert.ToUInt64(choice, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.WriteNumberValue(Convert.ToInt64(choice, CultureInfo.InvariantCulture));
                    }

                    break;
                default:
                    throw new ArgumentException($"A parameter of a command is a number, text or a truth value, and not a {value.GetType().Name}.", nameof(value));
            }
        }
    }
}
