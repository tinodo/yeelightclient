namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using FsCheck;
    using FsCheck.Fluent;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// A case of the receive fuzz: what the server sends on its own before a command, and what it sends in answer to the command (lines
    /// that may be wrong in any way, followed by the right answer). The id of the command is not known when the cases are made: the lines of
    /// the answer have <see cref="FuzzGenerators.IdToken"/> where it goes.
    /// </summary>
    /// <param name="Unsolicited">The bytes that are sent before the command.</param>
    /// <param name="AnswerLines">The lines that are sent in answer to the command, before the right answer.</param>
    internal sealed record ReceiveCase(byte[] Unsolicited, string[] AnswerLines)
    {
        /// <summary>Gets the bytes that are sent in answer to the command with this id: the lines, and then the right answer.</summary>
        public IReadOnlyList<byte[]> AnswerFor(int id)
        {
            var pieces = new List<byte[]>();
            foreach (var line in this.AnswerLines)
            {
                pieces.Add(Encoding.UTF8.GetBytes(line.Replace(FuzzGenerators.IdToken, id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) + "\r\n"));
            }

            pieces.Add(ScriptedServer.Ok(id));
            return pieces;
        }

        /// <summary>Tells what the case holds, for the message of a failure.</summary>
        public override string ToString()
        {
            return $"unsolicited {FuzzGenerators.Preview(Encoding.UTF8.GetString(this.Unsolicited))}, answer {FuzzGenerators.Preview(string.Join(" | ", this.AnswerLines))}";
        }
    }

    /// <summary>
    /// Generators of what a light, or somebody who sends as one, could send: values of every shape as JSON text, messages of the protocol
    /// with something wrong in them, announcements with something wrong in them, and plain junk. The names of the members are the ones of
    /// the protocol, so that a random message now and then has the shape of a real one, and what is wrong in it is a member, its type or its
    /// absence.
    /// </summary>
    internal static class FuzzGenerators
    {
        /// <summary>Where the id of the command goes in a line of an answer.</summary>
        public const string IdToken = "@ID@";

        /// <summary>Numbers as JSON writes them, and as it is allowed to write them: signs, exponents, what an int cannot hold, and what a double cannot.</summary>
        internal static readonly string[] Numbers =
        [
            "0", "-0", "1", "-1", "5", "42", "100", "101", "359", "360", "1700", "6500", "65535", "16777215", "16777216", "2147483647", "2147483648",
            "-2147483648", "-2147483649", "9223372036854775807", "123456789012345678901234567890", "0.5", "-1.25", "1e3", "1E-3", "5.0", "1e2",
            "3.4028235e38", "1e999", "-1e999", "1e-999", "0.1e1",
        ];

        // Text that is not JSON as a number: leading zeros, signs, hexadecimal, what a double says for infinity.
        private static readonly string[] InvalidNumbers = ["00", "+1", ".5", "5.", "0x10", "NaN", "Infinity", "-Infinity", "1e", "--1", "1,5"];

        // Text that is not like the rest: empty, with a character that JSON has to escape, outside the first plane, or half of a pair.
        private static readonly string[] OddText =
        [
            string.Empty, " ", "\0", "\n", "\r", "\r\n", "\t", "\"", "\\", "/", "\u00e4\u00f6\u00fc", "\u20ac", "\ud83d\ude00", "\ud83d", "\ude00", "\uffff", "\u2028",
            "\u202e", "\u0001\u0002\u0003", "{", "}", "[", "]", ":", ",", "on", "off", "ok", "null", "0", "1", "-1", "NaN",
        ];

        // The same, without what cannot be written to a line and read back: no half of a pair, no line break.
        private static readonly string[] CleanOdd =
        [
            string.Empty, " ", "\0", "\t", "\"", "\\", "/", "\u00e4\u00f6\u00fc", "\u20ac", "\ud83d\ude00", "\uffff", "\u2028", "\u202e", "{", "}", "[", "]", ":", ",", "on", "ok",
        ];

        // The names of the properties of a light, and names that are not.
        private static readonly string[] Names =
        [
            .. YeelightProperties.All, "props", "method", "params", "id", "result", "error", "code", "message", "bg_unknown", "unknown", string.Empty,
            "POWER", "Bright", "power ", "bright\0", "__proto__", "bg_", "ct ",
        ];

        private static readonly Gen<char> AnyChar = Gen.Choose(0, 0xFFFF).Select(code => (char)code);

        private static readonly Gen<char> PrintableChar = Gen.Choose(0x20, 0x7E).Select(code => (char)code);

        private static readonly string[] Seeds =
        [
            "{\"method\":\"props\",\"params\":{\"power\":\"on\",\"bright\":\"50\"}}",
            "{\"id\":1,\"result\":[\"ok\"]}",
            "{\"id\":1,\"error\":{\"code\":-1,\"message\":\"unsupported method\"}}",
            "{\"id\":1,\"result\":[\"on\",\"100\",\"4000\",\"16711680\",\"100\",\"35\",\"2\",\"0\",\"0\",\"\",\"0\",\"name\"]}",
            "{\"method\":\"props\",\"params\":{\"bg_power\":\"on\",\"bg_rgb\":\"255\",\"flow_params\":\"0,0,1000,2,2700,100\"}}",
        ];

        private static readonly Gen<(int Kind, int Position, int Code)> Edit =
            from kind in Gen.Choose(0, 4)
            from position in Gen.Choose(0, 1000)
            from code in Gen.Choose(0, 0xFFFF)
            select (kind, position, code);

        /// <summary>Gets a generator of text of any kind: random, odd, long, with half of a surrogate pair, with control characters.</summary>
        public static Gen<string> Text { get; } = Gen.Frequency(
            (4, ArbMap.Default.GeneratorFor<string>().Select(text => text ?? string.Empty)),
            (3, Gen.ListOf(PrintableChar).Select(chars => new string(chars.ToArray()))),
            (3, Gen.ListOf(AnyChar).Select(chars => new string(chars.ToArray()))),
            (3, Gen.Elements(OddText)),
            (1, from length in Gen.Choose(1000, 6000) from character in Gen.Choose(0x20, 0x7E) select new string((char)character, length)),
            (1, Gen.Elements(Numbers)));

        /// <summary>Gets a generator of text that can be written to a line and read back as it was: valid UTF-16, with no line break in it.</summary>
        public static Gen<string> CleanText { get; } = Gen.Frequency(
            (4, ArbMap.Default.GeneratorFor<string>().Select(text => Clean(text ?? string.Empty))),
            (3, Gen.ListOf(PrintableChar).Select(chars => new string(chars.ToArray()))),
            (3, Gen.ListOf(AnyChar).Select(chars => Clean(new string(chars.ToArray())))),
            (2, Gen.Elements(CleanOdd)));

        /// <summary>Gets a generator of the JSON text of a scalar: null, a boolean, a number or a string (and now and then text that is not a number).</summary>
        public static Gen<string> Scalar { get; } = Gen.Frequency(
            (2, Gen.Constant("null")),
            (2, Gen.Elements("true", "false")),
            (4, Gen.Choose(-10, 1000).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (3, Gen.Elements(Numbers)),
            (6, Text.Select(Quote)),
            (1, Gen.Elements(InvalidNumbers)));

        /// <summary>Gets a generator of the JSON text of a scalar that is valid JSON, always.</summary>
        public static Gen<string> ValidScalar { get; } = Gen.Frequency(
            (2, Gen.Constant("null")),
            (2, Gen.Elements("true", "false")),
            (4, Gen.Choose(-10, 1000).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (3, Gen.Elements(Numbers)),
            (6, Text.Select(Quote)));

        /// <summary>Gets a generator of the JSON text of a value of the way that a property of a light has: a number as text, a number, a word.</summary>
        public static Gen<string> PropertyValue { get; } = Gen.Frequency(
            (4, Gen.Choose(-5, 200).Select(number => Quote(number.ToString(CultureInfo.InvariantCulture)))),
            (3, Gen.Choose(-5, 200).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (3, Gen.Elements("\"on\"", "\"off\"", "\"0\"", "\"1\"", "\"2\"", "\"3\"", "\"\"")),
            (2, Scalar),
            (1, Value(2)),
            (1, Text.Select(Quote)));

        /// <summary>Gets a generator of the same, as valid JSON, always.</summary>
        public static Gen<string> ValidPropertyValue { get; } = Gen.Frequency(
            (4, Gen.Choose(-5, 200).Select(number => Quote(number.ToString(CultureInfo.InvariantCulture)))),
            (3, Gen.Choose(-5, 200).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (3, Gen.Elements("\"on\"", "\"off\"", "\"0\"", "\"1\"", "\"2\"", "\"3\"", "\"\"", "\"16711680\"", "\"99999999999\"")),
            (2, ValidScalar),
            (1, ValidValue(2)),
            (1, Text.Select(Quote)));

        /// <summary>Gets a generator of the text of a value of a property, as an announcement writes it (no line break in it).</summary>
        public static Gen<string> PropertyValueText { get; } = Gen.Frequency(
            (4, Gen.Choose(-5, 200).Select(number => number.ToString(CultureInfo.InvariantCulture))),
            (3, Gen.Elements("on", "off", "0", "1", "2", "3", string.Empty, " ", "16711680", "100000", "99999999999")),
            (2, Text.Select(NoBreaks)));

        /// <summary>Gets a generator of the members of the object of a <c>props</c> message (without the braces).</summary>
        public static Gen<string> PropsMembers { get; } =
            Gen.ListOf(
                from key in Gen.Frequency((8, Gen.Elements(Names)), (2, Text))
                from value in PropertyValue
                select Quote(key) + ":" + value)
            .Select(members => string.Join(",", members));

        /// <summary>
        /// Gets a generator of the JSON text of a value of any shape (JSON, but for the number that is not one now and then, which a light
        /// should not send and has to be refused, whatever it holds).
        /// </summary>
        /// <param name="depth">How deep arrays and objects may go.</param>
        public static Gen<string> Value(int depth = 3)
        {
            return depth <= 0
                ? Scalar
                : Gen.Frequency((5, Scalar), (2, Array(depth - 1)), (3, Object(depth - 1)));
        }

        /// <summary>Gets a generator of the JSON text of a value of any shape, which is valid JSON, always.</summary>
        /// <param name="depth">How deep arrays and objects may go.</param>
        public static Gen<string> ValidValue(int depth = 3)
        {
            if (depth <= 0)
            {
                return ValidScalar;
            }

            var key = Gen.Frequency((6, Gen.Elements(Names)), (1, Text));
            var member = from name in key from value in ValidValue(depth - 1) select Quote(name) + ":" + value;
            return Gen.Frequency(
                (5, ValidScalar),
                (2, Gen.ListOf(ValidValue(depth - 1)).Select(items => "[" + string.Join(",", items) + "]")),
                (3, Gen.ListOf(member).Select(members => "{" + string.Join(",", members) + "}")));
        }

        /// <summary>Gets a generator of an object: members with names of the protocol, or random ones, and values of any shape.</summary>
        public static Gen<string> Object(int depth = 2)
        {
            var key = Gen.Frequency((6, Gen.Elements(Names)), (1, Text));
            var member = from name in key from value in Value(depth) select Quote(name) + ":" + value;
            return Gen.ListOf(member).Select(members => "{" + string.Join(",", members) + "}");
        }

        /// <summary>Gets a generator of an array of values of any shape.</summary>
        public static Gen<string> Array(int depth = 2)
        {
            return Gen.ListOf(Value(depth)).Select(items => "[" + string.Join(",", items) + "]");
        }

        /// <summary>
        /// Gets a generator of the id of a message of answer: the id of the command (as <see cref="IdToken"/>), written right or not.
        /// </summary>
        public static Gen<string> AnswerId() => Gen.Frequency(
            (14, Gen.Constant(IdToken)),
            (1, Gen.Constant("\"" + IdToken + "\"")),
            (1, Gen.Constant(IdToken + ".0")),
            (1, Gen.Constant(IdToken + "0")),
            (1, Gen.Constant("-" + IdToken)),
            (1, Gen.Constant("[" + IdToken + "]")),
            (2, Gen.Elements("null", "true", "false", "{}", "\"\"", "2147483648", "-2147483649", "1e1", "0", "1", "\"abc\"")));

        /// <summary>
        /// Gets a generator of a line that looks like the answer to a command: the id (right, or not) and a result or an error of any shape.
        /// </summary>
        public static Gen<string> Answer()
        {
            var body = Gen.Frequency(
                (6, from values in Gen.ListOf(Value(1)) select ",\"result\":[" + string.Join(",", values) + "]"),
                (2, from value in Value(2) select ",\"result\":" + value),
                (2, from value in Value(2) select ",\"error\":" + value),
                (3, from code in Gen.Choose(-5000, 5000).Where(code => code is < -103 or > -100) from text in Text select ",\"error\":{\"code\":" + code.ToString(CultureInfo.InvariantCulture) + ",\"message\":" + Quote(text) + "}"),
                (1, Gen.Constant(string.Empty)),
                (1, from extra in Object(1) select ",\"result\":[\"ok\"],\"extra\":" + extra));
            return from id in AnswerId() from rest in body select "{\"id\":" + id + rest + "}";
        }

        /// <summary>
        /// Gets a generator of the messages that a light sends by itself: <c>props</c> with members of any shape, other methods, and a
        /// <c>method</c> or <c>params</c> that is not what it should be.
        /// </summary>
        public static Gen<string> Notification()
        {
            return Gen.Frequency(
                (8, from members in PropsMembers select "{\"method\":\"props\",\"params\":{" + members + "}}"),
                (2, from method in Value(1) from arguments in Value(2) select "{\"method\":" + method + ",\"params\":" + arguments + "}"),
                (2, from arguments in Value(2) select "{\"method\":\"props\",\"params\":" + arguments + "}"),
                (1, Gen.Constant("{\"method\":\"props\"}")),
                (1, from arguments in Value(2) select "{\"params\":" + arguments + "}"),
                (1, from method in Text from members in PropsMembers select "{\"method\":" + Quote(method) + ",\"params\":{" + members + "}}"));
        }

        /// <summary>Gets a generator of a valid message with a few changes made to it: characters deleted, inserted, replaced, a cut, a copy.</summary>
        public static Gen<string> Mutated()
        {
            return from seed in Gen.Elements(Seeds) from edits in Gen.ListOf(Edit) select Mutate(seed, edits);
        }

        /// <summary>
        /// Gets a generator of the text of a line that a light could send, or should not: mostly messages of the protocol with something
        /// wrong in them, sometimes JSON of another shape, sometimes text that is not JSON.
        /// </summary>
        public static Gen<string> Message()
        {
            return Gen.Frequency(
                (8, Notification()),
                (5, Answer()),
                (3, Mutated()),
                (2, Value()),
                (1, Text));
        }

        /// <summary>
        /// Gets a generator of the bytes of one thing that is sent over the connection: a line, with its break, or bytes that are not text.
        /// </summary>
        public static Gen<byte[]> RawFrame()
        {
            var crlf = new byte[] { 13, 10 };
            return Gen.Frequency(
                (12, Message().Select(text => Encoding.UTF8.GetBytes(Unsolicited(text) + "\r\n"))),
                (2, Message().Select(text => Encoding.UTF8.GetBytes(Unsolicited(text) + "\n"))),
                (1, Gen.ListOf(Gen.Choose(0, 255).Select(value => (byte)value)).Select(bytes => bytes.Where(value => value != 10).Concat(crlf).ToArray())),
                (1, Gen.Choose(0, 200).Select(length => new byte[length].Concat(crlf).ToArray())),
                (1, Gen.Elements(string.Empty, "\r\n", "\n", "\r", "\r\r\n", "\r\n\r\n", " \r\n").Select(text => Encoding.UTF8.GetBytes(text))));
        }

        /// <summary>
        /// Gets a generator of the text of a message that is sent over UDP: the answer to a search or an announcement of a light, field by
        /// field, with fields that are missing, repeated, of another kind, too long, or not what they should be.
        /// </summary>
        public static Gen<string> Announcement()
        {
            var start = Gen.Frequency(
                (6, Gen.Constant("HTTP/1.1 200 OK")),
                (6, Gen.Constant("NOTIFY * HTTP/1.1")),
                (1, Text),
                (1, Gen.Constant("M-SEARCH * HTTP/1.1")),
                (1, Gen.Constant("HTTP/1.1 404 Not Found")));
            var location = Gen.Frequency(
                (8, from port in Gen.Choose(1, 65535) select "yeelight://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)),
                (2, from port in Gen.Choose(-5, 70000) select "yeelight://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)),
                (3, from host in Gen.Elements("127.0.0.2", "10.0.0.1", "192.168.1.5", "8.8.8.8", "localhost", "::1", "[::1]", "0x7f000001", "2130706433", "127.1", string.Empty) select "yeelight://" + host + ":55443"),
                (1, Text.Select(NoBreaks)));
            var id = Gen.Frequency(
                (8, from number in Gen.Choose(0, int.MaxValue) select "0x" + number.ToString("x16", CultureInfo.InvariantCulture)),
                (1, Text.Select(NoBreaks)),
                (1, Gen.Constant(new string('i', 65))),
                (1, Gen.Constant(string.Empty)));
            var header = Gen.Frequency(
                (3, Gen.Elements("model: color", "model: stripe", "model: ", "fw_ver: 18", "support: get_prop set_power", "support: \tget_prop  toggle", "support: ")),
                (4, from name in Gen.Elements("power", "bright", "ct", "rgb", "hue", "sat", "color_mode", "name") from value in PropertyValueText select name + ": " + value),
                (2, Gen.Elements("NTS: ssdp:alive", "NTS: ssdp:byebye", "NTS: ", "Cache-Control: max-age=3600", "Server: POSIX UPnP/1.0 YGLC/1", "Date: ", "Ext: ", "HOST: 239.255.255.250:1982", "LOCATION: yeelight://127.0.0.1:1", "ID: 0xother")),
                (1, Text.Select(NoBreaks)),
                (1, from name in Text.Select(NoBreaks) from value in Text.Select(NoBreaks) select name + ":" + value));
            return from first in start
                   from locationLine in location.Select(value => "Location: " + value)
                   from idLine in id.Select(value => "id: " + value)
                   from complete in Gen.Frequency((8, Gen.Constant(true)), (2, Gen.Constant(false)))
                   from extra in Gen.ListOf(header)
                   from separator in Gen.Frequency((8, Gen.Constant("\r\n")), (1, Gen.Constant("\n")), (1, Gen.Constant("\r")))
                   from ending in Gen.Elements("\r\n", "\n", "\r\n\r\n", string.Empty, "\r")
                   select first + separator + string.Join(separator, complete ? new[] { locationLine, idLine }.Concat(extra) : extra) + ending;
        }

        /// <summary>
        /// Gets a generator of datagrams that a neighbour on the network could send to the port of the lights: announcements that are more or
        /// less right, junk, and bytes.
        /// </summary>
        public static Gen<byte[]> Datagram()
        {
            return Gen.Frequency(
                (8, Announcement().Select(text => Encoding.UTF8.GetBytes(text))),
                (2, Mutated().Select(text => Encoding.UTF8.GetBytes(text))),
                (2, Gen.ListOf(Gen.Choose(0, 255).Select(value => (byte)value)).Select(bytes => bytes.ToArray())),
                (1, Gen.Constant(System.Array.Empty<byte>())),
                (1, Text.Select(text => Encoding.UTF8.GetBytes(text))),
                (1, from length in Gen.Choose(4000, 5000) from fill in Gen.Choose(65, 90) select Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" + new string((char)fill, length))));
        }

        /// <summary>Gets a generator of an IPv4 address: of the ranges that matter (loopback, private, link-local, shared, public) and random.</summary>
        public static Gen<System.Net.IPAddress> Address()
        {
            var known = Gen.Elements("0.0.0.0", "127.0.0.1", "127.0.0.2", "127.255.255.254", "10.0.0.5", "192.168.1.50", "172.16.0.9", "172.31.255.1", "172.32.0.1", "169.254.1.1", "100.64.0.1", "100.128.0.1", "8.8.8.8", "255.255.255.255", "203.0.113.5")
                .Select(text => System.Net.IPAddress.Parse(text));
            var random = from first in Gen.Choose(0, 255)
                         from second in Gen.Choose(0, 255)
                         from third in Gen.Choose(0, 255)
                         from fourth in Gen.Choose(0, 255)
                         select new System.Net.IPAddress(new[] { (byte)first, (byte)second, (byte)third, (byte)fourth });
            return Gen.Frequency((3, known), (2, random));
        }

        /// <summary>
        /// Gets a generator of cases of the receive fuzz.
        /// </summary>
        public static Gen<ReceiveCase> ReceiveCase()
        {
            var frames = Gen.Frequency(
                (3, Gen.Constant(System.Array.Empty<byte>())),
                (5, RawFrame()),
                (2, Gen.ListOf(RawFrame()).Select(list => list.SelectMany(frame => frame).ToArray())));
            var answer = Gen.Frequency(
                (4, Gen.Constant(System.Array.Empty<string>())),
                (5, Gen.ListOf(Gen.Frequency((6, Answer()), (2, Mutated()), (1, Message()))).Select(lines => lines.ToArray())));
            return from unsolicited in frames from lines in answer select new ReceiveCase(unsolicited, lines);
        }

        /// <summary>Gets the JSON text of an escaped string.</summary>
        public static string Quote(string text)
        {
            var quoted = new StringBuilder(text.Length + 2).Append('"');
            foreach (var character in text)
            {
                if (character is '"' or '\\' || character < 0x20 || character > 0x7E)
                {
                    // Every UTF-16 unit on its own, so that half of a surrogate pair is written too (which a serializer refuses to).
                    quoted.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    quoted.Append(character);
                }
            }

            return quoted.Append('"').ToString();
        }

        /// <summary>Makes a line that is sent without being asked for: the id of an answer is one that no command has.</summary>
        public static string Unsolicited(string line) => line.Replace(IdToken, "99999", StringComparison.Ordinal);

        /// <summary>Takes the line breaks out of a text, so that it stays one line of a message.</summary>
        public static string NoBreaks(string text) => text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

        /// <summary>Cuts a text short for the message of a failure, and shows what is not printable.</summary>
        public static string Preview(string text)
        {
            return Quote(text.Length > 300 ? string.Concat(text.AsSpan(0, 300), "...") : text);
        }

        /// <summary>Makes text that is valid UTF-16 (a lone half of a pair becomes a replacement character) and has no line break.</summary>
        public static string Clean(string text)
        {
            var clean = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    clean.Append(character).Append(text[index + 1]);
                    index++;
                }
                else if (char.IsSurrogate(character))
                {
                    clean.Append('\uFFFD');
                }
                else if (character is '\r' or '\n')
                {
                    clean.Append(' ');
                }
                else
                {
                    clean.Append(character);
                }
            }

            return clean.ToString();
        }

        /// <summary>Gives the text as a JSON writer writes it: a lone half of a surrogate pair is a replacement character.</summary>
        public static string ReplaceLoneSurrogates(string text)
        {
            var result = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    result.Append(character).Append(text[index + 1]);
                    index++;
                }
                else
                {
                    result.Append(char.IsSurrogate(character) ? '\uFFFD' : character);
                }
            }

            return result.ToString();
        }

        private static string Mutate(string text, IEnumerable<(int Kind, int Position, int Code)> edits)
        {
            var builder = new StringBuilder(text);
            foreach (var (kind, position, code) in edits)
            {
                if (builder.Length == 0)
                {
                    builder.Append((char)code);
                    continue;
                }

                var at = position % builder.Length;
                switch (kind)
                {
                    case 0:
                        builder.Remove(at, 1);
                        break;
                    case 1:
                        builder.Insert(at, (char)code);
                        break;
                    case 2:
                        builder[at] = (char)code;
                        break;
                    case 3:
                        builder.Length = at;
                        break;
                    default:
                        builder.Insert(at, builder.ToString(at, Math.Min(10, builder.Length - at)));
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
