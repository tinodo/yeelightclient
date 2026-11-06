namespace Yeelight.Tests.Unit
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// The settings of a client: what the defaults are, and which values are refused when a client is made with them (an option that cannot be
    /// used is refused at once, with an <see cref="ArgumentOutOfRangeException"/> that names it, and not by every command that comes later).
    /// </summary>
    public class YeelightClientOptionsTests
    {
        private static void Construct(YeelightClientOptions options)
        {
            using var client = new YeelightClient(options);

            Assert.Same(options, client.Options);
        }

        private static ArgumentOutOfRangeException Refused(YeelightClientOptions options)
        {
            return Assert.Throws<ArgumentOutOfRangeException>(() => new YeelightClient(options));
        }

        [Fact]
        public void TheDefaults_AreThoseOfTheDocumentation()
        {
            var options = new YeelightClientOptions();

            Assert.True(options.AutoConnect);
            Assert.True(options.RefreshOnConnect);
            Assert.Equal(TimeSpan.FromSeconds(3), options.CommandTimeout);
            Assert.Equal(TimeSpan.FromSeconds(3), options.ConnectTimeout);
            Assert.Equal(TimeSpan.FromSeconds(60), options.KeepAliveInterval);
            Assert.Equal(60, options.CommandsPerMinute);
            Assert.True(options.UseBroadcast);
            Assert.Equal(3, options.SearchBurst);
            Assert.Equal(1982, options.DiscoveryPort);
            Assert.Equal(1000, options.MaxDevices);
        }

        [Fact]
        public void TheOptions_AreExactlyThoseThatAreCoveredHere()
        {
            // A new option has to be given a default (and its limits) in these tests on purpose: a default that changes silently changes the
            // behaviour of every application.
            var names = typeof(YeelightClientOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).Order();

            Assert.Equal(
                new[] { "AutoConnect", "CommandTimeout", "CommandsPerMinute", "ConnectTimeout", "DiscoveryPort", "KeepAliveInterval", "MaxDevices", "RefreshOnConnect", "SearchBurst", "UseBroadcast" }.Order(),
                names);
        }

        [Fact]
        public void TheDefaultOptions_AreOneObject_WithTheDefaults()
        {
            Assert.Same(YeelightClientOptions.Default, YeelightClientOptions.Default);
            Assert.Equal(new YeelightClientOptions().CommandTimeout, YeelightClientOptions.Default.CommandTimeout);
            Assert.Equal(new YeelightClientOptions().MaxDevices, YeelightClientOptions.Default.MaxDevices);
            Assert.True(YeelightClientOptions.Default.AutoConnect);
        }

        [Fact]
        public void EveryOption_CanBeSetWhenTheOptionsAreMade()
        {
            var options = new YeelightClientOptions
            {
                AutoConnect = false,
                RefreshOnConnect = false,
                CommandTimeout = TimeSpan.FromSeconds(7),
                ConnectTimeout = TimeSpan.FromSeconds(8),
                KeepAliveInterval = TimeSpan.Zero,
                CommandsPerMinute = 30,
                UseBroadcast = false,
                SearchBurst = 5,
                DiscoveryPort = 12345,
                MaxDevices = 5,
            };

            Assert.False(options.AutoConnect);
            Assert.False(options.RefreshOnConnect);
            Assert.Equal(TimeSpan.FromSeconds(7), options.CommandTimeout);
            Assert.Equal(TimeSpan.FromSeconds(8), options.ConnectTimeout);
            Assert.Equal(TimeSpan.Zero, options.KeepAliveInterval);
            Assert.Equal(30, options.CommandsPerMinute);
            Assert.False(options.UseBroadcast);
            Assert.Equal(5, options.SearchBurst);
            Assert.Equal(12345, options.DiscoveryPort);
            Assert.Equal(5, options.MaxDevices);
        }

        [Fact]
        public void AClientWithoutOptions_UsesTheDefaultOptions()
        {
            using var none = new YeelightClient();
            using var explicitNone = new YeelightClient(null);

            Assert.Same(YeelightClientOptions.Default, none.Options);
            Assert.Same(YeelightClientOptions.Default, explicitNone.Options);
        }

        [Fact]
        public void TheDefaults_AreAccepted()
        {
            Construct(new YeelightClientOptions());
        }

        [Fact]
        public void TheSmallestValuesThatAreAccepted_AreAccepted()
        {
            Construct(new YeelightClientOptions { CommandTimeout = TimeSpan.FromTicks(1) });
            Construct(new YeelightClientOptions { ConnectTimeout = TimeSpan.FromTicks(1) });
            Construct(new YeelightClientOptions { KeepAliveInterval = TimeSpan.Zero });
            Construct(new YeelightClientOptions { CommandsPerMinute = 1 });
            Construct(new YeelightClientOptions { SearchBurst = 1 });
            Construct(new YeelightClientOptions { DiscoveryPort = 1 });
            Construct(new YeelightClientOptions { MaxDevices = 1 });
        }

        [Fact]
        public void TheLargestPortThatIsAccepted_IsAccepted()
        {
            Construct(new YeelightClientOptions { DiscoveryPort = 65535 });
        }

        [Fact]
        public void LargeButUsableValues_AreAccepted()
        {
            Construct(new YeelightClientOptions
            {
                CommandTimeout = TimeSpan.FromHours(1),
                ConnectTimeout = TimeSpan.FromHours(1),
                KeepAliveInterval = TimeSpan.FromHours(1),
                CommandsPerMinute = 600_000,
                SearchBurst = 100,
                MaxDevices = int.MaxValue,
            });
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1000)]
        public void ACommandTimeoutThatIsNotMoreThanZero_IsRefused(int milliseconds)
        {
            var refusal = Refused(new YeelightClientOptions { CommandTimeout = TimeSpan.FromMilliseconds(milliseconds) });

            Assert.Equal("CommandTimeout", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1000)]
        public void AConnectTimeoutThatIsNotMoreThanZero_IsRefused(int milliseconds)
        {
            var refusal = Refused(new YeelightClientOptions { ConnectTimeout = TimeSpan.FromMilliseconds(milliseconds) });

            Assert.Equal("ConnectTimeout", refusal.ParamName);
        }

        [Fact]
        public void TheInfiniteTimespan_IsNotATimeoutThatIsAccepted()
        {
            Assert.Equal("CommandTimeout", Refused(new YeelightClientOptions { CommandTimeout = Timeout.InfiniteTimeSpan }).ParamName);
            Assert.Equal("ConnectTimeout", Refused(new YeelightClientOptions { ConnectTimeout = Timeout.InfiniteTimeSpan }).ParamName);
            Assert.Equal("KeepAliveInterval", Refused(new YeelightClientOptions { KeepAliveInterval = Timeout.InfiniteTimeSpan }).ParamName);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-60_000)]
        public void ANegativeKeepAliveInterval_IsRefused(int milliseconds)
        {
            var refusal = Refused(new YeelightClientOptions { KeepAliveInterval = TimeSpan.FromMilliseconds(milliseconds) });

            Assert.Equal("KeepAliveInterval", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void LessThanOneCommandAMinute_IsRefused(int perMinute)
        {
            var refusal = Refused(new YeelightClientOptions { CommandsPerMinute = perMinute });

            Assert.Equal("CommandsPerMinute", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void LessThanOneSearch_IsRefused(int burst)
        {
            var refusal = Refused(new YeelightClientOptions { SearchBurst = burst });

            Assert.Equal("SearchBurst", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        [InlineData(100_000)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void APortThatIsNotAPort_IsRefused(int port)
        {
            var refusal = Refused(new YeelightClientOptions { DiscoveryPort = port });

            Assert.Equal("DiscoveryPort", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void LessThanOneDevice_IsRefused(int maxDevices)
        {
            var refusal = Refused(new YeelightClientOptions { MaxDevices = maxDevices });

            Assert.Equal("MaxDevices", refusal.ParamName);
        }

        [Fact]
        public void ATimeoutThatNothingCanWaitFor_IsRefusedWhenTheClientIsMade()
        {
            // "An option has a value that cannot be used" is refused by the constructor of the client. A time that is longer than the longest
            // that a timer of the framework can wait (about 49 days) cannot be used: the first command, or the first connect, would
            // throw an ArgumentOutOfRangeException of the framework from somewhere inside, again and again.
            TimeSpan[] tooLong = [TimeSpan.MaxValue, TimeSpan.FromDays(100), TimeSpan.FromDays(50)];
            var problems = new System.Collections.Generic.List<string>();
            foreach (var time in tooLong)
            {
                foreach (var (name, options) in new (string, YeelightClientOptions)[]
                {
                    ("CommandTimeout", new YeelightClientOptions { CommandTimeout = time }),
                    ("ConnectTimeout", new YeelightClientOptions { ConnectTimeout = time }),
                })
                {
                    try
                    {
                        using var client = new YeelightClient(options);
                        problems.Add($"{name} of {time} was accepted");
                    }
                    catch (ArgumentOutOfRangeException ex) when (ex.ParamName == name)
                    {
                        // Refused, and by name.
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }
    }
}
