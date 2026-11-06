namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// C# embeds enum constants in consuming assemblies, so renumbering a published enum silently breaks already-compiled consumers after a
    /// library-only upgrade. These tests pin the names and the numeric values of every public enum as they shipped in 1.0.0; new members must
    /// be appended (or given explicit values), never inserted. Most of them are numbers of the Yeelight Inter-Operation Specification
    /// (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) that go over the wire as they are: the mode of set_power, the action
    /// and the modes of a flow of start_cf, color_mode and active_mode; the others (the direction and the property of set_adjust, which go
    /// over the wire as text, and the failures of the client) are numbered by their order, which is just as binding.
    /// </summary>
    public class EnumStabilityTests
    {
        // The enum, and its members with their values: the one place that says what 1.0.0 shipped.
        private static readonly (string Enum, (string Name, int Value)[] Members)[] Shipped =
        [
            ("ActiveMode", [("Daylight", 0), ("Moonlight", 1)]),
            ("AdjustAction", [("Increase", 0), ("Decrease", 1), ("Circle", 2)]),
            ("AdjustProperty", [("Brightness", 0), ("ColorTemperature", 1), ("Color", 2)]),
            ("ColorFlowAction", [("RecoverState", 0), ("Stay", 1), ("TurnOff", 2)]),
            ("ColorFlowMode", [("Rgb", 1), ("ColorTemperature", 2), ("Sleep", 7)]),
            ("ColorMode", [("Rgb", 1), ("ColorTemperature", 2), ("Hsv", 3)]),
            ("PowerOnMode", [("Normal", 0), ("ColorTemperature", 1), ("Rgb", 2), ("Hsv", 3), ("ColorFlow", 4), ("NightLight", 5)]),
            ("YeelightFailureKind", [("DeviceRejected", 0), ("NotConnected", 1), ("Timeout", 2), ("RateLimited", 3), ("ConnectionLost", 4)]),
        ];

        public static IEnumerable<object[]> Members()
        {
            return Shipped.SelectMany(item => item.Members.Select(member => new object[] { item.Enum, member.Name, member.Value }));
        }

        private static Type Find(string name)
        {
            return typeof(YeelightClient).Assembly.GetType("Yeelight." + name, throwOnError: true)!;
        }

        [Theory]
        [MemberData(nameof(Members))]
        public void PublishedValues_AreStable(string enumName, string member, int expected)
        {
            var type = Find(enumName);

            Assert.True(Enum.IsDefined(type, member), $"{enumName} has no member {member}.");
            Assert.Equal(expected, Convert.ToInt32(Enum.Parse(type, member), System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void EveryEnum_HasExactlyTheMembersThatShipped()
        {
            foreach (var (name, members) in Shipped)
            {
                var type = Find(name);

                Assert.True(type.IsEnum && type.IsPublic, $"{name} is not a public enum.");
                Assert.Equal(members.Select(member => member.Name).Order(), Enum.GetNames(type).Order());
                Assert.Equal(typeof(int), Enum.GetUnderlyingType(type));
            }
        }

        [Fact]
        public void EveryPublicEnum_IsPinnedHere()
        {
            var published = typeof(YeelightClient).Assembly.GetExportedTypes().Where(type => type.IsEnum).Select(type => type.Name).Order().ToList();

            Assert.Equal(Shipped.Select(item => item.Enum).Order(), published);
        }

        [Fact]
        public void NoEnum_IsAFlagsEnum()
        {
            Assert.All(Shipped, item => Assert.False(Find(item.Enum).IsDefined(typeof(FlagsAttribute), inherit: false), $"{item.Enum} is a flags enum."));
        }
    }
}
