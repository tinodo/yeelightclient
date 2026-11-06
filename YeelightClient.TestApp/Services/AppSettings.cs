namespace Yeelight.TestApp.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>The theme of the app: what the system uses, or one of the two themes of Windows.</summary>
    internal enum AppTheme
    {
        /// <summary>The theme that Windows uses.</summary>
        System,

        /// <summary>The light theme.</summary>
        Light,

        /// <summary>The dark theme.</summary>
        Dark,
    }

    /// <summary>
    /// What the app remembers between runs: the settings of the client (a change is applied by making a new client), the theme and the
    /// last values that were typed. Nothing in it is secret: a light has no password.
    /// </summary>
    internal sealed class AppSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Gets or sets whether the client connects to a light as soon as it is found.</summary>
        public bool AutoConnect { get; set; } = YeelightClientOptions.Default.AutoConnect;

        /// <summary>Gets or sets whether the client reads all the properties of a light each time that it connects.</summary>
        public bool RefreshOnConnect { get; set; } = YeelightClientOptions.Default.RefreshOnConnect;

        /// <summary>Gets or sets how long a command waits for the answer of the light, in seconds.</summary>
        public double CommandTimeoutSeconds { get; set; } = YeelightClientOptions.Default.CommandTimeout.TotalSeconds;

        /// <summary>Gets or sets how long the client waits for a connection to be made, in seconds.</summary>
        public double ConnectTimeoutSeconds { get; set; } = YeelightClientOptions.Default.ConnectTimeout.TotalSeconds;

        /// <summary>Gets or sets how long a connection may be quiet before the client asks the light something, in seconds (0 turns it off).</summary>
        public double KeepAliveSeconds { get; set; } = YeelightClientOptions.Default.KeepAliveInterval.TotalSeconds;

        /// <summary>Gets or sets the number of commands a minute that the client sends to a light on one connection.</summary>
        public int CommandsPerMinute { get; set; } = YeelightClientOptions.Default.CommandsPerMinute;

        /// <summary>Gets or sets whether a search is also sent as a broadcast.</summary>
        public bool UseBroadcast { get; set; } = YeelightClientOptions.Default.UseBroadcast;

        /// <summary>Gets or sets how many times a search is sent when it is due.</summary>
        public int SearchBurst { get; set; } = YeelightClientOptions.Default.SearchBurst;

        /// <summary>Gets or sets the UDP port that lights are searched on.</summary>
        public int DiscoveryPort { get; set; } = YeelightClientOptions.Default.DiscoveryPort;

        /// <summary>Gets or sets the theme of the app.</summary>
        public AppTheme Theme { get; set; }

        /// <summary>Gets or sets the number of seconds between two searches when the app searches by itself (Lights page).</summary>
        public int SearchIntervalSeconds { get; set; } = 30;

        /// <summary>Gets or sets the address that was probed last.</summary>
        public string ProbeAddress { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the commands of the Control page change smoothly, over <see cref="TransitionMilliseconds"/>.</summary>
        public bool TransitionEnabled { get; set; }

        /// <summary>Gets or sets the length of a smooth change, in milliseconds.</summary>
        public int TransitionMilliseconds { get; set; } = 500;

        /// <summary>Gets or sets the id of the light that was selected last.</summary>
        public string? LastLightId { get; set; }

        /// <summary>Gets or sets a value indicating whether the settings are read and written; the smoke test must not touch the settings of the user.</summary>
        public static bool Persist { get; set; } = true;

        /// <summary>Gets the folder where the app keeps its settings.</summary>
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YeelightClient.TestApp");

        /// <summary>Gets or sets a folder that replaces <see cref="ExportFolder"/>; the smoke test writes its exports next to its log, not in the documents of the user.</summary>
        public static string? ExportFolderOverride { get; set; }

        /// <summary>Gets the folder where the app writes its exports, where the user can find them.</summary>
        public static string ExportFolder => ExportFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YeelightClient Test App");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        /// <summary>Reads the settings; a missing or damaged file gives the defaults.</summary>
        /// <param name="path">The file to read, or <c>null</c> for the settings of the user (which are not read when <see cref="Persist"/> is off).</param>
        /// <returns>The settings, with every value that cannot be used replaced by its default.</returns>
        public static AppSettings Load(string? path = null)
        {
            if (path is null && !Persist)
            {
                return new AppSettings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? FilePath), JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                return new AppSettings();
            }
        }

        /// <summary>Writes the settings; a failure is not worth bothering the user with.</summary>
        /// <param name="path">The file to write, or <c>null</c> for the settings of the user (which are not written when <see cref="Persist"/> is off).</param>
        /// <returns><c>true</c> when the file was written.</returns>
        public bool Save(string? path = null)
        {
            if (path is null && !Persist)
            {
                return false;
            }

            try
            {
                var target = path ?? FilePath;
                var folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(target, JsonSerializer.Serialize(this, JsonOptions));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The settings are a convenience.
                return false;
            }
        }

        /// <summary>Makes a copy.</summary>
        /// <returns>A new object with the same values.</returns>
        public AppSettings Clone() => (AppSettings)this.MemberwiseClone();

        /// <summary>Makes the options of a client of these settings.</summary>
        /// <returns>The options. The client validates them, see <see cref="Validate"/> for the same checks in words.</returns>
        public YeelightClientOptions ToClientOptions()
        {
            return new YeelightClientOptions
            {
                AutoConnect = this.AutoConnect,
                RefreshOnConnect = this.RefreshOnConnect,
                CommandTimeout = TimeSpan.FromSeconds(this.CommandTimeoutSeconds),
                ConnectTimeout = TimeSpan.FromSeconds(this.ConnectTimeoutSeconds),
                KeepAliveInterval = TimeSpan.FromSeconds(this.KeepAliveSeconds),
                CommandsPerMinute = this.CommandsPerMinute,
                UseBroadcast = this.UseBroadcast,
                SearchBurst = this.SearchBurst,
                DiscoveryPort = this.DiscoveryPort,
            };
        }

        /// <summary>Checks the settings of the client the way the client does, in words that say what to correct.</summary>
        /// <returns>What is wrong; nothing when the settings can be used.</returns>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            if (!IsNumber(this.CommandTimeoutSeconds) || this.CommandTimeoutSeconds <= 0)
            {
                problems.Add("The command timeout is a number of seconds that is more than zero.");
            }

            if (!IsNumber(this.ConnectTimeoutSeconds) || this.ConnectTimeoutSeconds <= 0)
            {
                problems.Add("The connect timeout is a number of seconds that is more than zero.");
            }

            if (!IsNumber(this.KeepAliveSeconds) || this.KeepAliveSeconds < 0)
            {
                problems.Add("The keep-alive interval is a number of seconds that is not negative (0 turns it off).");
            }

            if (this.CommandsPerMinute < 1)
            {
                problems.Add("Commands a minute is at least 1.");
            }

            if (this.SearchBurst < 1)
            {
                problems.Add("The search burst is at least 1.");
            }

            if (this.DiscoveryPort is < 1 or > 65535)
            {
                problems.Add("The discovery port is a number from 1 to 65535 (a light uses 1982).");
            }

            return problems;
        }

        /// <summary>Replaces every value that cannot be used (a file can be edited by hand) by its default.</summary>
        public void Normalize()
        {
            var defaults = new AppSettings();
            if (!IsNumber(this.CommandTimeoutSeconds) || this.CommandTimeoutSeconds <= 0)
            {
                this.CommandTimeoutSeconds = defaults.CommandTimeoutSeconds;
            }

            if (!IsNumber(this.ConnectTimeoutSeconds) || this.ConnectTimeoutSeconds <= 0)
            {
                this.ConnectTimeoutSeconds = defaults.ConnectTimeoutSeconds;
            }

            if (!IsNumber(this.KeepAliveSeconds) || this.KeepAliveSeconds < 0)
            {
                this.KeepAliveSeconds = defaults.KeepAliveSeconds;
            }

            if (this.CommandsPerMinute < 1)
            {
                this.CommandsPerMinute = defaults.CommandsPerMinute;
            }

            if (this.SearchBurst < 1)
            {
                this.SearchBurst = defaults.SearchBurst;
            }

            if (this.DiscoveryPort is < 1 or > 65535)
            {
                this.DiscoveryPort = defaults.DiscoveryPort;
            }

            if (this.SearchIntervalSeconds is < 1 or > 86_400)
            {
                this.SearchIntervalSeconds = defaults.SearchIntervalSeconds;
            }

            if (this.TransitionMilliseconds < (int)YeelightLimits.MinTransition.TotalMilliseconds || this.TransitionMilliseconds > 600_000)
            {
                this.TransitionMilliseconds = defaults.TransitionMilliseconds;
            }

            if (!Enum.IsDefined(this.Theme))
            {
                this.Theme = AppTheme.System;
            }

            this.ProbeAddress ??= string.Empty;
        }

        /// <summary>Describes the settings of the client on one line, for the log.</summary>
        /// <returns>The line.</returns>
        public string Describe()
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"AutoConnect={this.AutoConnect}, RefreshOnConnect={this.RefreshOnConnect}, CommandTimeout={this.CommandTimeoutSeconds}s, ConnectTimeout={this.ConnectTimeoutSeconds}s, KeepAliveInterval={this.KeepAliveSeconds}s, CommandsPerMinute={this.CommandsPerMinute}, UseBroadcast={this.UseBroadcast}, SearchBurst={this.SearchBurst}, DiscoveryPort={this.DiscoveryPort}");
        }

        private static bool IsNumber(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value < TimeSpan.MaxValue.TotalSeconds / 2;
    }
}
