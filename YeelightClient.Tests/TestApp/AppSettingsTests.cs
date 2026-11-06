namespace Yeelight.Tests.TestApp
{
    using System;
    using System.IO;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>
    /// The settings of the test app: what they default to, how they are saved and read, what is refused, and how they become the options
    /// of a client. A test never touches the settings of the person who runs it: every file is in a folder of its own.
    /// </summary>
    [Collection("The persistence flag of the test app settings")]
    public sealed class AppSettingsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "yeelight-testapp-settings-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(this._folder))
            {
                Directory.Delete(this._folder, recursive: true);
            }
        }

        [Fact]
        public void TheDefaults_AreThoseOfTheClient()
        {
            var settings = new AppSettings();
            var options = settings.ToClientOptions();

            Assert.Equal(YeelightClientOptions.Default.AutoConnect, options.AutoConnect);
            Assert.Equal(YeelightClientOptions.Default.RefreshOnConnect, options.RefreshOnConnect);
            Assert.Equal(YeelightClientOptions.Default.CommandTimeout, options.CommandTimeout);
            Assert.Equal(YeelightClientOptions.Default.ConnectTimeout, options.ConnectTimeout);
            Assert.Equal(YeelightClientOptions.Default.KeepAliveInterval, options.KeepAliveInterval);
            Assert.Equal(YeelightClientOptions.Default.CommandsPerMinute, options.CommandsPerMinute);
            Assert.Equal(YeelightClientOptions.Default.UseBroadcast, options.UseBroadcast);
            Assert.Equal(YeelightClientOptions.Default.SearchBurst, options.SearchBurst);
            Assert.Equal(YeelightClientOptions.Default.DiscoveryPort, options.DiscoveryPort);
            Assert.Empty(settings.Validate());
        }

        [Fact]
        public void EverySettingOfTheClient_ReachesItsOptions()
        {
            var settings = new AppSettings
            {
                AutoConnect = false,
                RefreshOnConnect = false,
                CommandTimeoutSeconds = 1.5,
                ConnectTimeoutSeconds = 2.5,
                KeepAliveSeconds = 0,
                CommandsPerMinute = 45,
                UseBroadcast = false,
                SearchBurst = 5,
                DiscoveryPort = 19820,
            };

            var options = settings.ToClientOptions();

            Assert.False(options.AutoConnect);
            Assert.False(options.RefreshOnConnect);
            Assert.Equal(TimeSpan.FromSeconds(1.5), options.CommandTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2.5), options.ConnectTimeout);
            Assert.Equal(TimeSpan.Zero, options.KeepAliveInterval);
            Assert.Equal(45, options.CommandsPerMinute);
            Assert.False(options.UseBroadcast);
            Assert.Equal(5, options.SearchBurst);
            Assert.Equal(19820, options.DiscoveryPort);

            // The client accepts what the settings accept.
            using var client = new YeelightClient(options);
            Assert.Equal(45, client.Options.CommandsPerMinute);
        }

        [Fact]
        public void TheSettings_SurviveASaveAndALoad()
        {
            var path = Path.Combine(this._folder, "settings.json");
            var settings = new AppSettings
            {
                CommandsPerMinute = 30,
                Theme = AppTheme.Dark,
                SearchIntervalSeconds = 90,
                ProbeAddress = "192.168.1.50",
                TransitionEnabled = true,
                TransitionMilliseconds = 800,
                LastLightId = "0x000000000015243f",
            };

            Assert.True(settings.Save(path));
            var loaded = AppSettings.Load(path);

            Assert.Equal(30, loaded.CommandsPerMinute);
            Assert.Equal(AppTheme.Dark, loaded.Theme);
            Assert.Equal(90, loaded.SearchIntervalSeconds);
            Assert.Equal("192.168.1.50", loaded.ProbeAddress);
            Assert.True(loaded.TransitionEnabled);
            Assert.Equal(800, loaded.TransitionMilliseconds);
            Assert.Equal("0x000000000015243f", loaded.LastLightId);
            Assert.Contains("\"Dark\"", File.ReadAllText(path));
        }

        [Fact]
        public void AMissingFile_GivesTheDefaults()
        {
            var loaded = AppSettings.Load(Path.Combine(this._folder, "nothing-here.json"));

            Assert.Equal(new AppSettings().CommandsPerMinute, loaded.CommandsPerMinute);
            Assert.Equal(AppTheme.System, loaded.Theme);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{ \"Theme\": \"Purple\" }")]
        [InlineData("")]
        public void ADamagedFile_GivesTheDefaults(string content)
        {
            Directory.CreateDirectory(this._folder);
            var path = Path.Combine(this._folder, "damaged.json");
            File.WriteAllText(path, content);

            var loaded = AppSettings.Load(path);

            Assert.Equal(AppTheme.System, loaded.Theme);
            Assert.Empty(loaded.Validate());
        }

        [Fact]
        public void ValuesThatCannotBeUsed_AreReplacedByTheirDefaults_WhenTheFileIsRead()
        {
            Directory.CreateDirectory(this._folder);
            var path = Path.Combine(this._folder, "edited.json");
            File.WriteAllText(
                path,
                "{ \"CommandTimeoutSeconds\": -1, \"ConnectTimeoutSeconds\": 0, \"KeepAliveSeconds\": -5, \"CommandsPerMinute\": 0, \"SearchBurst\": -2, \"DiscoveryPort\": 70000, \"SearchIntervalSeconds\": 0, \"TransitionMilliseconds\": 5, \"Theme\": 42 }");

            var loaded = AppSettings.Load(path);

            Assert.Empty(loaded.Validate());
            Assert.Equal(YeelightClientOptions.Default.CommandTimeout, TimeSpan.FromSeconds(loaded.CommandTimeoutSeconds));
            Assert.Equal(YeelightClientOptions.Default.CommandsPerMinute, loaded.CommandsPerMinute);
            Assert.Equal(YeelightClientOptions.Default.DiscoveryPort, loaded.DiscoveryPort);
            Assert.True(loaded.TransitionMilliseconds >= (int)YeelightLimits.MinTransition.TotalMilliseconds);
            Assert.Equal(AppTheme.System, loaded.Theme);
            using var client = new YeelightClient(loaded.ToClientOptions()); // the client takes them
        }

        [Theory]
        [InlineData(nameof(AppSettings.CommandTimeoutSeconds), 0.0)]
        [InlineData(nameof(AppSettings.CommandTimeoutSeconds), double.NaN)]
        [InlineData(nameof(AppSettings.ConnectTimeoutSeconds), -1.0)]
        [InlineData(nameof(AppSettings.KeepAliveSeconds), -0.5)]
        [InlineData(nameof(AppSettings.CommandsPerMinute), 0.0)]
        [InlineData(nameof(AppSettings.SearchBurst), 0.0)]
        [InlineData(nameof(AppSettings.DiscoveryPort), 0.0)]
        [InlineData(nameof(AppSettings.DiscoveryPort), 65536.0)]
        public void ASettingThatTheClientRefuses_IsReportedInWordsBeforeTheClientIsMade(string name, double value)
        {
            var settings = new AppSettings();
            switch (name)
            {
                case nameof(AppSettings.CommandTimeoutSeconds):
                    settings.CommandTimeoutSeconds = value;
                    break;
                case nameof(AppSettings.ConnectTimeoutSeconds):
                    settings.ConnectTimeoutSeconds = value;
                    break;
                case nameof(AppSettings.KeepAliveSeconds):
                    settings.KeepAliveSeconds = value;
                    break;
                case nameof(AppSettings.CommandsPerMinute):
                    settings.CommandsPerMinute = (int)value;
                    break;
                case nameof(AppSettings.SearchBurst):
                    settings.SearchBurst = (int)value;
                    break;
                default:
                    settings.DiscoveryPort = (int)value;
                    break;
            }

            var problems = settings.Validate();

            Assert.Single(problems);
        }

        [Fact]
        public void KeepAliveOfZero_TurnsItOff_AndIsAccepted()
        {
            var settings = new AppSettings { KeepAliveSeconds = 0 };

            Assert.Empty(settings.Validate());
            Assert.Equal(TimeSpan.Zero, settings.ToClientOptions().KeepAliveInterval);
        }

        [Fact]
        public void Clone_MakesAnIndependentCopy()
        {
            var original = new AppSettings { CommandsPerMinute = 20 };

            var copy = original.Clone();
            copy.CommandsPerMinute = 99;

            Assert.Equal(20, original.CommandsPerMinute);
            Assert.Equal(99, copy.CommandsPerMinute);
        }

        [Fact]
        public void WhenPersistenceIsOff_NothingIsReadFromOrWrittenToTheSettingsOfTheUser()
        {
            var before = AppSettings.Persist;
            try
            {
                AppSettings.Persist = false;

                var loaded = AppSettings.Load();

                Assert.Equal(AppTheme.System, loaded.Theme);
                Assert.False(new AppSettings { CommandsPerMinute = 7 }.Save());
            }
            finally
            {
                AppSettings.Persist = before;
            }
        }

        [Fact]
        public void TheExportFolder_IsInTheDocumentsOfTheUser_AndTheSettingsAreInTheirOwnFolder()
        {
            Assert.EndsWith("YeelightClient Test App", AppSettings.ExportFolder, StringComparison.Ordinal);
            Assert.EndsWith("YeelightClient.TestApp", AppSettings.Folder, StringComparison.Ordinal);
            Assert.NotEqual(AppSettings.ExportFolder, AppSettings.Folder);
        }

        [Fact]
        public void Describe_NamesEverySettingOfTheClient()
        {
            var text = new AppSettings().Describe();

            foreach (var name in new[] { "AutoConnect", "RefreshOnConnect", "CommandTimeout", "ConnectTimeout", "KeepAliveInterval", "CommandsPerMinute", "UseBroadcast", "SearchBurst", "DiscoveryPort" })
            {
                Assert.Contains(name, text, StringComparison.Ordinal);
            }
        }
    }
}
