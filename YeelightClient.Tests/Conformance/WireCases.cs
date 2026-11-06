namespace Yeelight.Tests.Conformance
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading.Tasks;

    /// <summary>
    /// A command of a light, and what the specification says that it puts on the wire: the method (without the <c>bg_</c> of the
    /// background light) and the list of parameters as JSON, written the way that the client writes it (no space, as the specification's
    /// own examples have it, once the spaces that it puts in for the reader are taken out).
    /// </summary>
    /// <param name="Name">What the call is, for the name of the test.</param>
    /// <param name="Method">The method.</param>
    /// <param name="Parameters">The parameters.</param>
    /// <param name="Invoke">The call.</param>
    internal sealed record WireCase(string Name, string Method, string Parameters, Func<YeelightLight, Task> Invoke)
    {
        public override string ToString() => this.Name;
    }

    /// <summary>
    /// A command of a session of music mode, with what it sends.
    /// </summary>
    /// <param name="Name">What the call is, for the name of the test.</param>
    /// <param name="Method">The method.</param>
    /// <param name="Parameters">The parameters.</param>
    /// <param name="Invoke">The call.</param>
    internal sealed record MusicWireCase(string Name, string Method, string Parameters, Func<YeelightMusicSession, Task> Invoke)
    {
        public override string ToString() => this.Name;
    }

    /// <summary>
    /// The call that a light is refused, and why.
    /// </summary>
    /// <param name="Name">What the call is, for the name of the test.</param>
    /// <param name="Exception">The type of the exception.</param>
    /// <param name="ParamName">The argument that is wrong.</param>
    /// <param name="Invoke">The call.</param>
    internal sealed record RefusedCase(string Name, Type Exception, string ParamName, Func<YeelightLight, Task> Invoke)
    {
        public override string ToString() => this.Name;
    }

    /// <summary>
    /// The call that a session of music mode refuses, and why.
    /// </summary>
    /// <param name="Name">What the call is, for the name of the test.</param>
    /// <param name="Exception">The type of the exception.</param>
    /// <param name="ParamName">The argument that is wrong.</param>
    /// <param name="Invoke">The call.</param>
    internal sealed record MusicRefusedCase(string Name, Type Exception, string ParamName, Func<YeelightMusicSession, Task> Invoke)
    {
        public override string ToString() => this.Name;
    }

    /// <summary>
    /// The tables of the conformance tests: every typed command of a light (the main light and the background light have the same ones),
    /// with the method and the parameters that the specification (table 4-1, and the example of every method) says it sends, and the
    /// arguments that the light has to refuse before it sends anything. The numbers are written out by hand, from the specification, and not
    /// worked out the way the library does: they are the oracle.
    /// </summary>
    internal static class WireCases
    {
        /// <summary>The methods of table 4-1 of the specification, in the order of the table.</summary>
        public static IReadOnlyList<string> Table41 { get; } =
        [
            "get_prop", "set_ct_abx", "set_rgb", "set_hsv", "set_bright", "set_power", "toggle", "set_default", "start_cf", "stop_cf",
            "set_scene", "cron_add", "cron_get", "cron_del", "set_adjust", "set_music", "set_name",
            "bg_set_rgb", "bg_set_hsv", "bg_set_ct_abx", "bg_start_cf", "bg_stop_cf", "bg_set_scene", "bg_set_default", "bg_set_power",
            "bg_set_bright", "bg_set_adjust", "bg_toggle", "dev_toggle",
            "adjust_bright", "adjust_ct", "adjust_color", "bg_adjust_bright", "bg_adjust_ct", "bg_adjust_color",
        ];

        /// <summary>The properties of table 4-2 of the specification, in the order of the table.</summary>
        public static IReadOnlyList<string> Table42 { get; } =
        [
            "power", "bright", "ct", "rgb", "hue", "sat", "color_mode", "flowing", "delayoff", "flow_params", "music_on", "name",
            "bg_power", "bg_flowing", "bg_flow_params", "bg_ct", "bg_lmode", "bg_bright", "bg_rgb", "bg_hue", "bg_sat",
            "nl_br", "active_mode",
        ];

        /// <summary>The flow of the example of <c>start_cf</c> of the specification.</summary>
        public static ColorFlow SpecificationFlow()
        {
            return new ColorFlow(
                [
                    ColorFlowStep.ColorTemperature(2700, Ms(1000), 100),
                    ColorFlowStep.Rgb(0, 0, 255, Ms(500), 10),
                    ColorFlowStep.Sleep(Ms(5000)),
                    ColorFlowStep.ColorTemperature(5000, Ms(500), 1),
                ],
                count: 4,
                action: ColorFlowAction.TurnOff);
        }

        /// <summary>The flow of the example of <c>set_scene</c> of the specification (class <c>cf</c>).</summary>
        public static ColorFlow SceneFlow()
        {
            return new ColorFlow(
                [
                    ColorFlowStep.Rgb(0, 0, 255, Ms(500), 100),
                    ColorFlowStep.Rgb(255, 255, 0, Ms(1000), 70),
                ]);
        }

        /// <summary>Gets every typed command of a light, with what it sends.</summary>
        public static IReadOnlyList<WireCase> Light { get; } = MakeLight();

        /// <summary>Gets every typed command of a session of music mode, with what it sends.</summary>
        public static IReadOnlyList<MusicWireCase> Music { get; } = MakeMusic();

        /// <summary>Gets the calls that a light refuses before it sends anything.</summary>
        public static IReadOnlyList<RefusedCase> Refused { get; } = MakeRefused();

        /// <summary>Gets the calls that a session of music mode refuses before it sends anything.</summary>
        public static IReadOnlyList<MusicRefusedCase> MusicRefused { get; } = MakeMusicRefused();

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private static WireCase Case(string name, string method, string parameters, Func<YeelightLight, Task> invoke) => new(name, method, parameters, invoke);

        private static RefusedCase Refuse<TException>(string name, string paramName, Func<YeelightLight, Task> invoke)
            where TException : ArgumentException => new(name, typeof(TException), paramName, invoke);

        private static MusicWireCase MusicCase(string name, string method, string parameters, Func<YeelightMusicSession, Task> invoke) => new(name, method, parameters, invoke);

        private static MusicRefusedCase MusicRefuse<TException>(string name, string paramName, Func<YeelightMusicSession, Task> invoke)
            where TException : ArgumentException => new(name, typeof(TException), paramName, invoke);

        private static List<WireCase> MakeLight()
        {
            var cases = new List<WireCase>
            {
                // set_power: the mode, the fourth parameter, is only there when it is not the normal one (older lights know three).
                Case("SetPowerAsync(true)", "set_power", """["on","sudden",0]""", light => light.SetPowerAsync(true)),
                Case("SetPowerAsync(false)", "set_power", """["off","sudden",0]""", light => light.SetPowerAsync(false)),
                Case("SetPowerAsync(true, 500 ms)", "set_power", """["on","smooth",500]""", light => light.SetPowerAsync(true, Ms(500))),
                Case("SetPowerAsync(false, 500 ms)", "set_power", """["off","smooth",500]""", light => light.SetPowerAsync(false, Ms(500))),
                Case("SetPowerAsync(true, 30 ms)", "set_power", """["on","smooth",30]""", light => light.SetPowerAsync(true, Ms(30))),
                Case("SetPowerAsync(true, zero)", "set_power", """["on","sudden",0]""", light => light.SetPowerAsync(true, TimeSpan.Zero)),
                Case("SetPowerAsync(true, 500 ms, Normal)", "set_power", """["on","smooth",500]""", light => light.SetPowerAsync(true, Ms(500), PowerOnMode.Normal)),
                Case("SetPowerAsync(true, null, ColorTemperature)", "set_power", """["on","sudden",0,1]""", light => light.SetPowerAsync(true, null, PowerOnMode.ColorTemperature)),
                Case("SetPowerAsync(true, null, Rgb)", "set_power", """["on","sudden",0,2]""", light => light.SetPowerAsync(true, null, PowerOnMode.Rgb)),
                Case("SetPowerAsync(true, null, Hsv)", "set_power", """["on","sudden",0,3]""", light => light.SetPowerAsync(true, null, PowerOnMode.Hsv)),
                Case("SetPowerAsync(true, 500 ms, ColorFlow)", "set_power", """["on","smooth",500,4]""", light => light.SetPowerAsync(true, Ms(500), PowerOnMode.ColorFlow)),
                Case("SetPowerAsync(true, null, NightLight)", "set_power", """["on","sudden",0,5]""", light => light.SetPowerAsync(true, null, PowerOnMode.NightLight)),
                Case("TurnOnAsync()", "set_power", """["on","sudden",0]""", light => light.TurnOnAsync()),
                Case("TurnOnAsync(500 ms)", "set_power", """["on","smooth",500]""", light => light.TurnOnAsync(Ms(500))),
                Case("TurnOnAsync(null, Hsv)", "set_power", """["on","sudden",0,3]""", light => light.TurnOnAsync(null, PowerOnMode.Hsv)),
                Case("TurnOffAsync()", "set_power", """["off","sudden",0]""", light => light.TurnOffAsync()),
                Case("TurnOffAsync(500 ms)", "set_power", """["off","smooth",500]""", light => light.TurnOffAsync(Ms(500))),
                Case("ToggleAsync()", "toggle", "[]", light => light.ToggleAsync()),

                Case("SetBrightnessAsync(50, 500 ms)", "set_bright", """[50,"smooth",500]""", light => light.SetBrightnessAsync(50, Ms(500))),
                Case("SetBrightnessAsync(1)", "set_bright", """[1,"sudden",0]""", light => light.SetBrightnessAsync(1)),
                Case("SetBrightnessAsync(100)", "set_bright", """[100,"sudden",0]""", light => light.SetBrightnessAsync(100)),
                Case("SetBrightnessAsync(100, zero)", "set_bright", """[100,"sudden",0]""", light => light.SetBrightnessAsync(100, TimeSpan.Zero)),

                Case("SetColorTemperatureAsync(3500, 500 ms)", "set_ct_abx", """[3500,"smooth",500]""", light => light.SetColorTemperatureAsync(3500, Ms(500))),
                Case("SetColorTemperatureAsync(1700)", "set_ct_abx", """[1700,"sudden",0]""", light => light.SetColorTemperatureAsync(1700)),
                Case("SetColorTemperatureAsync(6500)", "set_ct_abx", """[6500,"sudden",0]""", light => light.SetColorTemperatureAsync(6500)),

                // The colour is one number: red times 65536, plus green times 256, plus blue.
                Case("SetRgbAsync(0, 0, 255, 500 ms)", "set_rgb", """[255,"smooth",500]""", light => light.SetRgbAsync(0, 0, 255, Ms(500))),
                Case("SetRgbAsync(0, 0, 0)", "set_rgb", """[0,"sudden",0]""", light => light.SetRgbAsync(0, 0, 0)),
                Case("SetRgbAsync(255, 255, 255)", "set_rgb", """[16777215,"sudden",0]""", light => light.SetRgbAsync(255, 255, 255)),
                Case("SetRgbAsync(255, 0, 0)", "set_rgb", """[16711680,"sudden",0]""", light => light.SetRgbAsync(255, 0, 0)),
                Case("SetRgbAsync(0, 255, 0)", "set_rgb", """[65280,"sudden",0]""", light => light.SetRgbAsync(0, 255, 0)),
                Case("SetRgbAsync(1, 2, 3)", "set_rgb", """[66051,"sudden",0]""", light => light.SetRgbAsync(1, 2, 3)),
                Case("SetRgbAsync(Color), whose alpha is ignored", "set_rgb", """[3430008,"sudden",0]""", light => light.SetRgbAsync(Color.FromArgb(0x12, 0x34, 0x56, 0x78))),
                Case("SetRgbAsync(Color, 500 ms)", "set_rgb", """[16776960,"smooth",500]""", light => light.SetRgbAsync(Color.Yellow, Ms(500))),

                Case("SetHsvAsync(255, 45, 500 ms)", "set_hsv", """[255,45,"smooth",500]""", light => light.SetHsvAsync(255, 45, Ms(500))),
                Case("SetHsvAsync(0, 0)", "set_hsv", """[0,0,"sudden",0]""", light => light.SetHsvAsync(0, 0)),
                Case("SetHsvAsync(359, 100)", "set_hsv", """[359,100,"sudden",0]""", light => light.SetHsvAsync(359, 100)),

                Case("SetDefaultAsync()", "set_default", "[]", light => light.SetDefaultAsync()),

                // start_cf: the count, the action and the expression, which is one string of four numbers for every step.
                Case("StartColorFlowAsync(the example of the specification)", "start_cf", """[4,2,"1000,2,2700,100,500,1,255,10,5000,7,0,0,500,2,5000,1"]""", light => light.StartColorFlowAsync(SpecificationFlow())),
                Case(
                    "StartColorFlowAsync(an endless flow that recovers)",
                    "start_cf",
                    """[0,0,"500,1,255,100,1000,1,16776960,70"]""",
                    light => light.StartColorFlowAsync(SceneFlow())),
                Case(
                    "StartColorFlowAsync(a flow that stays)",
                    "start_cf",
                    """[1,1,"50,2,1700,-1"]""",
                    light => light.StartColorFlowAsync(new ColorFlow([ColorFlowStep.ColorTemperature(1700, Ms(50))], 1, ColorFlowAction.Stay))),
                Case("StopColorFlowAsync()", "stop_cf", "[]", light => light.StopColorFlowAsync()),

                // set_scene: the class, and what belongs to the class.
                Case("SetSceneAsync(colour)", "set_scene", """["color",65280,70]""", light => light.SetSceneAsync(YeelightScene.Rgb(0, 255, 0, 70))),
                Case("SetSceneAsync(colour of a Color)", "set_scene", """["color",65280,70]""", light => light.SetSceneAsync(YeelightScene.Rgb(Color.FromArgb(255, 0, 255, 0), 70))),
                Case("SetSceneAsync(hsv)", "set_scene", """["hsv",300,70,100]""", light => light.SetSceneAsync(YeelightScene.Hsv(300, 70, 100))),
                Case("SetSceneAsync(colour temperature)", "set_scene", """["ct",5400,100]""", light => light.SetSceneAsync(YeelightScene.ColorTemperature(5400, 100))),
                Case("SetSceneAsync(colour flow)", "set_scene", """["cf",0,0,"500,1,255,100,1000,1,16776960,70"]""", light => light.SetSceneAsync(YeelightScene.Flow(SceneFlow()))),
                Case("SetSceneAsync(a flow that counts and turns off)", "set_scene", """["cf",4,2,"1000,2,2700,100,500,1,255,10,5000,7,0,0,500,2,5000,1"]""", light => light.SetSceneAsync(YeelightScene.Flow(SpecificationFlow()))),
                Case("SetSceneAsync(auto delay off)", "set_scene", """["auto_delay_off",50,5]""", light => light.SetSceneAsync(YeelightScene.AutoDelayOff(50, 5))),

                Case("AdjustBrightnessAsync(-20, 500 ms)", "adjust_bright", "[-20,500]", light => light.AdjustBrightnessAsync(-20, Ms(500))),
                Case("AdjustBrightnessAsync(100)", "adjust_bright", "[100,30]", light => light.AdjustBrightnessAsync(100)),
                Case("AdjustBrightnessAsync(-100)", "adjust_bright", "[-100,30]", light => light.AdjustBrightnessAsync(-100)),
                Case("AdjustBrightnessAsync(0)", "adjust_bright", "[0,30]", light => light.AdjustBrightnessAsync(0)),
                Case("AdjustColorTemperatureAsync(20, 500 ms)", "adjust_ct", "[20,500]", light => light.AdjustColorTemperatureAsync(20, Ms(500))),
                Case("AdjustColorTemperatureAsync(-100)", "adjust_ct", "[-100,30]", light => light.AdjustColorTemperatureAsync(-100)),

                // The percentage of adjust_color is ignored by the light, and the library sends a zero for it. A duration of none is the shortest
                // that the specification allows (30 milliseconds): an adjustment has no "sudden" effect that would let the duration be ignored.
                Case("AdjustColorAsync(500 ms)", "adjust_color", "[0,500]", light => light.AdjustColorAsync(Ms(500))),
                Case("AdjustColorAsync()", "adjust_color", "[0,30]", light => light.AdjustColorAsync()),
            };

            var actions = new[] { (AdjustAction.Increase, "increase"), (AdjustAction.Decrease, "decrease"), (AdjustAction.Circle, "circle") };
            var properties = new[] { (AdjustProperty.Brightness, "bright"), (AdjustProperty.ColorTemperature, "ct") };
            foreach (var (action, direction) in actions)
            {
                foreach (var (property, what) in properties)
                {
                    cases.Add(Case($"AdjustAsync({action}, {property})", "set_adjust", $"[\"{direction}\",\"{what}\"]", light => light.AdjustAsync(action, property)));
                }
            }

            // The colour can only be adjusted in a circle.
            cases.Add(Case("AdjustAsync(Circle, Color)", "set_adjust", """["circle","color"]""", light => light.AdjustAsync(AdjustAction.Circle, AdjustProperty.Color)));
            return cases;
        }

        private static List<MusicWireCase> MakeMusic()
        {
            return
            [
                MusicCase("SetPowerAsync(true)", "set_power", """["on","sudden",0]""", session => session.SetPowerAsync(true)),
                MusicCase("SetPowerAsync(false, 500 ms)", "set_power", """["off","smooth",500]""", session => session.SetPowerAsync(false, Ms(500))),
                MusicCase("ToggleAsync()", "toggle", "[]", session => session.ToggleAsync()),
                MusicCase("SetBrightnessAsync(50, 500 ms)", "set_bright", """[50,"smooth",500]""", session => session.SetBrightnessAsync(50, Ms(500))),
                MusicCase("SetBrightnessAsync(1)", "set_bright", """[1,"sudden",0]""", session => session.SetBrightnessAsync(1)),
                MusicCase("SetColorTemperatureAsync(3500, 500 ms)", "set_ct_abx", """[3500,"smooth",500]""", session => session.SetColorTemperatureAsync(3500, Ms(500))),
                MusicCase("SetColorTemperatureAsync(6500)", "set_ct_abx", """[6500,"sudden",0]""", session => session.SetColorTemperatureAsync(6500)),
                MusicCase("SetRgbAsync(0, 0, 255, 500 ms)", "set_rgb", """[255,"smooth",500]""", session => session.SetRgbAsync(0, 0, 255, Ms(500))),
                MusicCase("SetRgbAsync(255, 255, 255)", "set_rgb", """[16777215,"sudden",0]""", session => session.SetRgbAsync(255, 255, 255)),
                MusicCase("SetRgbAsync(Color), whose alpha is ignored", "set_rgb", """[3430008,"sudden",0]""", session => session.SetRgbAsync(Color.FromArgb(0x12, 0x34, 0x56, 0x78))),
                MusicCase("SetHsvAsync(255, 45, 500 ms)", "set_hsv", """[255,45,"smooth",500]""", session => session.SetHsvAsync(255, 45, Ms(500))),
                MusicCase("SetHsvAsync(359, 100)", "set_hsv", """[359,100,"sudden",0]""", session => session.SetHsvAsync(359, 100)),
                MusicCase("SendCommandAsync(bg_set_rgb)", "bg_set_rgb", """[255,"smooth",500]""", session => session.SendCommandAsync("bg_set_rgb", [255, "smooth", 500])),
                MusicCase("SendCommandAsync(no parameters)", "bg_toggle", "[]", session => session.SendCommandAsync("bg_toggle")),
            ];
        }

        private static List<RefusedCase> MakeRefused()
        {
            const string Method = "no_such_method";
            return
            [
                Refuse<ArgumentOutOfRangeException>("SetPowerAsync(true, 29 ms)", "transition", light => light.SetPowerAsync(true, Ms(29))),
                Refuse<ArgumentOutOfRangeException>("SetPowerAsync(true, 1 ms)", "transition", light => light.SetPowerAsync(true, Ms(1))),
                Refuse<ArgumentOutOfRangeException>("SetPowerAsync(true, -500 ms)", "transition", light => light.SetPowerAsync(true, Ms(-500))),
                Refuse<ArgumentOutOfRangeException>("SetPowerAsync(true, a mode that does not exist)", "mode", light => light.SetPowerAsync(true, null, (PowerOnMode)6)),
                Refuse<ArgumentOutOfRangeException>("SetPowerAsync(true, a negative mode)", "mode", light => light.SetPowerAsync(true, null, (PowerOnMode)(-1))),
                Refuse<ArgumentException>("SetPowerAsync(false, a mode)", "mode", light => light.SetPowerAsync(false, null, PowerOnMode.Rgb)),
                Refuse<ArgumentOutOfRangeException>("TurnOnAsync(29 ms)", "transition", light => light.TurnOnAsync(Ms(29))),
                Refuse<ArgumentOutOfRangeException>("TurnOnAsync(null, a mode that does not exist)", "mode", light => light.TurnOnAsync(null, (PowerOnMode)99)),
                Refuse<ArgumentOutOfRangeException>("TurnOffAsync(29 ms)", "transition", light => light.TurnOffAsync(Ms(29))),

                Refuse<ArgumentOutOfRangeException>("SetBrightnessAsync(0)", "brightness", light => light.SetBrightnessAsync(0)),
                Refuse<ArgumentOutOfRangeException>("SetBrightnessAsync(101)", "brightness", light => light.SetBrightnessAsync(101)),
                Refuse<ArgumentOutOfRangeException>("SetBrightnessAsync(-1)", "brightness", light => light.SetBrightnessAsync(-1)),
                Refuse<ArgumentOutOfRangeException>("SetBrightnessAsync(int.MaxValue)", "brightness", light => light.SetBrightnessAsync(int.MaxValue)),
                Refuse<ArgumentOutOfRangeException>("SetBrightnessAsync(50, 29 ms)", "transition", light => light.SetBrightnessAsync(50, Ms(29))),

                Refuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(1699)", "kelvin", light => light.SetColorTemperatureAsync(1699)),
                Refuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(6501)", "kelvin", light => light.SetColorTemperatureAsync(6501)),
                Refuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(0)", "kelvin", light => light.SetColorTemperatureAsync(0)),
                Refuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(3500, 29 ms)", "transition", light => light.SetColorTemperatureAsync(3500, Ms(29))),

                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(-1, 0, 0)", "red", light => light.SetRgbAsync(-1, 0, 0)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(256, 0, 0)", "red", light => light.SetRgbAsync(256, 0, 0)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(0, -1, 0)", "green", light => light.SetRgbAsync(0, -1, 0)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 256, 0)", "green", light => light.SetRgbAsync(0, 256, 0)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 0, -1)", "blue", light => light.SetRgbAsync(0, 0, -1)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 0, 256)", "blue", light => light.SetRgbAsync(0, 0, 256)),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 0, 255, 29 ms)", "transition", light => light.SetRgbAsync(0, 0, 255, Ms(29))),
                Refuse<ArgumentOutOfRangeException>("SetRgbAsync(Color, 29 ms)", "transition", light => light.SetRgbAsync(Color.Red, Ms(29))),

                Refuse<ArgumentOutOfRangeException>("SetHsvAsync(-1, 0)", "hue", light => light.SetHsvAsync(-1, 0)),
                Refuse<ArgumentOutOfRangeException>("SetHsvAsync(360, 0)", "hue", light => light.SetHsvAsync(360, 0)),
                Refuse<ArgumentOutOfRangeException>("SetHsvAsync(0, -1)", "saturation", light => light.SetHsvAsync(0, -1)),
                Refuse<ArgumentOutOfRangeException>("SetHsvAsync(0, 101)", "saturation", light => light.SetHsvAsync(0, 101)),
                Refuse<ArgumentOutOfRangeException>("SetHsvAsync(0, 0, 29 ms)", "transition", light => light.SetHsvAsync(0, 0, Ms(29))),

                Refuse<ArgumentNullException>("StartColorFlowAsync(null)", "flow", light => light.StartColorFlowAsync(null!)),
                Refuse<ArgumentNullException>("SetSceneAsync(null)", "scene", light => light.SetSceneAsync(null!)),

                Refuse<ArgumentOutOfRangeException>("AdjustAsync(an action that does not exist)", "action", light => light.AdjustAsync((AdjustAction)3, AdjustProperty.Brightness)),
                Refuse<ArgumentOutOfRangeException>("AdjustAsync(a property that does not exist)", "property", light => light.AdjustAsync(AdjustAction.Increase, (AdjustProperty)3)),
                Refuse<ArgumentException>("AdjustAsync(Increase, Color)", "action", light => light.AdjustAsync(AdjustAction.Increase, AdjustProperty.Color)),
                Refuse<ArgumentException>("AdjustAsync(Decrease, Color)", "action", light => light.AdjustAsync(AdjustAction.Decrease, AdjustProperty.Color)),

                Refuse<ArgumentOutOfRangeException>("AdjustBrightnessAsync(101)", "percentage", light => light.AdjustBrightnessAsync(101)),
                Refuse<ArgumentOutOfRangeException>("AdjustBrightnessAsync(-101)", "percentage", light => light.AdjustBrightnessAsync(-101)),
                Refuse<ArgumentOutOfRangeException>("AdjustBrightnessAsync(0, 29 ms)", "duration", light => light.AdjustBrightnessAsync(0, Ms(29))),
                Refuse<ArgumentOutOfRangeException>("AdjustColorTemperatureAsync(101)", "percentage", light => light.AdjustColorTemperatureAsync(101)),
                Refuse<ArgumentOutOfRangeException>("AdjustColorTemperatureAsync(-101)", "percentage", light => light.AdjustColorTemperatureAsync(-101)),
                Refuse<ArgumentOutOfRangeException>("AdjustColorTemperatureAsync(0, 29 ms)", "duration", light => light.AdjustColorTemperatureAsync(0, Ms(29))),
                Refuse<ArgumentOutOfRangeException>("AdjustColorAsync(29 ms)", "duration", light => light.AdjustColorAsync(Ms(29))),

                Refuse<ArgumentException>("SendCommandAsync(an empty method)", "method", light => light.SendCommandAsync(string.Empty)),
                Refuse<ArgumentNullException>("SendCommandAsync(no method)", "method", light => light.SendCommandAsync(null!)),
                Refuse<ArgumentException>("SendCommandAsync(a parameter that is an object)", "value", light => light.SendCommandAsync(Method, [new object()])),
                Refuse<ArgumentException>("SendCommandAsync(a parameter that is a list)", "value", light => light.SendCommandAsync(Method, [new[] { 1, 2 }])),
            ];
        }

        private static List<MusicRefusedCase> MakeMusicRefused()
        {
            return
            [
                MusicRefuse<ArgumentOutOfRangeException>("SetPowerAsync(true, 29 ms)", "transition", session => session.SetPowerAsync(true, Ms(29))),
                MusicRefuse<ArgumentOutOfRangeException>("SetBrightnessAsync(0)", "brightness", session => session.SetBrightnessAsync(0)),
                MusicRefuse<ArgumentOutOfRangeException>("SetBrightnessAsync(101)", "brightness", session => session.SetBrightnessAsync(101)),
                MusicRefuse<ArgumentOutOfRangeException>("SetBrightnessAsync(50, 29 ms)", "transition", session => session.SetBrightnessAsync(50, Ms(29))),
                MusicRefuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(1699)", "kelvin", session => session.SetColorTemperatureAsync(1699)),
                MusicRefuse<ArgumentOutOfRangeException>("SetColorTemperatureAsync(6501)", "kelvin", session => session.SetColorTemperatureAsync(6501)),
                MusicRefuse<ArgumentOutOfRangeException>("SetRgbAsync(256, 0, 0)", "red", session => session.SetRgbAsync(256, 0, 0)),
                MusicRefuse<ArgumentOutOfRangeException>("SetRgbAsync(0, -1, 0)", "green", session => session.SetRgbAsync(0, -1, 0)),
                MusicRefuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 0, 256)", "blue", session => session.SetRgbAsync(0, 0, 256)),
                MusicRefuse<ArgumentOutOfRangeException>("SetRgbAsync(0, 0, 255, 29 ms)", "transition", session => session.SetRgbAsync(0, 0, 255, Ms(29))),
                MusicRefuse<ArgumentOutOfRangeException>("SetHsvAsync(360, 0)", "hue", session => session.SetHsvAsync(360, 0)),
                MusicRefuse<ArgumentOutOfRangeException>("SetHsvAsync(0, 101)", "saturation", session => session.SetHsvAsync(0, 101)),
                MusicRefuse<ArgumentOutOfRangeException>("SetHsvAsync(0, 0, 29 ms)", "transition", session => session.SetHsvAsync(0, 0, Ms(29))),
                MusicRefuse<ArgumentException>("SendCommandAsync(an empty method)", "method", session => session.SendCommandAsync(string.Empty)),
                MusicRefuse<ArgumentException>("SendCommandAsync(a parameter that is an object)", "value", session => session.SendCommandAsync("set_bright", [new object()])),
            ];
        }
    }
}
