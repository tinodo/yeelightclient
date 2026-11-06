namespace Yeelight.TestApp.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One method of the control protocol of a Yeelight light, as the Raw page offers it.</summary>
    /// <param name="Name">The name of the method, as it is sent (<c>set_power</c>).</param>
    /// <param name="Group">What the method is about, to group the table.</param>
    /// <param name="Parameters">The parameters in order, in a few words.</param>
    /// <param name="Summary">What the method does, in a sentence.</param>
    /// <param name="Examples">Parameters that work, as JSON arrays; the first one is what the Raw page fills in.</param>
    /// <param name="NeedsPower">Whether only a light that is on accepts it.</param>
    internal sealed record ProtocolMethod(string Name, string Group, string Parameters, string Summary, IReadOnlyList<string> Examples, bool NeedsPower = false)
    {
        /// <summary>Gets the example that the Raw page fills in.</summary>
        public string Example => this.Examples[0];

        /// <summary>Gets whether this is a method of the background light.</summary>
        public bool IsBackground => this.Name.StartsWith("bg_", StringComparison.Ordinal);

        /// <summary>Gets the notes of the method on one line: only on a light that is on, or nothing.</summary>
        public string Notes => this.NeedsPower ? "Only while the light is on." : string.Empty;
    }

    /// <summary>One property that a light reports (<c>get_prop</c> and the <c>props</c> notification).</summary>
    /// <param name="Name">The name of the property, as it is sent.</param>
    /// <param name="Meaning">What it is, and what values it takes, in a sentence.</param>
    internal sealed record ProtocolProperty(string Name, string Meaning);

    /// <summary>
    /// The methods and the properties of the control protocol, as a table for the Raw page: what each method takes, a small example that
    /// works, and what it needs. The words are the app's own summary of the Yeelight WiFi Light Inter-Operation Specification (table 4-1
    /// for the methods, table 4-2 for the properties): https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf.
    /// </summary>
    internal static class ProtocolCatalog
    {
        /// <summary>Gets the methods of the main light and of the device, then those of the background light.</summary>
        public static IReadOnlyList<ProtocolMethod> Methods { get; } = Build();

        /// <summary>Gets the properties that a light reports.</summary>
        public static IReadOnlyList<ProtocolProperty> Properties { get; } =
        [
            new("power", "Whether the main light is on: on or off."),
            new("bright", "The brightness of the main light, a percentage from 1 to 100."),
            new("ct", "The colour temperature of the main light in kelvin (1700 to 6500 on most lights); only what is shown when the colour mode is 2."),
            new("rgb", "The colour of the main light as one number (red times 65536, plus green times 256, plus blue); only what is shown when the colour mode is 1."),
            new("hue", "The hue of the main light in degrees (0 to 359); only what is shown when the colour mode is 3."),
            new("sat", "The saturation of the main light, a percentage (0 to 100); only what is shown when the colour mode is 3."),
            new("color_mode", "Which of the colour properties is what the main light shows: 1 is rgb, 2 is ct, 3 is hue and sat."),
            new("flowing", "Whether a colour flow is running on the main light: 1 or 0."),
            new("delayoff", "The minutes that are left of the sleep timer, 0 when there is none."),
            new("flow_params", "The colour flow that is running: the count, the action and the steps, as the text that start_cf took."),
            new("music_on", "Whether the light is in music mode: 1 or 0."),
            new("name", "The name that was stored on the light with set_name; empty when there is none."),
            new("bg_power", "Whether the background light is on: on or off."),
            new("bg_flowing", "Whether a colour flow is running on the background light: 1 or 0."),
            new("bg_flow_params", "The colour flow that is running on the background light."),
            new("bg_ct", "The colour temperature of the background light in kelvin."),
            new("bg_lmode", "The colour mode of the background light: 1 is rgb, 2 is ct, 3 is hue and sat."),
            new("bg_bright", "The brightness of the background light, a percentage from 1 to 100."),
            new("bg_rgb", "The colour of the background light as one number."),
            new("bg_hue", "The hue of the background light in degrees."),
            new("bg_sat", "The saturation of the background light, a percentage."),
            new("nl_br", "The brightness of the night light mode of a ceiling light, a percentage."),
            new("active_mode", "The mode of a ceiling light: 0 is daylight, 1 is the moonlight (night light)."),
        ];

        /// <summary>Gets the names of the methods, for a list to choose from.</summary>
        public static IReadOnlyList<string> MethodNames { get; } = [.. Methods.Select(method => method.Name)];

        /// <summary>Gets the names of the properties, for <c>get_prop</c>.</summary>
        public static IReadOnlyList<string> PropertyNames { get; } = [.. Properties.Select(property => property.Name)];

        /// <summary>Finds a method.</summary>
        /// <param name="name">The name of the method, as it is sent.</param>
        /// <returns>The method, or <c>null</c> when the protocol has none by that name.</returns>
        public static ProtocolMethod? Find(string? name)
        {
            return name is null ? null : Methods.FirstOrDefault(method => string.Equals(method.Name, name.Trim(), StringComparison.Ordinal));
        }

        private static List<ProtocolMethod> Build()
        {
            const string Effect = "effect (sudden or smooth), duration (ms, 30 or more, only for smooth)";
            var main = new List<ProtocolMethod>
            {
                new("get_prop", "Read", "one or more property names", "Reads properties. The answer has one value per name, in the same order, and an empty text for a name that the light does not know.", ["[\"power\", \"bright\", \"ct\"]"]),

                new("set_power", "Power", "power (on or off), " + Effect + ", mode (optional, 0 to 5)", "Switches the light on or off. The optional mode switches on in a mode: 0 normal, 1 colour temperature, 2 colour, 3 hue and saturation, 4 colour flow, 5 night light.", ["[\"on\", \"smooth\", 500]", "[\"off\", \"sudden\", 0]", "[\"on\", \"smooth\", 500, 2]"]),
                new("toggle", "Power", "none", "Switches the light off when it is on, and on when it is off.", ["[]"]),
                new("dev_toggle", "Power", "none", "Does the same as toggle for the main light and the background light together.", ["[]"]),
                new("set_default", "Power", "none", "Saves the state that the light is in now, as the state that it starts in when it gets power.", ["[]"], NeedsPower: true),

                new("set_bright", "Colour and brightness", "brightness (1 to 100), " + Effect, "Sets the brightness, a percentage.", ["[50, \"smooth\", 500]"], NeedsPower: true),
                new("set_ct_abx", "Colour and brightness", "colour temperature (1700 to 6500 K), " + Effect, "Shows white light of a colour temperature, and puts the light in colour temperature mode.", ["[4000, \"smooth\", 500]"], NeedsPower: true),
                new("set_rgb", "Colour and brightness", "colour (0 to 16777215), " + Effect, "Shows a colour, given as red times 65536 plus green times 256 plus blue.", ["[16711680, \"smooth\", 500]"], NeedsPower: true),
                new("set_hsv", "Colour and brightness", "hue (0 to 359), saturation (0 to 100), " + Effect, "Shows a colour given by its hue and saturation.", ["[120, 100, \"smooth\", 500]"], NeedsPower: true),
                new("set_adjust", "Colour and brightness", "action (increase, decrease or circle), property (bright, ct or color)", "Takes one step in a property without knowing what it is now. The colour only takes circle.", ["[\"increase\", \"bright\"]", "[\"circle\", \"color\"]"]),
                new("adjust_bright", "Colour and brightness", "percentage (-100 to 100), duration (ms)", "Changes the brightness by a percentage of what it is now.", ["[-20, 500]"]),
                new("adjust_ct", "Colour and brightness", "percentage (-100 to 100), duration (ms)", "Changes the colour temperature by a percentage of what it is now.", ["[20, 500]"]),
                new("adjust_color", "Colour and brightness", "percentage (-100 to 100), duration (ms)", "Moves the colour on; the light ignores the percentage.", ["[20, 500]"]),

                new("start_cf", "Effects", "count (0 is for ever), action (0 recover, 1 stay, 2 turn off), flow (text)", "Starts a colour flow. The flow is text of steps, four numbers each: duration in ms, mode (1 colour, 2 colour temperature, 7 sleep), value, brightness.", ["[4, 0, \"1000,2,2700,100,500,1,255,10\"]"], NeedsPower: true),
                new("stop_cf", "Effects", "none", "Stops the colour flow that is running.", ["[]"]),
                new("set_scene", "Effects", "class (color, hsv, ct, cf or auto_delay_off), and the values of that class", "Sets a state directly, switching the light on first when it is off: colour and brightness, hue and saturation and brightness, colour temperature and brightness, a colour flow, or a brightness with a time after which the light switches off.", ["[\"hsv\", 300, 70, 100]", "[\"color\", 65280, 70]", "[\"ct\", 5400, 100]", "[\"cf\", 0, 0, \"500,2,5000,1\"]", "[\"auto_delay_off\", 50, 5]"]),

                new("cron_add", "Timer", "type (0, switch off), minutes", "Starts the sleep timer: the light switches off after the minutes. A light has one.", ["[0, 15]"], NeedsPower: true),
                new("cron_get", "Timer", "type (0)", "Reads the sleep timer: the answer has the minutes, or nothing when there is no timer.", ["[0]"]),
                new("cron_del", "Timer", "type (0)", "Stops the sleep timer.", ["[0]"]),

                new("set_music", "Device", "action (1 on, 0 off), host, port", "Puts the light in music mode: it connects to the host and the port, and takes commands from that connection without a limit and without answering. Off ends it.", ["[0]", "[1, \"192.168.1.20\", 54321]"]),
                new("set_name", "Device", "name (64 bytes at most)", "Stores a name on the light.", ["[\"my_bulb\"]"]),
            };

            var background = new List<ProtocolMethod>();
            foreach (var method in main.Where(method => method.Name is not ("get_prop" or "dev_toggle" or "set_music" or "set_name" or "cron_add" or "cron_get" or "cron_del")))
            {
                background.Add(method with
                {
                    Name = "bg_" + method.Name,
                    Group = "Background light",
                    Summary = $"The same as {method.Name}, for the background light.",
                });
            }

            var all = new List<ProtocolMethod>(main);
            all.AddRange(background);
            return all;
        }
    }
}
