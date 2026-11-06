namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Yeelight.TestApp.Services;
    using Xunit;

    /// <summary>The version of the library, as the Help page shows it.</summary>
    public sealed class VersionInfoTests
    {
        [Theory]
        [InlineData(null, "unknown", "")]
        [InlineData("", "unknown", "")]
        [InlineData("   ", "unknown", "")]
        [InlineData("1.0.0", "1.0.0", "")]
        [InlineData(" 1.0.0-beta.1 ", "1.0.0-beta.1", "")]
        [InlineData("1.0.0-beta.1+3d3c42e5aac5ba805825da76410c181273ba90b1", "1.0.0-beta.1", "3d3c42e")]
        [InlineData("2.1.0+abc", "2.1.0", "abc")]
        [InlineData("2.1.0+", "2.1.0", "")]
        public void TheInformationalVersion_IsSplitIntoTheVersionAndTheShortBuild(string? informational, string version, string build)
        {
            Assert.Equal((version, build), VersionInfo.Split(informational));
        }

        [Fact]
        public void TheVersionOfTheLibrary_IsThatOfItsAssembly()
        {
            var informational = typeof(YeelightClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            var (version, _) = VersionInfo.OfLibrary();

            Assert.Equal(VersionInfo.Split(informational).Version, version);
            Assert.NotEqual("unknown", version);
            Assert.True(char.IsDigit(version[0]), "a version starts with a number");
        }

        [Fact]
        public void AnAssemblyWithoutAnInformationalVersion_GetsItsAssemblyVersion()
        {
            var (version, build) = VersionInfo.OfAssembly(typeof(VersionInfoTests).Assembly);

            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.True(build.Length <= 7);
        }

        [Fact]
        public void ANullAssembly_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => VersionInfo.OfAssembly(null!));
        }
    }
}
