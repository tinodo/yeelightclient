using Microsoft.UI.Xaml;

namespace Yeelight.TestApp.Views;

/// <summary>Small functions that the XAML of the pages binds to (<c>{x:Bind local:Fmt.Visible(IsConnected)}</c>): what is shown, and what is not.</summary>
public static class Fmt
{
    /// <summary>Shows an element when a condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see cref="Visibility.Visible"/> when it holds.</returns>
    public static Visibility Visible(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shows an element when a condition does not hold.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see cref="Visibility.Collapsed"/> when it holds.</returns>
    public static Visibility Hidden(bool condition) => condition ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shows an element when a text is not empty.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see cref="Visibility.Visible"/> when there is text.</returns>
    public static Visibility VisibleIfText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
