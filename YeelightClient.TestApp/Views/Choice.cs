namespace Yeelight.TestApp.Views;

/// <summary>An entry of a list to choose from: what it stands for, and the words that are shown.</summary>
/// <typeparam name="T">The type of what it stands for.</typeparam>
/// <param name="Value">What the entry stands for.</param>
/// <param name="Text">The words that are shown.</param>
internal sealed record Choice<T>(T Value, string Text)
{
    /// <inheritdoc/>
    public override string ToString() => this.Text;
}
