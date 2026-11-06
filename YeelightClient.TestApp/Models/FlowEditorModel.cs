namespace Yeelight.TestApp.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>A colour flow that is ready to be loaded in the editor, to see what a flow can do.</summary>
    /// <param name="Name">The name of the example.</param>
    /// <param name="Description">What it does, in a sentence.</param>
    /// <param name="Steps">The steps.</param>
    /// <param name="Count">The number of visible state changes before it ends.</param>
    /// <param name="Action">What the light does when it has ended.</param>
    internal sealed record FlowPreset(string Name, string Description, IReadOnlyList<FlowStepModel> Steps, int Count, ColorFlowAction Action);

    /// <summary>
    /// The colour flow that is being built in the editor of the Control page: a list of steps (add, replace, remove, move), a count and an
    /// action. It makes the <see cref="ColorFlow"/> of the library, which checks it, and reads one that a light reports.
    /// </summary>
    internal sealed class FlowEditorModel
    {
        /// <summary>Gets the examples that can be loaded. Every one is a flow that the library accepts, and that ends by itself.</summary>
        public static IReadOnlyList<FlowPreset> Presets { get; } =
        [
            new FlowPreset(
                "Rainbow",
                "Goes through the colours of the rainbow, two rounds, and then goes back to what the light showed.",
                [
                    FlowStepModel.Color(0xFF0000, 1500, 100),
                    FlowStepModel.Color(0xFFFF00, 1500, 100),
                    FlowStepModel.Color(0x00FF00, 1500, 100),
                    FlowStepModel.Color(0x00FFFF, 1500, 100),
                    FlowStepModel.Color(0x0000FF, 1500, 100),
                    FlowStepModel.Color(0xFF00FF, 1500, 100),
                ],
                12,
                ColorFlowAction.RecoverState),
            new FlowPreset(
                "Alarm",
                "Flashes red and blue, ten times each, and then stays on the last colour.",
                [
                    FlowStepModel.Color(0xFF0000, 300, 100),
                    FlowStepModel.Sleep(100),
                    FlowStepModel.Color(0x0000FF, 300, 100),
                    FlowStepModel.Sleep(100),
                ],
                20,
                ColorFlowAction.Stay),
            new FlowPreset(
                "Sunrise",
                "Starts as a dim candle and ends in daylight: three steps in warm to cool white, and then stays.",
                [
                    FlowStepModel.Temperature(1700, 1000, 1),
                    FlowStepModel.Temperature(2700, 10000, 50),
                    FlowStepModel.Temperature(5000, 10000, 100),
                ],
                3,
                ColorFlowAction.Stay),
            new FlowPreset(
                "Candle",
                "Flickers in warm white and fades out after a minute (the light is switched off at the end).",
                [
                    FlowStepModel.Temperature(2000, 400, 30),
                    FlowStepModel.Temperature(2000, 300, 60),
                    FlowStepModel.Temperature(1900, 500, 40),
                    FlowStepModel.Temperature(2100, 250, 70),
                    FlowStepModel.Temperature(2000, 600, 35),
                ],
                140,
                ColorFlowAction.TurnOff),
            new FlowPreset(
                "Pulse",
                "Breathes in orange: eight times between bright and dim, and then goes back to what the light showed.",
                [
                    FlowStepModel.Color(0xFF8C00, 800, 100),
                    FlowStepModel.Color(0xFF8C00, 800, 10),
                ],
                16,
                ColorFlowAction.RecoverState),
        ];

        /// <summary>Gets the steps of the flow, in the order in which they run.</summary>
        public ObservableCollection<FlowStepModel> Steps { get; } = [];

        /// <summary>Gets or sets the number of visible state changes before the flow ends; 0 is a flow that never ends.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets what the light does when the flow has ended.</summary>
        public ColorFlowAction Action { get; set; } = ColorFlowAction.RecoverState;

        /// <summary>Gets the time that one round of the steps takes.</summary>
        public TimeSpan RoundDuration => TimeSpan.FromMilliseconds(this.Steps.Sum(step => (double)step.DurationMs));

        /// <summary>Gets the action in words, for a person.</summary>
        /// <param name="action">The action.</param>
        /// <returns>For example <c>recover the state</c>.</returns>
        public static string ActionText(ColorFlowAction action)
        {
            return action switch
            {
                ColorFlowAction.RecoverState => "recover the state from before the flow",
                ColorFlowAction.Stay => "stay as it is",
                ColorFlowAction.TurnOff => "turn off",
                _ => action.ToString(),
            };
        }

        /// <summary>Describes a flow that a light reports in <see cref="YeelightLight.FlowParameters"/>, step by step.</summary>
        /// <param name="flowParameters">The parameters as the light reports them (<c>0,0,1000,2,2700,100,500,1,255,10</c>).</param>
        /// <returns>The description, or the reason why the text is not a flow.</returns>
        public static string DescribeParameters(string? flowParameters)
        {
            if (string.IsNullOrWhiteSpace(flowParameters))
            {
                return "No flow is running.";
            }

            if (!ColorFlow.TryParse(flowParameters, out var flow) || flow is null)
            {
                return $"The parameters are not a flow that the library can read: {flowParameters}";
            }

            var text = new StringBuilder();
            text.Append(string.Create(CultureInfo.InvariantCulture, $"{flow.Steps.Count} steps, "));
            text.Append(flow.Count == 0 ? "never ends" : string.Create(CultureInfo.InvariantCulture, $"ends after {flow.Count} visible changes"));
            text.Append(", then ").Append(ActionText(flow.Action)).Append('.');
            for (var index = 0; index < flow.Steps.Count; index++)
            {
                text.AppendLine().Append(string.Create(CultureInfo.InvariantCulture, $"{index + 1}. ")).Append(flow.Steps[index]);
            }

            return text.ToString();
        }

        /// <summary>Makes an editor with the steps of a flow.</summary>
        /// <param name="flow">The flow.</param>
        /// <returns>The editor.</returns>
        public static FlowEditorModel From(ColorFlow flow)
        {
            ArgumentNullException.ThrowIfNull(flow);
            var model = new FlowEditorModel();
            model.Load(flow);
            return model;
        }

        /// <summary>Adds a step at the end.</summary>
        /// <param name="step">The step.</param>
        public void Add(FlowStepModel step)
        {
            ArgumentNullException.ThrowIfNull(step);
            this.Steps.Add(step);
        }

        /// <summary>Replaces a step.</summary>
        /// <param name="index">The place of the step.</param>
        /// <param name="step">The new step.</param>
        /// <returns><c>false</c> when there is no step at that place.</returns>
        public bool Replace(int index, FlowStepModel step)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (index < 0 || index >= this.Steps.Count)
            {
                return false;
            }

            this.Steps[index] = step;
            return true;
        }

        /// <summary>Removes a step.</summary>
        /// <param name="index">The place of the step.</param>
        /// <returns><c>false</c> when there is no step at that place.</returns>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= this.Steps.Count)
            {
                return false;
            }

            this.Steps.RemoveAt(index);
            return true;
        }

        /// <summary>Moves a step one place forward (earlier in the flow).</summary>
        /// <param name="index">The place of the step.</param>
        /// <returns><c>false</c> when the step is the first, or there is none at that place.</returns>
        public bool MoveUp(int index)
        {
            if (index < 1 || index >= this.Steps.Count)
            {
                return false;
            }

            this.Steps.Move(index, index - 1);
            return true;
        }

        /// <summary>Moves a step one place back (later in the flow).</summary>
        /// <param name="index">The place of the step.</param>
        /// <returns><c>false</c> when the step is the last, or there is none at that place.</returns>
        public bool MoveDown(int index)
        {
            if (index < 0 || index >= this.Steps.Count - 1)
            {
                return false;
            }

            this.Steps.Move(index, index + 1);
            return true;
        }

        /// <summary>Removes every step, and puts the count and the action back.</summary>
        public void Clear()
        {
            this.Steps.Clear();
            this.Count = 0;
            this.Action = ColorFlowAction.RecoverState;
        }

        /// <summary>Takes over a flow of the library: its steps, its count and its action.</summary>
        /// <param name="flow">The flow.</param>
        public void Load(ColorFlow flow)
        {
            ArgumentNullException.ThrowIfNull(flow);
            this.Steps.Clear();
            foreach (var step in flow.Steps)
            {
                this.Steps.Add(FlowStepModel.From(step));
            }

            this.Count = flow.Count;
            this.Action = flow.Action;
        }

        /// <summary>Takes over an example.</summary>
        /// <param name="preset">The example.</param>
        public void Load(FlowPreset preset)
        {
            ArgumentNullException.ThrowIfNull(preset);
            this.Steps.Clear();
            foreach (var step in preset.Steps)
            {
                this.Steps.Add(step);
            }

            this.Count = preset.Count;
            this.Action = preset.Action;
        }

        /// <summary>Makes the flow of the library, which checks every value: what a light would refuse is refused here.</summary>
        /// <returns>The flow.</returns>
        /// <exception cref="ArgumentException">There are no steps.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A value is out of the range that the protocol allows.</exception>
        public ColorFlow ToFlow()
        {
            return new ColorFlow(this.Steps.Select(step => step.ToStep()).ToArray(), this.Count, this.Action);
        }

        /// <summary>Describes the flow on one line: the steps, the count and the action.</summary>
        /// <returns>The words.</returns>
        public string Describe()
        {
            var rounds = this.Count == 0 ? "never ends" : string.Create(CultureInfo.InvariantCulture, $"ends after {this.Count} visible changes");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{this.Steps.Count} steps ({this.RoundDuration.TotalSeconds:0.0#} s a round), {rounds}, then {ActionText(this.Action)}.");
        }
    }
}
