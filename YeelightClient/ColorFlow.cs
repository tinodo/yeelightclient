namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// A colour flow: a series of changes of colour, colour temperature and brightness (and pauses) that a light runs by itself.
    /// </summary>
    /// <remarks>
    /// This is the most powerful command of a Yeelight light: with the steps of a flow a light can be "programmed". The steps run in a circle, and
    /// the flow ends after <see cref="Count"/> visible state changes (never, when that is zero), and then does what <see cref="Action"/> says.
    /// Start a flow with <see cref="YeelightLight.StartColorFlowAsync(ColorFlow, System.Threading.CancellationToken)"/>.
    /// </remarks>
    public sealed class ColorFlow
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorFlow"/> class.
        /// </summary>
        /// <param name="steps">The steps of the flow, at least one.</param>
        /// <param name="count">The number of visible state changes before the flow ends, or <c>0</c> for a flow that never ends.</param>
        /// <param name="action">What the light does when the flow has ended.</param>
        /// <exception cref="ArgumentNullException"><paramref name="steps"/> is <c>null</c>, or has a step that is.</exception>
        /// <exception cref="ArgumentException"><paramref name="steps"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative, or <paramref name="action"/> is not a value of <see cref="ColorFlowAction"/>.</exception>
        public ColorFlow(IEnumerable<ColorFlowStep> steps, int count = 0, ColorFlowAction action = ColorFlowAction.RecoverState)
        {
            ArgumentNullException.ThrowIfNull(steps);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (!Enum.IsDefined(action))
            {
                throw new ArgumentOutOfRangeException(nameof(action), action, "That is not a value of ColorFlowAction.");
            }

            var list = steps.ToArray();
            if (list.Length == 0)
            {
                throw new ArgumentException("A colour flow needs at least one step.", nameof(steps));
            }

            if (list.Any(step => step is null))
            {
                throw new ArgumentNullException(nameof(steps), "A step of a colour flow is null.");
            }

            this.Steps = Array.AsReadOnly(list);
            this.Count = count;
            this.Action = action;
        }

        /// <summary>
        /// Gets the number of visible state changes before the flow ends, or <c>0</c> for a flow that never ends.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets what the light does when the flow has ended.
        /// </summary>
        public ColorFlowAction Action { get; }

        /// <summary>
        /// Gets the steps of the flow.
        /// </summary>
        public IReadOnlyList<ColorFlowStep> Steps { get; }

        internal string Expression => string.Join(",", this.Steps.Select(step => step.ToExpression()));

        /// <summary>
        /// Reads a flow as a light reports it in <see cref="YeelightLight.FlowParameters"/>: the count, the action, and the four numbers of every step.
        /// </summary>
        /// <param name="text">The text, for example <c>0,0,1000,2,2700,100,500,1,255,10</c>.</param>
        /// <param name="flow">The flow, when the text is one.</param>
        /// <returns><c>false</c> for a text that is not a flow.</returns>
        public static bool TryParse(string? text, out ColorFlow? flow)
        {
            flow = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 6 || (parts.Length - 2) % 4 != 0)
            {
                return false;
            }

            var numbers = new int[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(parts[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numbers[index]))
                {
                    return false;
                }
            }

            if (numbers[0] < 0 || !Enum.IsDefined((ColorFlowAction)numbers[1]))
            {
                return false;
            }

            var steps = new List<ColorFlowStep>((parts.Length - 2) / 4);
            for (var index = 2; index < parts.Length; index += 4)
            {
                if (!ColorFlowStep.TryCreate(numbers[index], numbers[index + 1], numbers[index + 2], numbers[index + 3], out var step))
                {
                    return false;
                }

                steps.Add(step!);
            }

            flow = new ColorFlow(steps, numbers[0], (ColorFlowAction)numbers[1]);
            return true;
        }

        /// <summary>
        /// Gets the flow as text, in the form that <see cref="TryParse(string?, out ColorFlow?)"/> reads.
        /// </summary>
        /// <returns>The count, the action, and the four numbers of every step.</returns>
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"{this.Count},{(int)this.Action},{this.Expression}");
        }
    }
}
