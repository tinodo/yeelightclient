namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Reflection.PortableExecutable;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// The package is one thing: the library. It asks for nothing besides the framework and the logging abstractions, it carries nothing
    /// that is not code (no embedded file), and it says in its metadata what the trimmer and the native AOT compiler may do with it
    /// (tools/verify-package.py checks the package itself in CI).
    /// </summary>
    public class PackagingTests
    {
        private static readonly Assembly Library = typeof(YeelightClient).Assembly;

        // A run that measures coverage (coverlet) adds a class to the library, in a namespace of its own, that finds its own assembly by
        // reflection to count the lines that run. That is the tool and not the library: it is left out where the library is looked at.
        private const string InstrumentationNamespace = "Coverlet.Core.Instrumentation.Tracker";

        private static readonly bool Instrumented = Library.GetTypes().Any(type => type.Namespace == InstrumentationNamespace);

        private static readonly string[] InstrumentationNeeds = ["System.Reflection.Assembly"];

        // What reflects on types at run time, builds code at run time, or serializes with what it finds by reflection: nothing that a
        // trimmer or a native AOT compiler can follow. The library only uses what they can see.
        private static readonly string[] ForbiddenForAot =
        [
            "System.Activator",
            "System.Reflection.Assembly",
            "System.Reflection.ConstructorInfo",
            "System.Reflection.FieldInfo",
            "System.Reflection.MethodBase",
            "System.Reflection.MethodInfo",
            "System.Reflection.PropertyInfo",
            "System.Reflection.Emit",
            "System.Linq.Expressions",
            "System.Text.Json.JsonSerializer",
            "System.Text.Json.Serialization",
            "System.Runtime.Serialization",
            "System.Xml",
        ];

        [Fact]
        public void TheLibrary_CarriesNoEmbeddedFiles()
        {
            Assert.Empty(Library.GetManifestResourceNames());
        }

        [Fact]
        public void TheLibrary_ReferencesNothingBesidesTheFrameworkAndTheMicrosoftExtensions()
        {
            var unexpected = Library.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                    && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                    && name is not ("mscorlib" or "netstandard"))
                .ToList();

            Assert.Empty(unexpected);
        }

        [Fact]
        public void TheLibrary_ReferencesNoWindowsOnlyPartOfTheFramework()
        {
            var windows = Library.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => name.StartsWith("System.Windows", StringComparison.Ordinal) || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal) || name is "WindowsBase" or "System.Drawing.Common")
                .ToList();

            Assert.Empty(windows);
        }

        [Fact]
        public void TheLibrary_AsksForTheOldestLoggingAbstractionsThatItCanWorkWith()
        {
            // A library that asks for a newer version makes every application that uses it update too (and breaks the restore of one that pins
            // an older one): 8.0.0 is the oldest that has what the client uses.
            var logging = Library.GetReferencedAssemblies().Single(reference => reference.Name == "Microsoft.Extensions.Logging.Abstractions");

            Assert.True(logging.Version <= new Version(8, 0, 0, 0), $"The library references Microsoft.Extensions.Logging.Abstractions {logging.Version}.");
        }

        [Fact]
        public void TheLibrary_UsesNothingThatATrimmerOrNativeAotCannotFollow()
        {
            using var stream = File.OpenRead(Library.Location);
            using var image = new PEReader(stream);
            var reader = image.GetMetadataReader();
            var referenced = reader.TypeReferences
                .Select(handle => reader.GetTypeReference(handle))
                .Select(reference => $"{reader.GetString(reference.Namespace)}.{reader.GetString(reference.Name)}")
                .Distinct()
                .ToList();

            var found = referenced
                .Where(type => ForbiddenForAot.Any(forbidden => type == forbidden || type.StartsWith(forbidden + ".", StringComparison.Ordinal)))
                .Where(type => !(Instrumented && InstrumentationNeeds.Contains(type)))
                .Order()
                .ToList();

            Assert.NotEmpty(referenced);
            Assert.Empty(found);
        }

        [Fact]
        public void TheLibrary_SaysThatItCanBeTrimmed()
        {
            var metadata = Library.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

            Assert.Equal("True", metadata["IsTrimmable"]);
#if NET10_0_OR_GREATER
            Assert.Equal("True", metadata["IsAotCompatible"]);
#endif
        }

        [Fact]
        public void TheLibrary_IsClsCompliant()
        {
            var attribute = Library.GetCustomAttribute<CLSCompliantAttribute>();

            Assert.NotNull(attribute);
            Assert.True(attribute.IsCompliant);
        }

        [Fact]
        public void TheLibrary_IsNamedAfterThePackage_AndEveryTypeOfItIsInOneNamespace()
        {
            var outside = Library.GetTypes()
                .Where(type => !type.Name.StartsWith('<') && !(type.DeclaringType?.Name.StartsWith('<') ?? false) && type.Namespace != "Yeelight" && type.Namespace != InstrumentationNamespace)
                .Select(type => type.FullName)
                .ToList();

            Assert.Equal("YeelightClient", Library.GetName().Name);
            Assert.Empty(outside);
        }

        [Fact]
        public void TheInternalsOfTheLibrary_AreShownToTheTestsAndToNobodyElse()
        {
            var friends = Library.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName).ToList();

            Assert.Equal(["YeelightClient.Tests"], friends);
        }
    }
}
