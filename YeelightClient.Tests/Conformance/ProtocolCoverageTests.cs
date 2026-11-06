namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The library covers the protocol: every one of the 35 methods of table 4-1 of the specification
    /// (https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) is sent by a typed member of the API, the list below says which,
    /// and a sweep that calls every typed command once against a simulated light shows that each of them is sent (and that nothing is sent
    /// that the table does not have). A command that is added to the API has to be added to the sweep, or a test here fails.
    /// </summary>
    public class ProtocolCoverageTests
    {
        private static readonly string[] NotCommands = [nameof(YeelightDevice.ConnectAsync), nameof(YeelightDevice.DisconnectAsync)];

        /// <summary>Every method of table 4-1, the type that has the member that sends it, and that member.</summary>
        private static readonly (string Method, string Receiver, string Member)[] Map =
        [
            ("get_prop", nameof(YeelightDevice), nameof(YeelightDevice.GetPropertiesAsync)),
            ("set_ct_abx", nameof(YeelightLight), nameof(YeelightLight.SetColorTemperatureAsync)),
            ("set_rgb", nameof(YeelightLight), nameof(YeelightLight.SetRgbAsync)),
            ("set_hsv", nameof(YeelightLight), nameof(YeelightLight.SetHsvAsync)),
            ("set_bright", nameof(YeelightLight), nameof(YeelightLight.SetBrightnessAsync)),
            ("set_power", nameof(YeelightLight), nameof(YeelightLight.SetPowerAsync)),
            ("toggle", nameof(YeelightLight), nameof(YeelightLight.ToggleAsync)),
            ("set_default", nameof(YeelightLight), nameof(YeelightLight.SetDefaultAsync)),
            ("start_cf", nameof(YeelightLight), nameof(YeelightLight.StartColorFlowAsync)),
            ("stop_cf", nameof(YeelightLight), nameof(YeelightLight.StopColorFlowAsync)),
            ("set_scene", nameof(YeelightLight), nameof(YeelightLight.SetSceneAsync)),
            ("cron_add", nameof(YeelightDevice), nameof(YeelightDevice.SetSleepTimerAsync)),
            ("cron_get", nameof(YeelightDevice), nameof(YeelightDevice.GetSleepTimerAsync)),
            ("cron_del", nameof(YeelightDevice), nameof(YeelightDevice.CancelSleepTimerAsync)),
            ("set_adjust", nameof(YeelightLight), nameof(YeelightLight.AdjustAsync)),
            ("set_music", nameof(YeelightDevice), nameof(YeelightDevice.StartMusicModeAsync)),
            ("set_name", nameof(YeelightDevice), nameof(YeelightDevice.SetNameAsync)),
            ("bg_set_rgb", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetRgbAsync)),
            ("bg_set_hsv", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetHsvAsync)),
            ("bg_set_ct_abx", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetColorTemperatureAsync)),
            ("bg_start_cf", nameof(YeelightBackgroundLight), nameof(YeelightLight.StartColorFlowAsync)),
            ("bg_stop_cf", nameof(YeelightBackgroundLight), nameof(YeelightLight.StopColorFlowAsync)),
            ("bg_set_scene", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetSceneAsync)),
            ("bg_set_default", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetDefaultAsync)),
            ("bg_set_power", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetPowerAsync)),
            ("bg_set_bright", nameof(YeelightBackgroundLight), nameof(YeelightLight.SetBrightnessAsync)),
            ("bg_set_adjust", nameof(YeelightBackgroundLight), nameof(YeelightLight.AdjustAsync)),
            ("bg_toggle", nameof(YeelightBackgroundLight), nameof(YeelightLight.ToggleAsync)),
            ("dev_toggle", nameof(YeelightDevice), nameof(YeelightDevice.ToggleAllAsync)),
            ("adjust_bright", nameof(YeelightLight), nameof(YeelightLight.AdjustBrightnessAsync)),
            ("adjust_ct", nameof(YeelightLight), nameof(YeelightLight.AdjustColorTemperatureAsync)),
            ("adjust_color", nameof(YeelightLight), nameof(YeelightLight.AdjustColorAsync)),
            ("bg_adjust_bright", nameof(YeelightBackgroundLight), nameof(YeelightLight.AdjustBrightnessAsync)),
            ("bg_adjust_ct", nameof(YeelightBackgroundLight), nameof(YeelightLight.AdjustColorTemperatureAsync)),
            ("bg_adjust_color", nameof(YeelightBackgroundLight), nameof(YeelightLight.AdjustColorAsync)),
        ];

        // The calls of the sweep: every typed command once (the overloads each, and the optional arguments given, as an expression needs).
        private static readonly Expression<Func<YeelightLight, Task>>[] LightCalls =
        [
            light => light.SendCommandAsync("toggle", null, default),
            light => light.SetPowerAsync(true, null, PowerOnMode.Normal, default),
            light => light.TurnOnAsync(null, PowerOnMode.Normal, default),
            light => light.TurnOffAsync(null, default),
            light => light.ToggleAsync(default),
            light => light.SetBrightnessAsync(50, null, default),
            light => light.SetColorTemperatureAsync(3500, null, default),
            light => light.SetRgbAsync(0, 0, 255, null, default),
            light => light.SetRgbAsync(Color.Red, null, default),
            light => light.SetHsvAsync(255, 45, null, default),
            light => light.SetDefaultAsync(default),
            light => light.StartColorFlowAsync(WireCases.SceneFlow(), default),
            light => light.StopColorFlowAsync(default),
            light => light.SetSceneAsync(YeelightScene.AutoDelayOff(50, 5), default),
            light => light.AdjustAsync(AdjustAction.Increase, AdjustProperty.Brightness, default),
            light => light.AdjustBrightnessAsync(10, null, default),
            light => light.AdjustColorTemperatureAsync(10, null, default),
            light => light.AdjustColorAsync(null, default),
        ];

        private static readonly Expression<Func<YeelightDevice, Task>>[] DeviceCalls =
        [
            device => device.RefreshAsync(default),
            device => device.GetPropertiesAsync(new[] { "power", "bright" }, default),
            device => device.SetNameAsync("sweep", default),
            device => device.ToggleAllAsync(default),
            device => device.SetSleepTimerAsync(TimeSpan.FromMinutes(5), default),
            device => device.GetSleepTimerAsync(default),
            device => device.CancelSleepTimerAsync(default),
        ];

        private static readonly Expression<Func<YeelightDevice, Task>> StartMusic = device => device.StartMusicModeAsync(null, 0, default);

        private static readonly Expression<Func<YeelightMusicSession, Task>>[] MusicCalls =
        [
            session => session.SendCommandAsync("toggle", null, default),
            session => session.SetPowerAsync(true, null, default),
            session => session.ToggleAsync(default),
            session => session.SetBrightnessAsync(50, null, default),
            session => session.SetColorTemperatureAsync(3500, null, default),
            session => session.SetRgbAsync(0, 0, 255, null, default),
            session => session.SetRgbAsync(Color.Red, null, default),
            session => session.SetHsvAsync(255, 45, null, default),
        ];

        private static readonly Lazy<Task<SweepResult>> Sweep = new(RunSweepAsync);

        [Fact]
        public void TableOf41_HasThe35MethodsOfTheSpecification_AndTheMapNamesEachOfThemOnce()
        {
            Assert.Equal(35, WireCases.Table41.Count);
            Assert.Equal(35, WireCases.Table41.Distinct().Count());
            Assert.Equal(35, Map.Length);
            Assert.Equal(WireCases.Table41.Order(StringComparer.Ordinal).ToArray(), Map.Select(row => row.Method).Order(StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void TheSimulatedLight_ListsTheMethodsOfTable41_AndNoOther()
        {
            var listed = SimulatedBulbOptions.AllMethods.Split(' ').Concat(SimulatedBulbOptions.BackgroundMethods.Split(' ')).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(WireCases.Table41.Order(StringComparer.Ordinal).ToArray(), listed);
        }

        [Fact]
        public void EveryMemberThatTheMapNames_IsAPublicCommandOfTheApi()
        {
            var types = new[] { typeof(YeelightLight), typeof(YeelightDevice), typeof(YeelightBackgroundLight) }.ToDictionary(type => type.Name);

            foreach (var (method, receiver, member) in Map)
            {
                var candidates = types[receiver].GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(info => info.Name == member).ToArray();

                Assert.True(candidates.Length > 0, $"{receiver} has no public member {member}, which should send {method}.");
                Assert.All(candidates, info => Assert.True(typeof(Task).IsAssignableFrom(info.ReturnType), $"{receiver}.{member} is not an asynchronous command."));
            }
        }

        [Fact]
        public async Task ASweepOfEveryTypedCommand_SendsEveryMethodOfTable41()
        {
            var sweep = await Sweep.Value;

            var missing = WireCases.Table41.Except(sweep.Received).ToArray();

            Assert.True(missing.Length == 0, "Methods of table 4-1 that no typed command sent: " + string.Join(", ", missing));
        }

        [Fact]
        public async Task ASweepOfEveryTypedCommand_SendsNothingThatTable41DoesNotHave()
        {
            var sweep = await Sweep.Value;

            var extra = sweep.Received.Except(WireCases.Table41).ToArray();

            Assert.True(extra.Length == 0, "Methods that a typed command sent and that table 4-1 does not have: " + string.Join(", ", extra));
        }

        [Fact]
        public async Task EveryMethodOfTable41_IsSentByTheMemberThatTheMapNames()
        {
            var sweep = await Sweep.Value;

            foreach (var (method, receiver, member) in Map)
            {
                Assert.True(
                    sweep.Observed.TryGetValue((receiver, member), out var sent) && sent.Contains(method),
                    $"{receiver}.{member} should send {method}, and it sent: " + (sweep.Observed.TryGetValue((receiver, member), out var other) ? string.Join(", ", other) : "nothing (it is not part of the sweep)"));
            }
        }

        [Fact]
        public async Task EveryTypedCommandOfTheApi_IsPartOfTheSweep()
        {
            var sweep = await Sweep.Value;

            var commands = Commands(typeof(YeelightLight), declaredOnly: true)
                .Concat(Commands(typeof(YeelightDevice), declaredOnly: true))
                .Concat(Commands(typeof(YeelightBackgroundLight), declaredOnly: true))
                .Concat(Commands(typeof(YeelightMusicSession), declaredOnly: true))
                .Select(Signature)
                .ToArray();

            var missing = commands.Except(sweep.Covered).ToArray();
            Assert.True(commands.Length >= 34, "The API has fewer commands than it had: the reflection finds " + commands.Length);
            Assert.True(missing.Length == 0, "Commands of the API that the sweep does not call (add them to the sweep): " + string.Join("; ", missing));
        }

        [Fact]
        public void TheListOfPropertiesOfTheLibrary_IsTable42OfTheSpecification()
        {
            Assert.Equal(23, WireCases.Table42.Count);
            Assert.Equal(WireCases.Table42.ToArray(), YeelightProperties.All.ToArray());
        }

        private static IEnumerable<MethodInfo> Commands(Type type, bool declaredOnly)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | (declaredOnly ? BindingFlags.DeclaredOnly : BindingFlags.Default);
            return type.GetMethods(flags).Where(info => !info.IsSpecialName && typeof(Task).IsAssignableFrom(info.ReturnType) && !NotCommands.Contains(info.Name));
        }

        private static string Signature(MethodInfo method) => method.DeclaringType!.Name + "." + method;

        private static MethodInfo MethodOf<T>(Expression<Func<T, Task>> call)
        {
            var body = call.Body;
            while (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            {
                body = convert.Operand;
            }

            return ((MethodCallExpression)body).Method;
        }

        private static async Task<SweepResult> RunSweepAsync()
        {
            await using var h = await Wire.StartAsync(new SimulatedBulbOptions { HasBackground = true });
            var covered = new HashSet<string>();
            var observed = new Dictionary<(string Receiver, string Member), HashSet<string>>();

            async Task Run(string receiver, MethodInfo method, Func<Task> call, bool overMusic)
            {
                var before = h.Bulb.Requests.Count;
                await call();
                if (overMusic)
                {
                    await Wait.UntilAsync(() => h.Bulb.Requests.Count > before, $"{receiver}.{method.Name} over the music connection");
                }

                covered.Add(Signature(method));
                if (!observed.TryGetValue((receiver, method.Name), out var sent))
                {
                    observed[(receiver, method.Name)] = sent = [];
                }

                sent.UnionWith(h.Bulb.Requests.Skip(before).Select(request => request.Method));
            }

            foreach (var call in LightCalls)
            {
                var compiled = call.Compile();
                await Run(nameof(YeelightLight), MethodOf(call), () => compiled(h.Device), overMusic: false);
                await Run(nameof(YeelightBackgroundLight), MethodOf(call), () => compiled(h.Device.Background), overMusic: false);
            }

            foreach (var call in DeviceCalls)
            {
                var compiled = call.Compile();
                await Run(nameof(YeelightDevice), MethodOf(call), () => compiled(h.Device), overMusic: false);
            }

            YeelightMusicSession? session = null;
            await Run(nameof(YeelightDevice), MethodOf(StartMusic), async () => session = await h.Device.StartMusicModeAsync(), overMusic: false);
            foreach (var call in MusicCalls)
            {
                var compiled = call.Compile();
                await Run(nameof(YeelightMusicSession), MethodOf(call), () => compiled(session!), overMusic: true);
            }

            await session!.DisposeAsync();
            return new SweepResult(covered, observed, h.Bulb.Requests.Select(request => request.Method).ToHashSet(StringComparer.Ordinal));
        }

        private sealed record SweepResult(
            HashSet<string> Covered,
            Dictionary<(string Receiver, string Member), HashSet<string>> Observed,
            HashSet<string> Received);
    }
}
