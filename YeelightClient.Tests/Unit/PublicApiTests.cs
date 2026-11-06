namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The shape of the public API of 1.0.0. A public type cannot be made internal, and a class that can be derived from cannot be sealed,
    /// without breaking the code of someone; the other way round costs nobody anything. So the API starts closed, and these tests keep it
    /// from opening by accident: what is made public, or left open for derived classes, has to be added here on purpose.
    /// </summary>
    public class PublicApiTests
    {
        private static readonly Assembly Library = typeof(YeelightClient).Assembly;

        // Every type of the API, by name: a new public type has to be added here on purpose.
        private static readonly string[] ExpectedTypes =
        [
            "Yeelight.ActiveMode",
            "Yeelight.AdjustAction",
            "Yeelight.AdjustProperty",
            "Yeelight.ColorFlow",
            "Yeelight.ColorFlowAction",
            "Yeelight.ColorFlowMode",
            "Yeelight.ColorFlowStep",
            "Yeelight.ColorMode",
            "Yeelight.DeviceDiscoveredEventArgs",
            "Yeelight.DeviceStateChangedEventArgs",
            "Yeelight.PowerOnMode",
            "Yeelight.YeelightBackgroundLight",
            "Yeelight.YeelightClient",
            "Yeelight.YeelightClientOptions",
            "Yeelight.YeelightDevice",
            "Yeelight.YeelightError",
            "Yeelight.YeelightException",
            "Yeelight.YeelightFailureKind",
            "Yeelight.YeelightLight",
            "Yeelight.YeelightLimits",
            "Yeelight.YeelightMusicSession",
            "Yeelight.YeelightResult",
            "Yeelight.YeelightScene",
        ];

        private static IEnumerable<Type> PublicTypes => Library.GetExportedTypes();

        [Fact]
        public void TheExportedTypes_AreExactlyThoseThatTheApiNames()
        {
            var exported = PublicTypes.Select(type => type.FullName!).Order().ToList();

            Assert.Equal(ExpectedTypes.Order(), exported);
        }

        [Fact]
        public void TheInternals_AreNotPublic()
        {
            // What the client and a light exchange, how the messages are made and read, the search for the lights, the connection to one and
            // the events: an application gets what they carry through YeelightClient and its lights, and has no use for the parts.
            string[] internals =
            [
                "YeelightMessage", "YeelightResponse", "YeelightNotification", "YeelightProtocol", "YeelightLineReader", "YeelightRateLimiter",
                "DeviceAnnouncement", "YeelightLocation", "Validate", "LightState", "YeelightProperties", "YeelightConnection",
                "YeelightConnectionOptions", "YeelightDiscovery", "YeelightDiscoveryOptions", "EventDispatcher", "DeviceContext",
            ];

            var exposed = PublicTypes.Select(type => type.Name).Intersect(internals).Order().ToList();

            Assert.Empty(exposed);
        }

        [Fact]
        public void NoNestedTypeIsPublic()
        {
            var nested = Library.GetTypes().Where(type => type.IsNested && type.IsVisible).Select(type => type.FullName).ToList();

            Assert.Empty(nested);
        }

        [Fact]
        public void TheOnlyPublicInterfaces_AreNone()
        {
            var interfaces = PublicTypes.Where(type => type.IsInterface).Select(type => type.FullName).ToList();

            Assert.Empty(interfaces);
        }

        [Fact]
        public void TheApi_HasNoStructsAndNoDelegates()
        {
            var structs = PublicTypes.Where(type => type.IsValueType && !type.IsEnum).Select(type => type.FullName).ToList();
            var delegates = PublicTypes.Where(type => typeof(Delegate).IsAssignableFrom(type)).Select(type => type.FullName).ToList();

            Assert.Empty(structs);
            Assert.Empty(delegates);
        }

        [Fact]
        public void EveryPublicClass_IsSealed_ExceptTheLightAndTheException()
        {
            // YeelightLight is what the device and its background light are (abstract, and it cannot be derived from outside: see the next
            // test). An exception can be derived from by the code that catches it, as it can in the framework.
            var open = PublicTypes
                .Where(type => type.IsClass && !type.IsSealed)
                .Select(type => type.FullName)
                .Order()
                .ToList();

            Assert.Equal(["Yeelight.YeelightException", "Yeelight.YeelightLight"], open);
        }

        [Fact]
        public void TheLight_IsAbstract_AndCannotBeDerivedFromOutsideTheLibrary()
        {
            var constructors = typeof(YeelightLight).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.True(typeof(YeelightLight).IsAbstract);
            Assert.NotEmpty(constructors);
            Assert.All(constructors, constructor => Assert.True(constructor.IsAssembly || constructor.IsPrivate, $"A constructor of YeelightLight is {(constructor.IsPublic ? "public" : "protected")}."));
        }

        [Fact]
        public void TheLight_IsDerivedFromByTheDeviceAndItsBackgroundLightAndNothingElse()
        {
            var derived = Library.GetTypes().Where(type => type != typeof(YeelightLight) && typeof(YeelightLight).IsAssignableFrom(type)).Select(type => type.Name).Order();

            Assert.Equal(["YeelightBackgroundLight", "YeelightDevice"], derived);
        }

        [Fact]
        public void TheApiOfALight_HasNoMemberThatADerivedClassOutsideTheLibraryCouldUse()
        {
            var open = typeof(YeelightLight)
                .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(member => member is MethodBase { IsFamily: true } or MethodBase { IsFamilyOrAssembly: true }
                    or FieldInfo { IsFamily: true } or FieldInfo { IsFamilyOrAssembly: true }
                    or PropertyInfo { GetMethod.IsFamily: true } or PropertyInfo { GetMethod.IsFamilyOrAssembly: true })
                .Select(member => member.Name)
                .ToList();

            Assert.Empty(open);
        }

        [Fact]
        public void TheLimits_AreAStaticClassOfConstants()
        {
            var limits = typeof(YeelightLimits);

            Assert.True(limits.IsAbstract && limits.IsSealed);
            Assert.Empty(limits.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        [Fact]
        public void OnlyTheseClasses_CanBeMadeByAnApplication()
        {
            // A light and its parts are found by the client, and a result or an event is made by it. (A record has a public constructor,
            // so the error is one that can be made.)
            string[] makeable = ["ColorFlow", "YeelightClient", "YeelightClientOptions", "YeelightError", "YeelightException"];

            var made = PublicTypes
                .Where(type => type.IsClass && !type.IsAbstract && type.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length > 0)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            Assert.Equal(makeable.Order(StringComparer.Ordinal), made);
        }

        [Fact]
        public void EveryPublicType_IsUsedByTheApi_ExceptTheClientTheExceptionAndTheLimits()
        {
            // A type is public because something in the API takes it, returns it, holds it, raises it or derives from it. One that nothing
            // uses is dead, and it cannot be removed after 1.0.0 without breaking the code of someone: it goes now, or it is internal. The
            // exception is left out because it is thrown, and the limits because they are numbers to compare with.
            var used = new HashSet<Type>();
            foreach (var type in PublicTypes)
            {
                var related = new List<Type>();
                if (type.BaseType is not null)
                {
                    related.Add(type.BaseType);
                }

                related.AddRange(type.GetInterfaces());
                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    switch (member)
                    {
                        case MethodBase { IsPublic: true } or MethodBase { IsFamily: true } or MethodBase { IsFamilyOrAssembly: true }:
                            var method = (MethodBase)member;
                            related.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
                            if (method is MethodInfo info)
                            {
                                related.Add(info.ReturnType);
                            }

                            break;
                        case PropertyInfo property when property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true:
                            related.Add(property.PropertyType);
                            break;
                        case FieldInfo { IsPublic: true } field:
                            related.Add(field.FieldType);
                            break;
                        case EventInfo { AddMethod.IsPublic: true } @event:
                            related.Add(@event.EventHandlerType!);
                            break;
                    }
                }

                foreach (var other in related.SelectMany(TypesIn).Where(other => other != type))
                {
                    used.Add(other);
                }
            }

            var unused = PublicTypes
                .Where(type => type != typeof(YeelightClient) && type != typeof(YeelightException) && type != typeof(YeelightLimits) && !used.Contains(type))
                .Select(type => type.FullName)
                .Order()
                .ToList();

            Assert.True(unused.Count == 0, "Public types that nothing in the API uses (remove them, or make them internal): " + string.Join(", ", unused) + ".");
        }

        [Fact]
        public void EveryCommandOfALight_TakesACancellationTokenLast_ThatIsOptional()
        {
            var commands = typeof(YeelightLight).Assembly.GetExportedTypes()
                .Where(type => typeof(YeelightLight).IsAssignableFrom(type) || type == typeof(YeelightMusicSession))
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(method => !method.IsSpecialName && typeof(Task).IsAssignableFrom(method.ReturnType) && method.Name != "DisconnectAsync")
                .ToList();

            Assert.NotEmpty(commands);
            var problems = commands
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 0 || parameters[^1].ParameterType != typeof(CancellationToken) || !parameters[^1].HasDefaultValue;
                })
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
                .ToList();

            Assert.True(problems.Count == 0, "Without an optional CancellationToken as the last argument: " + string.Join(", ", problems));
        }

        [Fact]
        public void EveryMethodThatReturnsATask_IsNamedAsync_AndTheOtherWayRound()
        {
            var methods = PublicTypes
                .Where(type => !type.IsEnum)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(method => !method.IsSpecialName)
                .ToList();

            var returnsATask = methods.Where(method => typeof(Task).IsAssignableFrom(method.ReturnType) || method.ReturnType == typeof(ValueTask)).ToList();
            var problems = returnsATask.Where(method => !method.Name.EndsWith("Async", StringComparison.Ordinal))
                .Concat(methods.Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal) && !returnsATask.Contains(method)))
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
                .ToList();

            Assert.True(problems.Count == 0, "Naming of asynchronous methods: " + string.Join(", ", problems));
        }

        [Fact]
        public void TheApi_HasNoPublicSetters_OnlyInit()
        {
            var settable = PublicTypes
                .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(property => property.SetMethod is { IsPublic: true } setter
                    && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
                .ToList();

            Assert.Empty(settable);
        }

        [Fact]
        public void TheOptions_AreSetWhenTheyAreMade_AndNotChangedAfterwards()
        {
            var properties = typeof(YeelightClientOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.NotEmpty(properties);
            Assert.All(properties, property =>
            {
                Assert.NotNull(property.SetMethod);
                Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
            });
        }

        [Fact]
        public void TheEventsOfTheClient_FollowTheConventionsOfTheFramework()
        {
            var events = typeof(YeelightClient).GetEvents(BindingFlags.Instance | BindingFlags.Public).OrderBy(item => item.Name).ToList();

            Assert.Equal(["DeviceDiscovered", "DeviceStateChanged"], events.Select(item => item.Name));
            Assert.Equal(typeof(EventHandler<DeviceDiscoveredEventArgs>), events[0].EventHandlerType);
            Assert.Equal(typeof(EventHandler<DeviceStateChangedEventArgs>), events[1].EventHandlerType);
            Assert.True(typeof(EventArgs).IsAssignableFrom(typeof(DeviceDiscoveredEventArgs)));
            Assert.True(typeof(EventArgs).IsAssignableFrom(typeof(DeviceStateChangedEventArgs)));
        }

        [Fact]
        public void ALight_IsObservable_AndTheClientIsDisposable()
        {
            Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(YeelightLight)));
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(YeelightClient)));
            Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(YeelightClient)));
            Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(YeelightMusicSession)));
            Assert.Equal(["PropertyChanged"], typeof(YeelightLight).GetEvents(BindingFlags.Instance | BindingFlags.Public).Select(item => item.Name));
        }

        [Fact]
        public void TheOnlyThingsADeviceAddsToItsLight_AreWhatConcernsTheDeviceAsAWhole()
        {
            // The commands of a light are on YeelightLight, so that the background light has the same ones: the device only adds its own.
            var added = typeof(YeelightDevice).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            string[] expected =
            [
                "CancelSleepTimerAsync", "ConnectAsync", "DisconnectAsync", "GetPropertiesAsync", "GetSleepTimerAsync", "RefreshAsync", "SetNameAsync",
                "SetSleepTimerAsync", "StartMusicModeAsync", "Supports", "ToString", "ToggleAllAsync",
            ];

            var ownMethods = typeof(YeelightBackgroundLight).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName).ToList();

            Assert.Equal(expected.Order(StringComparer.Ordinal), added);
            Assert.Empty(ownMethods);
        }

        // The type itself, and every type that it is made of: the element of an array, the arguments of a generic type.
        private static IEnumerable<Type> TypesIn(Type type)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType()!;
            }

            yield return type;
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    foreach (var inner in TypesIn(argument))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }
}
