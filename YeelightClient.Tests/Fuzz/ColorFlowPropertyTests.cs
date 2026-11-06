namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;

    /// <summary>
    /// What a colour flow promises for any input: the text that a light reports is read without throwing, a flow that was made is written
    /// and read back as it was, and whatever is accepted is written in one form that reads back as itself.
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class ColorFlowPropertyTests(ITestOutputHelper output)
    {
        private static readonly Gen<ColorFlowStep> Step =
            from kind in Gen.Choose(0, 2)
            from milliseconds in Gen.Frequency((6, Gen.Choose(50, 5000)), (1, Gen.Choose(50, 3_600_000)))
            from brightness in Gen.Frequency((2, Gen.Constant<int?>(null)), (3, Gen.Choose(1, 100).Select(value => (int?)value)))
            from red in Gen.Choose(0, 255)
            from green in Gen.Choose(0, 255)
            from blue in Gen.Choose(0, 255)
            from kelvin in Gen.Choose(YeelightLimits.MinColorTemperature, YeelightLimits.MaxColorTemperature)
            select Make(kind, TimeSpan.FromMilliseconds(milliseconds), brightness, red, green, blue, kelvin);

        private static readonly Gen<ColorFlow> Flow =
            from first in Step
            from rest in Gen.ListOf(Step)
            from count in Gen.Frequency((2, Gen.Constant(0)), (3, Gen.Choose(1, 100)), (1, Gen.Choose(1, 100_000)))
            from action in Gen.Elements(ColorFlowAction.RecoverState, ColorFlowAction.Stay, ColorFlowAction.TurnOff)
            select new ColorFlow([first, .. rest], count, action);

        // Text in the form of a flow: a count, an action and groups of four numbers, now and then with a number that is not right.
        private static readonly Gen<string> FlowText =
            from count in Gen.Frequency((6, Gen.Choose(0, 20)), (1, Gen.Choose(-3, -1)))
            from action in Gen.Frequency((6, Gen.Choose(0, 2)), (1, Gen.Choose(-1, 5)))
            from groups in Gen.ListOf(
                from milliseconds in Gen.Frequency((6, Gen.Choose(50, 5000)), (1, Gen.Choose(-5, 49)))
                from mode in Gen.Frequency((6, Gen.Elements(1, 2, 7)), (1, Gen.Choose(-1, 9)))
                from value in Gen.Frequency((5, Gen.Choose(0, 0xFFFFFF)), (1, Gen.Choose(-5, 100)))
                from brightness in Gen.Frequency((4, Gen.Choose(1, 100)), (2, Gen.Constant(-1)), (1, Gen.Choose(-3, 120)))
                select string.Join(",", milliseconds, mode, value, brightness))
            from spaces in Gen.Elements(string.Empty, " ")
            select string.Join("," + spaces, new[] { count.ToString(CultureInfo.InvariantCulture), action.ToString(CultureInfo.InvariantCulture) }.Concat(groups));

        [Fact]
        public void AFlow_IsWrittenAndReadBackAsItWas()
        {
            FuzzCheck.ForAll(
                Flow,
                flow =>
                {
                    var text = flow.ToString();
                    Assert.True(ColorFlow.TryParse(text, out var read), text);
                    Assert.Equal(flow.Count, read!.Count);
                    Assert.Equal(flow.Action, read.Action);
                    Assert.Equal(flow.Steps.Count, read.Steps.Count);
                    for (var index = 0; index < flow.Steps.Count; index++)
                    {
                        var expected = flow.Steps[index];
                        var actual = read.Steps[index];
                        Assert.Equal(expected.Mode, actual.Mode);
                        Assert.Equal(expected.Value, actual.Value);
                        Assert.Equal(expected.Brightness, actual.Brightness);
                        Assert.Equal(expected.Duration, actual.Duration);
                    }

                    Assert.Equal(text, read.ToString());
                    return true;
                },
                seedOffset: 30);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void TheTextOfAFlow_IsReadWithoutThrowing_ForAnyText()
        {
            Assert.False(ColorFlow.TryParse(null, out _));
            var texts = Gen.Frequency(
                (3, FuzzGenerators.Text),
                (6, FlowText),
                (3, from flow in Flow from edits in Gen.ListOf(Gen.Choose(0, 1000)) select Cut(flow.ToString(), edits)),
                (1, Gen.ListOf(Gen.Elements("0", "1", "2", "-1", "2147483647", "2147483648", "x", string.Empty)).Select(parts => string.Join(",", parts))));

            FuzzCheck.ForAll(
                texts,
                text =>
                {
                    _ = ColorFlow.TryParse(text, out _);
                    return true;
                },
                seedOffset: 31);
        }

        [Fact]
        public void WhatIsAccepted_IsWrittenInOneForm_ThatReadsBackAsItself()
        {
            FuzzCheck.ForAll(
                FlowText,
                text =>
                {
                    if (ColorFlow.TryParse(text, out var flow))
                    {
                        var canonical = flow!.ToString();
                        Assert.True(ColorFlow.TryParse(canonical, out var again), canonical);
                        Assert.Equal(canonical, again!.ToString());
                        Assert.InRange(flow.Steps.Count, 1, 1000);
                    }

                    return true;
                },
                seedOffset: 32);
        }

        private static ColorFlowStep Make(int kind, TimeSpan duration, int? brightness, int red, int green, int blue, int kelvin)
        {
            return kind switch
            {
                0 => ColorFlowStep.Rgb(red, green, blue, duration, brightness),
                1 => ColorFlowStep.ColorTemperature(kelvin, duration, brightness),
                _ => ColorFlowStep.Sleep(duration),
            };
        }

        // Cuts a text off at one place and puts a few of its characters back in another.
        private static string Cut(string text, IEnumerable<int> edits)
        {
            var cuts = edits.ToArray();
            if (cuts.Length == 0 || text.Length == 0)
            {
                return text;
            }

            var at = cuts[0] % text.Length;
            return cuts.Length == 1 ? text[..at] : text[..at] + text[(cuts[1] % text.Length)..];
        }
    }
}
