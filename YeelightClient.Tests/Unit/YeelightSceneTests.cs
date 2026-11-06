namespace Yeelight.Tests.Unit
{
    using System;
    using System.Drawing;
    using Xunit;

    /// <summary>
    /// A scene (set_scene, section 4.1 of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the class of the scene and the values that belong to it, as the
    /// light takes them.
    /// </summary>
    public class YeelightSceneTests
    {
        private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);

        [Fact]
        public void Rgb_IsTheClassColor_WithTheNumberOfTheColorAndABrightness()
        {
            // The first example of set_scene: {"params": ["color", 65280, 70]}.
            var scene = YeelightScene.Rgb(0, 255, 0, 70);

            Assert.Equal(new object[] { "color", 65280, 70 }, scene.GetParameters());
        }

        [Fact]
        public void Rgb_OfAColor_IgnoresItsAlpha()
        {
            var scene = YeelightScene.Rgb(Color.FromArgb(10, 0, 255, 0), 70);

            Assert.Equal(new object[] { "color", 65280, 70 }, scene.GetParameters());
        }

        [Fact]
        public void Hsv_IsTheClassHsv_WithAHueASaturationAndABrightness()
        {
            // The second example: {"params": ["hsv", 300, 70, 100]}.
            var scene = YeelightScene.Hsv(300, 70, 100);

            Assert.Equal(new object[] { "hsv", 300, 70, 100 }, scene.GetParameters());
        }

        [Fact]
        public void ColorTemperature_IsTheClassCt_WithKelvinAndABrightness()
        {
            // The third example: {"params": ["ct", 5400, 100]}.
            var scene = YeelightScene.ColorTemperature(5400, 100);

            Assert.Equal(new object[] { "ct", 5400, 100 }, scene.GetParameters());
        }

        [Fact]
        public void Flow_IsTheClassCf_WithTheCountTheActionAndTheExpression()
        {
            // The fourth example: {"params": ["cf", 0, 0, "500,1,255,100,1000,1,16776960,70"]}, an endless flow of two steps.
            var flow = new ColorFlow([ColorFlowStep.Rgb(0, 0, 255, Half, 100), ColorFlowStep.Rgb(255, 255, 0, TimeSpan.FromSeconds(1), 70)]);

            var scene = YeelightScene.Flow(flow);

            Assert.Equal(new object[] { "cf", 0, 0, "500,1,255,100,1000,1,16776960,70" }, scene.GetParameters());
        }

        [Fact]
        public void Flow_TakesTheCountAndTheActionOfTheFlow()
        {
            var flow = new ColorFlow([ColorFlowStep.Sleep(Half)], 3, ColorFlowAction.TurnOff);

            Assert.Equal(new object[] { "cf", 3, 2, "500,7,0,0" }, YeelightScene.Flow(flow).GetParameters());
        }

        [Fact]
        public void AutoDelayOff_IsTheClassOfThatName_WithABrightnessAndMinutes()
        {
            // The fifth example: {"params": ["auto_delay_off", 50, 5]}. (The list of classes of the specification says "auto_dealy_off", but
            // its description and its example have the name that the lights know.)
            var scene = YeelightScene.AutoDelayOff(50, 5);

            Assert.Equal(new object[] { "auto_delay_off", 50, 5 }, scene.GetParameters());
        }

        [Fact]
        public void TheParameters_AreTheClassAsTextAndWholeNumbers_AndTheExpressionOfAFlowAsText()
        {
            var flow = new ColorFlow([ColorFlowStep.Sleep(Half)]);
            (YeelightScene Scene, Type[] Types)[] scenes =
            [
                (YeelightScene.Rgb(1, 2, 3, 4), [typeof(string), typeof(int), typeof(int)]),
                (YeelightScene.Hsv(1, 2, 3), [typeof(string), typeof(int), typeof(int), typeof(int)]),
                (YeelightScene.ColorTemperature(2000, 4), [typeof(string), typeof(int), typeof(int)]),
                (YeelightScene.Flow(flow), [typeof(string), typeof(int), typeof(int), typeof(string)]),
                (YeelightScene.AutoDelayOff(5, 6), [typeof(string), typeof(int), typeof(int)]),
            ];

            Assert.All(scenes, item => Assert.Equal(item.Types, item.Scene.GetParameters().Select(parameter => parameter.GetType())));
        }

        [Fact]
        public void GetParameters_IsACopy()
        {
            var scene = YeelightScene.Rgb(0, 255, 0, 70);

            var first = scene.GetParameters();
            first[0] = "changed";
            first[1] = -1;
            Array.Clear(first);
            var second = scene.GetParameters();

            Assert.NotSame(first, second);
            Assert.Equal(new object[] { "color", 65280, 70 }, second);
        }

        [Fact]
        public void TheEdgesOfTheRanges_AreAccepted()
        {
            Assert.Equal(new object[] { "color", 0, 1 }, YeelightScene.Rgb(0, 0, 0, 1).GetParameters());
            Assert.Equal(new object[] { "color", 16777215, 100 }, YeelightScene.Rgb(255, 255, 255, 100).GetParameters());
            Assert.Equal(new object[] { "hsv", 0, 0, 1 }, YeelightScene.Hsv(0, 0, 1).GetParameters());
            Assert.Equal(new object[] { "hsv", 359, 100, 100 }, YeelightScene.Hsv(359, 100, 100).GetParameters());
            Assert.Equal(new object[] { "ct", 1700, 1 }, YeelightScene.ColorTemperature(1700, 1).GetParameters());
            Assert.Equal(new object[] { "ct", 6500, 100 }, YeelightScene.ColorTemperature(6500, 100).GetParameters());
            Assert.Equal(new object[] { "auto_delay_off", 1, 1 }, YeelightScene.AutoDelayOff(1, 1).GetParameters());
            Assert.Equal(new object[] { "auto_delay_off", 100, int.MaxValue }, YeelightScene.AutoDelayOff(100, int.MaxValue).GetParameters());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        [InlineData(-1)]
        public void ABrightnessOutsideTheRange_IsRefused_ByEverySceneThatHasOne(int brightness)
        {
            Action[] makes =
            [
                () => YeelightScene.Rgb(1, 2, 3, brightness),
                () => YeelightScene.Rgb(Color.Red, brightness),
                () => YeelightScene.Hsv(1, 2, brightness),
                () => YeelightScene.ColorTemperature(2000, brightness),
                () => YeelightScene.AutoDelayOff(brightness, 5),
            ];

            Assert.All(makes, make => Assert.Equal("brightness", Assert.Throws<ArgumentOutOfRangeException>(make).ParamName));
        }

        [Theory]
        [InlineData(-1, 0, 0, "red")]
        [InlineData(256, 0, 0, "red")]
        [InlineData(0, -1, 0, "green")]
        [InlineData(0, 256, 0, "green")]
        [InlineData(0, 0, -1, "blue")]
        [InlineData(0, 0, 256, "blue")]
        public void APartOfAColorOutsideTheRange_IsRefused(int red, int green, int blue, string name)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => YeelightScene.Rgb(red, green, blue, 50));

            Assert.Equal(name, refusal.ParamName);
        }

        [Theory]
        [InlineData(360, 0, "hue")]
        [InlineData(-1, 0, "hue")]
        [InlineData(0, 101, "saturation")]
        [InlineData(0, -1, "saturation")]
        public void AHueOrASaturationOutsideTheRange_IsRefused(int hue, int saturation, string name)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => YeelightScene.Hsv(hue, saturation, 50));

            Assert.Equal(name, refusal.ParamName);
        }

        [Theory]
        [InlineData(1699)]
        [InlineData(6501)]
        [InlineData(0)]
        public void AColorTemperatureOutsideTheRange_IsRefused(int kelvin)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => YeelightScene.ColorTemperature(kelvin, 50));

            Assert.Equal("kelvin", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ATimerOfLessThanAMinute_IsRefused(int minutes)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => YeelightScene.AutoDelayOff(50, minutes));

            Assert.Equal("minutes", refusal.ParamName);
        }

        [Fact]
        public void ANullFlow_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => YeelightScene.Flow(null!));

            Assert.Equal("flow", refusal.ParamName);
        }

        [Fact]
        public void ToString_SaysWhatTheSceneIs()
        {
            Assert.Equal("Scene: colour #00FF00 at 70%", YeelightScene.Rgb(0, 255, 0, 70).ToString());
            Assert.Equal("Scene: colour #0000FF at 5%", YeelightScene.Rgb(Color.Blue, 5).ToString());
            Assert.Equal("Scene: hue 300, saturation 70% at 100%", YeelightScene.Hsv(300, 70, 100).ToString());
            Assert.Equal("Scene: 5400K at 100%", YeelightScene.ColorTemperature(5400, 100).ToString());
            Assert.Equal("Scene: colour flow of 2 steps", YeelightScene.Flow(new ColorFlow([ColorFlowStep.Sleep(Half), ColorFlowStep.Sleep(Half)])).ToString());
            Assert.Equal("Scene: 50% for 5 minutes", YeelightScene.AutoDelayOff(50, 5).ToString());
        }

        [Fact]
        public void TheDescription_IsWhatToStringSaysAfterThePrefix()
        {
            var scene = YeelightScene.Hsv(1, 2, 3);

            Assert.Equal("Scene: " + scene.Description, scene.ToString());
        }
    }
}
