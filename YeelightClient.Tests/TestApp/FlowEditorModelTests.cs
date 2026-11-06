namespace Yeelight.Tests.TestApp
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;
    using Yeelight.TestApp.Models;
    using Xunit;

    /// <summary>The colour flow editor of the test app: the steps that a person builds, the flow that the library makes of them, and the flow that a light reports.</summary>
    public sealed class FlowEditorModelTests
    {
        [Fact]
        public void AStepOfEveryKind_BecomesAStepOfTheLibrary()
        {
            var color = FlowStepModel.Color(0xFF8C00, 1000, 80).ToStep();
            var temperature = FlowStepModel.Temperature(2700, 500).ToStep();
            var sleep = FlowStepModel.Sleep(2000).ToStep();

            Assert.Equal(ColorFlowMode.Rgb, color.Mode);
            Assert.Equal(0xFF8C00, color.Value);
            Assert.Equal(80, color.Brightness);
            Assert.Equal(TimeSpan.FromMilliseconds(1000), color.Duration);
            Assert.Equal(ColorFlowMode.ColorTemperature, temperature.Mode);
            Assert.Equal(2700, temperature.Value);
            Assert.Null(temperature.Brightness);
            Assert.Equal(ColorFlowMode.Sleep, sleep.Mode);
            Assert.Equal(TimeSpan.FromMilliseconds(2000), sleep.Duration);
        }

        [Fact]
        public void TheStepsOfTheEditor_BecomeTheFlowOfTheLibrary()
        {
            var editor = new FlowEditorModel { Count = 4, Action = ColorFlowAction.Stay };
            editor.Add(FlowStepModel.Color(0xFF0000, 1000, 100));
            editor.Add(FlowStepModel.Temperature(2700, 500));
            editor.Add(FlowStepModel.Sleep(2000));

            var flow = editor.ToFlow();

            Assert.Equal(3, flow.Steps.Count);
            Assert.Equal(4, flow.Count);
            Assert.Equal(ColorFlowAction.Stay, flow.Action);
            Assert.Equal("4,1,1000,1,16711680,100,500,2,2700,-1,2000,7,0,0", flow.ToString());
            Assert.Equal(TimeSpan.FromMilliseconds(3500), editor.RoundDuration);
        }

        [Fact]
        public void AFlowWithoutSteps_IsRefusedByTheLibrary()
        {
            Assert.Throws<ArgumentException>(() => new FlowEditorModel().ToFlow());
        }

        [Theory]
        [InlineData(0, 10, 50)]
        [InlineData(0, 49, 50)]
        [InlineData(0, 1000, 101)]
        [InlineData(0, 1000, 0)]
        [InlineData(1, 1000, 50)]
        [InlineData(1, 1000, 101)]
        public void AValueThatALightWouldRefuse_IsRefusedWhenTheFlowIsMade(int kind, int durationMs, int brightness)
        {
            var editor = new FlowEditorModel();
            editor.Add(kind == 0 ? FlowStepModel.Color(0xFF0000, durationMs, brightness) : FlowStepModel.Temperature(kind == 1 && brightness == 50 ? 1000 : 3000, durationMs, brightness));

            Assert.Throws<ArgumentOutOfRangeException>(() => editor.ToFlow());
        }

        [Fact]
        public void ASleepOfLessThanTheLeastStep_IsRefused()
        {
            var editor = new FlowEditorModel();
            editor.Add(FlowStepModel.Sleep(10));

            Assert.Throws<ArgumentOutOfRangeException>(() => editor.ToFlow());
        }

        [Fact]
        public void Steps_CanBeReplacedMovedAndRemoved()
        {
            var editor = new FlowEditorModel();
            var a = FlowStepModel.Color(0xFF0000, 100);
            var b = FlowStepModel.Color(0x00FF00, 100);
            var c = FlowStepModel.Color(0x0000FF, 100);
            editor.Add(a);
            editor.Add(b);
            editor.Add(c);

            Assert.True(editor.MoveDown(0));
            Assert.Equal(new[] { b, a, c }, editor.Steps);
            Assert.True(editor.MoveUp(2));
            Assert.Equal(new[] { b, c, a }, editor.Steps);
            Assert.False(editor.MoveUp(0));
            Assert.False(editor.MoveDown(2));
            Assert.False(editor.MoveUp(7));
            Assert.False(editor.MoveDown(-1));

            var d = FlowStepModel.Sleep(300);
            Assert.True(editor.Replace(1, d));
            Assert.Equal(new[] { b, d, a }, editor.Steps);
            Assert.False(editor.Replace(3, d));

            Assert.True(editor.RemoveAt(0));
            Assert.Equal(new[] { d, a }, editor.Steps);
            Assert.False(editor.RemoveAt(5));
            Assert.False(editor.RemoveAt(-1));

            editor.Count = 9;
            editor.Action = ColorFlowAction.TurnOff;
            editor.Clear();
            Assert.Empty(editor.Steps);
            Assert.Equal(0, editor.Count);
            Assert.Equal(ColorFlowAction.RecoverState, editor.Action);
        }

        [Fact]
        public void TheEditor_ReportsWhenItsStepsChange()
        {
            var editor = new FlowEditorModel();
            var changes = 0;
            editor.Steps.CollectionChanged += (_, _) => changes++;

            editor.Add(FlowStepModel.Sleep(100));
            editor.Add(FlowStepModel.Sleep(200));
            editor.MoveUp(1);
            editor.RemoveAt(0);

            Assert.Equal(4, changes);
        }

        [Fact]
        public void AFlowOfTheLibrary_CanBeLoadedInTheEditorAndMadeAgain()
        {
            var flow = new ColorFlow(
                [
                    ColorFlowStep.Rgb(255, 140, 0, TimeSpan.FromMilliseconds(800), 60),
                    ColorFlowStep.ColorTemperature(5000, TimeSpan.FromSeconds(2)),
                    ColorFlowStep.Sleep(TimeSpan.FromMilliseconds(250)),
                ],
                7,
                ColorFlowAction.TurnOff);

            var editor = FlowEditorModel.From(flow);

            Assert.Equal(3, editor.Steps.Count);
            Assert.Equal(FlowStepKind.Color, editor.Steps[0].Kind);
            Assert.Equal(60, editor.Steps[0].Brightness);
            Assert.Equal(FlowStepKind.ColorTemperature, editor.Steps[1].Kind);
            Assert.Null(editor.Steps[1].Brightness);
            Assert.Equal(FlowStepKind.Sleep, editor.Steps[2].Kind);
            Assert.Equal(7, editor.Count);
            Assert.Equal(ColorFlowAction.TurnOff, editor.Action);
            Assert.Equal(flow.ToString(), editor.ToFlow().ToString());
        }

        [Fact]
        public void EveryExample_IsAFlowThatEndsByItself_AndThatTheLibraryAccepts()
        {
            Assert.NotEmpty(FlowEditorModel.Presets);
            Assert.Equal(FlowEditorModel.Presets.Count, FlowEditorModel.Presets.Select(preset => preset.Name).Distinct().Count());
            foreach (var preset in FlowEditorModel.Presets)
            {
                var editor = new FlowEditorModel();
                editor.Load(preset);

                var flow = editor.ToFlow();

                Assert.True(flow.Count > 0, $"{preset.Name} never ends");
                Assert.False(string.IsNullOrWhiteSpace(preset.Description));
                Assert.Equal(preset.Steps.Count, flow.Steps.Count);
                Assert.True(ColorFlow.TryParse(flow.ToString(), out var parsed), $"{preset.Name} cannot be read back");
                Assert.Equal(flow.ToString(), parsed!.ToString());
            }
        }

        [Fact]
        public async Task EveryExample_IsRunByALight_AndWhatTheLightReportsIsDescribed()
        {
            foreach (var preset in FlowEditorModel.Presets)
            {
                await using var harness = await Harness.StartAsync();
                await harness.SettleAsync();
                var editor = new FlowEditorModel();
                editor.Load(preset);
                var flow = editor.ToFlow();

                await harness.Device.StartColorFlowAsync(flow);

                try
                {
                    await Wait.UntilAsync(() => harness.Device.IsFlowing && harness.Device.FlowParameters == flow.ToString(), $"the flow {preset.Name}", TimeSpan.FromSeconds(3));
                }
                catch (Exception)
                {
                    Assert.Fail($"{preset.Name}: flowing={harness.Device.IsFlowing}, reported='{harness.Device.FlowParameters}', expected='{flow}', the light got: {string.Join(" | ", harness.Bulb.Requests.Select(request => request.Raw))}");
                }

                var description = FlowEditorModel.DescribeParameters(harness.Device.FlowParameters);
                Assert.Contains($"{flow.Steps.Count} steps", description, StringComparison.Ordinal);
                Assert.Contains("1. ", description, StringComparison.Ordinal);
                Assert.True(ColorFlow.TryParse(harness.Device.FlowParameters, out var reported));
                Assert.Equal(flow.ToString(), FlowEditorModel.From(reported!).ToFlow().ToString());
            }
        }

        [Theory]
        [InlineData(null, "No flow is running.")]
        [InlineData("", "No flow is running.")]
        [InlineData("   ", "No flow is running.")]
        public void WhenNoFlowRuns_TheDescriptionSaysSo(string? parameters, string expected)
        {
            Assert.Equal(expected, FlowEditorModel.DescribeParameters(parameters));
        }

        [Fact]
        public void AFlowThatTheLibraryCannotRead_IsReportedAsItIs()
        {
            var text = FlowEditorModel.DescribeParameters("this,is,not,a,flow");

            Assert.Contains("not a flow", text, StringComparison.Ordinal);
            Assert.Contains("this,is,not,a,flow", text, StringComparison.Ordinal);
        }

        [Fact]
        public void AFlowThatRunsForEver_IsDescribedAsSuch()
        {
            var text = FlowEditorModel.DescribeParameters("0,0,1000,2,2700,100,500,1,255,10");

            Assert.StartsWith("2 steps, never ends, then recover the state", text, StringComparison.Ordinal);
            Assert.Contains("1. ", text, StringComparison.Ordinal);
            Assert.Contains("2. ", text, StringComparison.Ordinal);
            Assert.Contains("2700K", text, StringComparison.Ordinal);
        }

        [Fact]
        public void TheStepsAndTheEditor_DescribeThemselves()
        {
            Assert.Equal("Colour #FF0000, 100%, over 1000 ms", FlowStepModel.Color(0xFF0000, 1000, 100).Description);
            Assert.Equal("Colour temperature 2700 K, keep brightness, over 500 ms", FlowStepModel.Temperature(2700, 500).Description);
            Assert.Equal("Sleep for 2000 ms", FlowStepModel.Sleep(2000).Description);
            Assert.Equal(string.Empty, FlowStepModel.Sleep(2000).ValueText);
            Assert.Equal("#FF0000", FlowStepModel.Color(0xFF0000, 100).ValueText);

            var editor = new FlowEditorModel { Count = 0, Action = ColorFlowAction.TurnOff };
            editor.Add(FlowStepModel.Sleep(1500));
            Assert.Equal("1 steps (1.5 s a round), never ends, then turn off.", editor.Describe());
        }

        [Fact]
        public void TheActionsOfAFlow_HaveWords()
        {
            Assert.Contains("recover", FlowEditorModel.ActionText(ColorFlowAction.RecoverState), StringComparison.Ordinal);
            Assert.Contains("stay", FlowEditorModel.ActionText(ColorFlowAction.Stay), StringComparison.Ordinal);
            Assert.Contains("off", FlowEditorModel.ActionText(ColorFlowAction.TurnOff), StringComparison.Ordinal);
        }
    }
}
