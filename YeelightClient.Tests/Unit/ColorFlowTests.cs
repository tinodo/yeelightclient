namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// A colour flow (start_cf, section 4.1 of the Yeelight Inter-Operation Specification,
    /// https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf): the steps, how many changes it runs, and what the light does
    /// at the end; and the text that a light reports a running flow in, which this reads back.
    /// </summary>
    public class ColorFlowTests
    {
        private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);

        private static ColorFlowStep[] Steps(int count)
        {
            return Enumerable.Range(0, count).Select(index => ColorFlowStep.ColorTemperature(2000 + index, Half)).ToArray();
        }

        private static ColorFlow ExampleOfTheSpecification()
        {
            return new ColorFlow(
                [
                    ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMilliseconds(1000), 100),
                    ColorFlowStep.Rgb(0, 0, 255, Half, 10),
                    ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(5000)),
                    ColorFlowStep.ColorTemperature(5000, Half, 1),
                ],
                4,
                ColorFlowAction.TurnOff);
        }

        private static void AssertSame(ColorFlow expected, ColorFlow actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.Action, actual.Action);
            Assert.Equal(expected.Steps.Count, actual.Steps.Count);
            for (var index = 0; index < expected.Steps.Count; index++)
            {
                Assert.Equal(expected.Steps[index].Mode, actual.Steps[index].Mode);
                Assert.Equal(expected.Steps[index].Value, actual.Steps[index].Value);
                Assert.Equal(expected.Steps[index].Brightness, actual.Steps[index].Brightness);
                Assert.Equal(expected.Steps[index].Milliseconds, actual.Steps[index].Milliseconds);
            }
        }

        [Fact]
        public void AFlow_HasItsStepsItsCountAndItsAction()
        {
            var steps = Steps(3);

            var flow = new ColorFlow(steps, 7, ColorFlowAction.Stay);

            Assert.Equal(7, flow.Count);
            Assert.Equal(ColorFlowAction.Stay, flow.Action);
            Assert.Equal(steps, flow.Steps);
        }

        [Fact]
        public void AFlow_ThatRunsForEver_AndRecoversTheState_IsWhatTheDefaultsMean()
        {
            var flow = new ColorFlow(Steps(1));

            Assert.Equal(0, flow.Count);
            Assert.Equal(ColorFlowAction.RecoverState, flow.Action);
        }

        [Fact]
        public void TheSteps_ArePutInAListOfTheirOwn()
        {
            var steps = new List<ColorFlowStep>(Steps(2));
            var flow = new ColorFlow(steps);

            steps.Add(ColorFlowStep.Sleep(Half));
            steps[0] = ColorFlowStep.Sleep(Half);

            Assert.Equal(2, flow.Steps.Count);
            Assert.Equal(ColorFlowMode.ColorTemperature, flow.Steps[0].Mode);
        }

        [Fact]
        public void TheSteps_CannotBeChangedThroughTheList()
        {
            var flow = new ColorFlow(Steps(2));

            // A caller that casts the list to an array would change the steps of a flow that is shared, and that a scene holds.
            Assert.False(flow.Steps is ColorFlowStep[], "The list of steps is the array that the flow holds.");
        }

        [Fact]
        public void ASequenceOfSteps_ThatIsNotAList_IsRead()
        {
            var flow = new ColorFlow(Steps(3).Where(step => step.Value > 2000));

            Assert.Equal(2, flow.Steps.Count);
        }

        [Fact]
        public void ANullSetOfSteps_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => new ColorFlow(null!));

            Assert.Equal("steps", refusal.ParamName);
        }

        [Fact]
        public void NoSteps_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentException>(() => new ColorFlow([]));

            Assert.Equal("steps", refusal.ParamName);
        }

        [Fact]
        public void ANullStep_IsRefused()
        {
            var refusal = Assert.Throws<ArgumentNullException>(() => new ColorFlow([ColorFlowStep.Sleep(Half), null!]));

            Assert.Equal("steps", refusal.ParamName);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void ANegativeCount_IsRefused(int count)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new ColorFlow(Steps(1), count));

            Assert.Equal("count", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(int.MaxValue)]
        public void ACountOfZeroOrMore_IsAccepted(int count)
        {
            Assert.Equal(count, new ColorFlow(Steps(1), count).Count);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(-1)]
        [InlineData(100)]
        public void AnActionThatIsNotOneOfTheThree_IsRefused(int action)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new ColorFlow(Steps(1), 0, (ColorFlowAction)action));

            Assert.Equal("action", refusal.ParamName);
        }

        [Fact]
        public void TheExpression_IsTheTuplesOfTheStepsSeparatedByCommas()
        {
            Assert.Equal("1000,2,2700,100,500,1,255,10,5000,7,0,0,500,2,5000,1", ExampleOfTheSpecification().Expression);
        }

        [Fact]
        public void ToString_IsTheCountTheActionAndTheExpression()
        {
            Assert.Equal("4,2,1000,2,2700,100,500,1,255,10,5000,7,0,0,500,2,5000,1", ExampleOfTheSpecification().ToString());
            Assert.Equal("0,0,500,2,2000,-1", new ColorFlow(Steps(1)).ToString());
        }

        [Fact]
        public void TryParse_ThePatternOfTheDocumentation_IsAFlowOfTwoSteps()
        {
            Assert.True(ColorFlow.TryParse("0,0,1000,2,2700,100,500,1,255,10", out var flow));

            Assert.NotNull(flow);
            Assert.Equal(0, flow.Count);
            Assert.Equal(ColorFlowAction.RecoverState, flow.Action);
            Assert.Equal(2, flow.Steps.Count);
            Assert.Equal(ColorFlowMode.ColorTemperature, flow.Steps[0].Mode);
            Assert.Equal(2700, flow.Steps[0].Value);
            Assert.Equal(100, flow.Steps[0].Brightness);
            Assert.Equal(TimeSpan.FromMilliseconds(1000), flow.Steps[0].Duration);
            Assert.Equal(ColorFlowMode.Rgb, flow.Steps[1].Mode);
            Assert.Equal(255, flow.Steps[1].Value);
            Assert.Equal(10, flow.Steps[1].Brightness);
            Assert.Equal(TimeSpan.FromMilliseconds(500), flow.Steps[1].Duration);
        }

        [Fact]
        public void TryParse_TheExampleOfTheSpecification_IsTheFlowItBuilds()
        {
            Assert.True(ColorFlow.TryParse("4,2,1000,2,2700,100,500,1,255,10,5000,7,0,0,500,2,5000,1", out var flow));

            AssertSame(ExampleOfTheSpecification(), flow!);
        }

        [Fact]
        public void TryParse_TheSpecificationWritesSpacesInItsExample_AndThoseAreFine()
        {
            Assert.True(ColorFlow.TryParse("4, 2, 1000, 2, 2700, 100, 500, 1, 255, 10, 5000, 7, 0,0, 500, 2, 5000, 1", out var flow));

            AssertSame(ExampleOfTheSpecification(), flow!);
        }

        [Theory]
        [InlineData("0,0,500,1,255,100", 0, ColorFlowAction.RecoverState)]
        [InlineData("1,1,500,1,255,100", 1, ColorFlowAction.Stay)]
        [InlineData("5,2,500,1,255,100", 5, ColorFlowAction.TurnOff)]
        [InlineData("2147483647,2,500,1,255,100", int.MaxValue, ColorFlowAction.TurnOff)]
        [InlineData(" 0 , 1 , 500 , 1 , 255 , 100 ", 0, ColorFlowAction.Stay)]
        public void TryParse_TheCountAndTheAction_AreTheFirstTwoNumbers(string text, int count, ColorFlowAction action)
        {
            Assert.True(ColorFlow.TryParse(text, out var flow));

            Assert.Equal(count, flow!.Count);
            Assert.Equal(action, flow.Action);
            Assert.Single(flow.Steps);
        }

        [Fact]
        public void TryParse_ASleep_HasNeitherAValueNorABrightness()
        {
            Assert.True(ColorFlow.TryParse("0,0,5000,7,99,50", out var flow));

            var step = Assert.Single(flow!.Steps);
            Assert.Equal(ColorFlowMode.Sleep, step.Mode);
            Assert.Equal(0, step.Value);
            Assert.Null(step.Brightness);
        }

        [Fact]
        public void TryParse_ABrightnessOfMinus1_MeansThatTheBrightnessIsLeftAlone()
        {
            Assert.True(ColorFlow.TryParse("0,0,500,1,255,-1,500,2,4000,-1", out var flow));

            Assert.All(flow!.Steps, step => Assert.Null(step.Brightness));
        }

        [Fact]
        public void TryParse_ManySteps_AreAllRead()
        {
            var text = "0,0," + string.Join(",", Enumerable.Repeat("500,1,255,50", 500));

            Assert.True(ColorFlow.TryParse(text, out var flow));
            Assert.Equal(500, flow!.Steps.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("garbage")]
        [InlineData(",")]
        [InlineData(",,,,,")]
        [InlineData("0")]
        [InlineData("0,0")]
        [InlineData("0,0,1000,2,2700")]
        [InlineData("0,0,1000,2,2700,100,500")]
        [InlineData("0,0,1000,2,2700,100,500,1")]
        [InlineData("0,0,1000,2,2700,100,500,1,255")]
        [InlineData("0,0,1000,2,2700,100,")]
        [InlineData(",0,0,1000,2,2700,100")]
        [InlineData("0,0,1000,2,2700,100,500,1,255,10,")]
        [InlineData("0,0,,2,2700,100")]
        [InlineData("0,0,1000,2,2700,")]
        [InlineData("a,0,1000,2,2700,100")]
        [InlineData("0,b,1000,2,2700,100")]
        [InlineData("0,0,c,2,2700,100")]
        [InlineData("0,0,1000,d,2700,100")]
        [InlineData("0,0,1000,2,e,100")]
        [InlineData("0,0,1000,2,2700,f")]
        [InlineData("0.5,0,1000,2,2700,100")]
        [InlineData("0,0,1000,2,2700,100.5")]
        [InlineData("0x0,0,1000,2,2700,100")]
        [InlineData("1e1,0,1000,2,2700,100")]
        [InlineData("99999999999,0,1000,2,2700,100")]
        [InlineData("0,0,1000,2,2700,99999999999")]
        [InlineData("0;0;1000;2;2700;100")]
        [InlineData("0 0 1000 2 2700 100")]
        public void TryParse_WhatIsNotAFlow_IsRefused(string? text)
        {
            Assert.False(ColorFlow.TryParse(text, out var flow));
            Assert.Null(flow);
        }

        [Theory]
        [InlineData("-1,0,1000,2,2700,100")]
        [InlineData("-2147483648,0,1000,2,2700,100")]
        [InlineData("0,3,1000,2,2700,100")]
        [InlineData("0,-1,1000,2,2700,100")]
        [InlineData("0,100,1000,2,2700,100")]
        [InlineData("0,0,-1,2,2700,100")]
        [InlineData("0,0,1000,0,2700,100")]
        [InlineData("0,0,1000,3,2700,100")]
        [InlineData("0,0,1000,4,2700,100")]
        [InlineData("0,0,1000,6,2700,100")]
        [InlineData("0,0,1000,8,2700,100")]
        [InlineData("0,0,1000,-2,2700,100")]
        [InlineData("0,0,1000,2,2700,100,500,9,255,10")]
        [InlineData("0,0,1000,2,2700,100,-500,1,255,10")]
        public void TryParse_ANegativeCountOrDurationAnActionThatIsNot0To2OrAModeThatDoesNotExist_IsRefused(string text)
        {
            Assert.False(ColorFlow.TryParse(text, out var flow));
            Assert.Null(flow);
        }

        [Fact]
        public void TryParse_ThenToString_IsTheTextThatWasParsed()
        {
            const string text = "3,1,1000,2,2700,100,500,1,16711680,-1,5000,7,0,0";

            Assert.True(ColorFlow.TryParse(text, out var flow));

            Assert.Equal(text, flow!.ToString());
        }

        [Fact]
        public void AFlowThatAPersonMade_SurvivesToStringAndTryParse_WhateverItIsMadeOf()
        {
            var random = new Random(20261106);
            for (var round = 0; round < 1000; round++)
            {
                var steps = new List<ColorFlowStep>();
                for (var index = random.Next(1, 12); index > 0; index--)
                {
                    var duration = TimeSpan.FromMilliseconds(random.Next(50, 3_600_000));
                    int? brightness = random.Next(3) == 0 ? null : random.Next(1, 101);
                    steps.Add(
                        random.Next(3) switch
                        {
                            0 => ColorFlowStep.Rgb(random.Next(256), random.Next(256), random.Next(256), duration, brightness),
                            1 => ColorFlowStep.ColorTemperature(random.Next(1700, 6501), duration, brightness),
                            _ => ColorFlowStep.Sleep(duration),
                        });
                }

                var flow = new ColorFlow(steps, random.Next(0, 1000), (ColorFlowAction)random.Next(3));

                Assert.True(ColorFlow.TryParse(flow.ToString(), out var parsed), "Not read: " + flow);
                AssertSame(flow, parsed!);
                Assert.Equal(flow.ToString(), parsed!.ToString());
            }
        }

        [Fact]
        public void TryParse_RandomNumbers_NeverThrows_AndWhatIsReadIsAFlowThatCanBeWrittenAgain()
        {
            var random = new Random(1982);
            for (var round = 0; round < 3000; round++)
            {
                var numbers = Enumerable.Range(0, random.Next(0, 20)).Select(_ => random.Next(-3, 12).ToString()).ToArray();
                var text = string.Join(",", numbers);

                var read = ColorFlow.TryParse(text, out var flow);

                if (read)
                {
                    Assert.NotNull(flow);
                    Assert.True(ColorFlow.TryParse(flow.ToString(), out var again), "Not read again: " + flow);
                    AssertSame(flow, again!);
                }
                else
                {
                    Assert.Null(flow);
                }
            }
        }
    }
}
