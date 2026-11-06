namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Yeelight.TestApp.Models;

    /// <summary>
    /// How the properties of a light are shown to a person: the words for a state, the colour that a light shows (as a swatch), the
    /// time since it was last heard. It is plain code, so that it can be tested without a screen.
    /// </summary>
    internal static class LightFormat
    {
        /// <summary>Gets the colour that a light shows, according to its <see cref="YeelightLight.ColorMode"/>.</summary>
        /// <param name="light">The light.</param>
        /// <returns>The colour of its <see cref="YeelightLight.Rgb"/>, of its colour temperature, or of its hue and saturation at full value.</returns>
        public static Rgb24 Swatch(YeelightLight light)
        {
            ArgumentNullException.ThrowIfNull(light);
            return light.ColorMode switch
            {
                ColorMode.ColorTemperature => KelvinToRgb(light.ColorTemperature),
                ColorMode.Hsv => HsvToRgb(light.Hue, light.Saturation, 100),
                _ => Rgb24.FromValue(light.Rgb),
            };
        }

        /// <summary>Makes an approximation of the colour of white light of a colour temperature (the curve of Tanner Helland), good enough for a swatch.</summary>
        /// <param name="kelvin">The colour temperature; a value outside 1000 to 40000 is brought into it.</param>
        /// <returns>The colour.</returns>
        public static Rgb24 KelvinToRgb(int kelvin)
        {
            var temperature = Math.Clamp(kelvin, 1000, 40000) / 100.0;
            double red;
            double green;
            double blue;
            if (temperature <= 66)
            {
                red = 255;
                green = (99.4708025861 * Math.Log(temperature)) - 161.1195681661;
            }
            else
            {
                red = 329.698727446 * Math.Pow(temperature - 60, -0.1332047592);
                green = 288.1221695283 * Math.Pow(temperature - 60, -0.0755148492);
            }

            if (temperature >= 66)
            {
                blue = 255;
            }
            else if (temperature <= 19)
            {
                blue = 0;
            }
            else
            {
                blue = (138.5177312231 * Math.Log(temperature - 10)) - 305.0447927307;
            }

            return new Rgb24(Channel(red), Channel(green), Channel(blue));
        }

        /// <summary>Converts a colour of hue, saturation and value to red, green and blue.</summary>
        /// <param name="hue">The hue, in degrees (0 to 359; a value outside it wraps around).</param>
        /// <param name="saturation">The saturation, a percentage.</param>
        /// <param name="value">The value (the brightness of the colour itself), a percentage.</param>
        /// <returns>The colour.</returns>
        public static Rgb24 HsvToRgb(int hue, int saturation, int value)
        {
            var h = (((hue % 360) + 360) % 360) / 60.0;
            var s = Math.Clamp(saturation, 0, 100) / 100.0;
            var v = Math.Clamp(value, 0, 100) / 100.0;
            var sector = (int)Math.Floor(h);
            var fraction = h - sector;
            var p = v * (1 - s);
            var q = v * (1 - (s * fraction));
            var t = v * (1 - (s * (1 - fraction)));
            var (red, green, blue) = sector switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };
            return new Rgb24(Channel(red * 255), Channel(green * 255), Channel(blue * 255));
        }

        /// <summary>Converts red, green and blue to a hue, a saturation and a value.</summary>
        /// <param name="color">The colour.</param>
        /// <returns>The hue in degrees (0 to 359), and the saturation and the value as percentages.</returns>
        public static (int Hue, int Saturation, int Value) RgbToHsv(Rgb24 color)
        {
            var r = color.R / 255.0;
            var g = color.G / 255.0;
            var b = color.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            double hue;
            if (delta == 0)
            {
                hue = 0;
            }
            else if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }

            if (hue < 0)
            {
                hue += 360;
            }

            var saturation = max == 0 ? 0 : delta / max;
            return (Math.Min(359, (int)Math.Round(hue)), (int)Math.Round(saturation * 100), (int)Math.Round(max * 100));
        }

        /// <summary>Says how long ago a light was heard from, the way a person says it.</summary>
        /// <param name="lastSeen">When the light was last heard from.</param>
        /// <param name="now">The time now.</param>
        /// <returns>For example <c>just now</c>, <c>12 s ago</c> or <c>3 min ago</c>.</returns>
        public static string LastSeen(DateTimeOffset lastSeen, DateTimeOffset now)
        {
            var age = now - lastSeen;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            return age.TotalSeconds switch
            {
                < 2 => "just now",
                < 60 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalSeconds} s ago"),
                < 3600 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} min ago"),
                < 86400 => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} h ago"),
                _ => lastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>Gets the name of a colour mode for a person.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The words.</returns>
        public static string ColorModeText(ColorMode mode)
        {
            return mode switch
            {
                ColorMode.Rgb => "Colour (RGB)",
                ColorMode.ColorTemperature => "Colour temperature",
                ColorMode.Hsv => "Colour (HSV)",
                _ => mode.ToString(),
            };
        }

        /// <summary>Writes the colour of a light as text: <c>#FF8C00 (16747520)</c>.</summary>
        /// <param name="rgb">The colour as the protocol says it.</param>
        /// <returns>The text.</returns>
        public static string RgbText(int rgb)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Rgb24.FromValue(rgb).Hex} ({rgb})");
        }

        /// <summary>Writes a length of time in minutes, as the sleep timer of a light has it.</summary>
        /// <param name="remaining">The time that is left, or zero when there is no timer.</param>
        /// <returns>The words.</returns>
        public static string TimerText(TimeSpan remaining)
        {
            return remaining <= TimeSpan.Zero
                ? "no timer"
                : string.Create(CultureInfo.InvariantCulture, $"{(int)remaining.TotalMinutes} min left");
        }

        /// <summary>Gets what a property of a light is now, as text, for the events: the name is that of the property of the library that changed.</summary>
        /// <param name="light">The light (a device, or the background light of a device).</param>
        /// <param name="propertyName">The name of the property, for example <c>Brightness</c>.</param>
        /// <returns>The value; the text <c>(not shown)</c> for a name that this does not know.</returns>
        public static string ValueOf(YeelightLight light, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(light);
            ArgumentNullException.ThrowIfNull(propertyName);
            var inv = CultureInfo.InvariantCulture;
            switch (propertyName)
            {
                case nameof(YeelightLight.IsOn):
                    return light.IsOn ? "on" : "off";
                case nameof(YeelightLight.Brightness):
                    return string.Create(inv, $"{light.Brightness}%");
                case nameof(YeelightLight.ColorMode):
                    return light.ColorMode.ToString();
                case nameof(YeelightLight.ColorTemperature):
                    return string.Create(inv, $"{light.ColorTemperature} K");
                case nameof(YeelightLight.Rgb):
                    return RgbText(light.Rgb);
                case nameof(YeelightLight.Color):
                    return Rgb24.FromValue(light.Rgb).Hex;
                case nameof(YeelightLight.Hue):
                    return string.Create(inv, $"{light.Hue}\u00b0");
                case nameof(YeelightLight.Saturation):
                    return string.Create(inv, $"{light.Saturation}%");
                case nameof(YeelightLight.IsFlowing):
                    return light.IsFlowing ? "flowing" : "not flowing";
                case nameof(YeelightLight.FlowParameters):
                    return light.FlowParameters.Length == 0 ? "(none)" : light.FlowParameters;
            }

            if (light is not YeelightDevice device)
            {
                return "(not shown)";
            }

            return propertyName switch
            {
                nameof(YeelightDevice.IsConnected) => device.IsConnected ? "connected" : "not connected",
                nameof(YeelightDevice.Name) => device.Name.Length == 0 ? "(no name)" : device.Name,
                nameof(YeelightDevice.Model) => device.Model,
                nameof(YeelightDevice.FirmwareVersion) => device.FirmwareVersion,
                nameof(YeelightDevice.Location) => device.Location,
                nameof(YeelightDevice.Address) => device.Address?.ToString() ?? "(none)",
                nameof(YeelightDevice.Port) => device.Port.ToString(inv),
                nameof(YeelightDevice.SupportedMethods) => string.Create(inv, $"{device.SupportedMethods.Count} methods"),
                nameof(YeelightDevice.SleepTimerRemaining) => TimerText(device.SleepTimerRemaining),
                nameof(YeelightDevice.IsMusicModeOn) => device.IsMusicModeOn ? "music mode on" : "music mode off",
                nameof(YeelightDevice.NightLightBrightness) => string.Create(inv, $"{device.NightLightBrightness}%"),
                nameof(YeelightDevice.ActiveMode) => device.ActiveMode?.ToString() ?? "(none)",
                _ => "(not shown)",
            };
        }

        /// <summary>Lists the properties of a device, and of its background light when it has one, for the details of the Lights page.</summary>
        /// <param name="device">The device.</param>
        /// <param name="now">The time now, to say how long ago the device was heard from.</param>
        /// <returns>The rows, the identity first, then the state.</returns>
        public static IReadOnlyList<PropertyRow> Properties(YeelightDevice device, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(device);
            var inv = CultureInfo.InvariantCulture;
            var rows = new List<PropertyRow>
            {
                new("Name", device.Name.Length == 0 ? "(none: the name is set with Set name)" : device.Name),
                new("Model", Known(device.Model)),
                new("Id", device.Id),
                new("Firmware version", Known(device.FirmwareVersion)),
                new("Location", device.Location),
                new("Address", device.Address is { } address ? string.Create(inv, $"{address}:{device.Port}") : "(the location holds no IP address)"),
                new("Connection", device.IsConnected ? "connected" : "not connected"),
                new("Last seen", string.Create(inv, $"{LastSeen(device.LastSeen, now)} ({device.LastSeen.ToLocalTime():HH:mm:ss})")),
            };
            AddState(rows, device, string.Empty);
            rows.Add(new PropertyRow("Sleep timer", TimerText(device.SleepTimerRemaining)));
            rows.Add(new PropertyRow("Music mode", device.IsMusicModeOn ? "on" : "off"));
            rows.Add(new PropertyRow("Night light brightness", device.NightLightBrightness == 0 ? "(not reported)" : string.Create(inv, $"{device.NightLightBrightness}%")));
            rows.Add(new PropertyRow("Active mode", device.ActiveMode?.ToString() ?? "(not reported)"));
            if (device.SupportsBackgroundLight)
            {
                AddState(rows, device.Background, "Background ");
            }

            return rows;
        }

        /// <summary>Lists what a light shows now, for the live state of the Control page: the power, the brightness, the colour mode, the colour, and the rest.</summary>
        /// <param name="light">The light: a device, or the background light of a device.</param>
        /// <param name="prefix">What the names start with, for example <c>Background </c>.</param>
        /// <returns>The rows.</returns>
        public static IReadOnlyList<PropertyRow> StateRows(YeelightLight light, string prefix = "")
        {
            ArgumentNullException.ThrowIfNull(light);
            var rows = new List<PropertyRow>();
            AddState(rows, light, prefix ?? string.Empty);
            return rows;
        }

        private static void AddState(List<PropertyRow> rows, YeelightLight light, string prefix)
        {
            var inv = CultureInfo.InvariantCulture;
            rows.Add(new PropertyRow(prefix + "Power", light.IsOn ? "on" : "off"));
            rows.Add(new PropertyRow(prefix + "Brightness", light.Brightness == 0 ? "(not reported)" : string.Create(inv, $"{light.Brightness}%")));
            rows.Add(new PropertyRow(prefix + "Colour mode", ColorModeText(light.ColorMode)));
            rows.Add(new PropertyRow(prefix + "RGB", RgbText(light.Rgb)));
            rows.Add(new PropertyRow(prefix + "Colour temperature", light.ColorTemperature == 0 ? "(not reported)" : string.Create(inv, $"{light.ColorTemperature} K")));
            rows.Add(new PropertyRow(prefix + "Hue and saturation", string.Create(inv, $"{light.Hue}\u00b0, {light.Saturation}%")));
            rows.Add(new PropertyRow(prefix + "Flowing", light.IsFlowing ? "yes" : "no"));
            if (light.FlowParameters.Length > 0)
            {
                rows.Add(new PropertyRow(prefix + "Flow parameters", light.FlowParameters));
            }
        }

        private static string Known(string text) => text.Length == 0 ? "(not reported)" : text;

        private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }
}
