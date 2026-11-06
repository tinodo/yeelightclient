namespace Yeelight.TestApp.Services
{
    using System;
    using System.Reflection;

    /// <summary>The version of the library, as the About page shows it.</summary>
    internal static class VersionInfo
    {
        /// <summary>Splits the informational version of an assembly into the version and the build that it was made from.</summary>
        /// <param name="informationalVersion">For example <c>1.0.0-beta.1+3d3c42e5aac5ba805825da76410c181273ba90b1</c>.</param>
        /// <returns>The version (<c>1.0.0-beta.1</c>) and the short form of the build (<c>3d3c42e</c>), which is empty when there is none.</returns>
        public static (string Version, string Build) Split(string? informationalVersion)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion))
            {
                return ("unknown", string.Empty);
            }

            var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            if (plus < 0)
            {
                return (informationalVersion.Trim(), string.Empty);
            }

            var build = informationalVersion[(plus + 1)..].Trim();
            return (informationalVersion[..plus].Trim(), build.Length > 7 ? build[..7] : build);
        }

        /// <summary>Gets the version of the library that this app is made with, from the informational version of its assembly.</summary>
        /// <returns>The version and the build.</returns>
        public static (string Version, string Build) OfLibrary()
        {
            return OfAssembly(typeof(YeelightClient).Assembly);
        }

        /// <summary>Gets the version of an assembly, from its informational version.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <returns>The version and the build.</returns>
        public static (string Version, string Build) OfAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return Split(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString());
        }
    }
}
