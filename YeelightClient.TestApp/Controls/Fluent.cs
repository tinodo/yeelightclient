using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yeelight.TestApp.Services;

namespace Yeelight.TestApp.Controls;

/// <summary>
/// Looks up the styles of the app (App.xaml) and of Fluent 2 (the Windows App SDK) for the controls that are built in code.
/// Colors always come from styles that use theme resources, so they follow the light, dark and high contrast themes.
/// </summary>
internal static class Fluent
{
    /// <summary>The unit of the spacing grid of Fluent 2.</summary>
    public const double Unit = 4;

    /// <summary>Gets a style by key, or null when there is none (the control then keeps its default look).</summary>
    /// <param name="key">The key of the style.</param>
    /// <returns>The style.</returns>
    public static Style? StyleOf(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    /// <summary>Creates text in one of the styles of the type ramp, such as BodyStrongTextBlockStyle.</summary>
    /// <param name="text">The text.</param>
    /// <param name="styleKey">The key of the style.</param>
    /// <param name="wrap">Whether the text wraps.</param>
    /// <returns>The text block.</returns>
    public static TextBlock Text(string text, string styleKey, bool wrap = true)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (StyleOf(styleKey) is { } style)
        {
            block.Style = style;
        }

        return block;
    }

    /// <summary>Creates a line of secondary text in the caption size.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text block.</returns>
    public static TextBlock Caption(string text) => Text(text, "SecondaryCaptionStyle");

    /// <summary>Creates a panel with the look of a card of Fluent 2: a layer with a thin border and rounded corners.</summary>
    /// <param name="content">What the card holds.</param>
    /// <returns>The card.</returns>
    public static Border Card(UIElement content)
    {
        var card = new Border { Child = content };
        if (StyleOf("CardStyle") is { } style)
        {
            card.Style = style;
        }

        return card;
    }

    /// <summary>Creates a small label with a quiet background, for the name of a method.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The chip.</returns>
    public static Border Chip(string text)
    {
        var chip = new Border { Child = new TextBlock { Text = text, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["CodeFontFamily"], FontSize = 12 } };
        if (StyleOf("ChipStyle") is { } style)
        {
            chip.Style = style;
        }

        return chip;
    }

    /// <summary>Shows how a command ended on an info bar: green when the light accepted it, yellow when it or the client refused it, red when it did not get through.</summary>
    /// <param name="bar">The info bar.</param>
    /// <param name="outcome">What came of the command.</param>
    public static void Show(InfoBar bar, Outcome outcome)
    {
        bar.Severity = outcome.Severity switch
        {
            OutcomeSeverity.Success => InfoBarSeverity.Success,
            OutcomeSeverity.Informational => InfoBarSeverity.Informational,
            OutcomeSeverity.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
        bar.Title = outcome.Title;
        bar.Message = outcome.Message;
        bar.IsOpen = true;
    }

    /// <summary>Shows a message on an info bar.</summary>
    /// <param name="bar">The info bar.</param>
    /// <param name="severity">How it looks.</param>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    public static void Show(InfoBar bar, InfoBarSeverity severity, string title, string message)
    {
        bar.Severity = severity;
        bar.Title = title;
        bar.Message = message;
        bar.IsOpen = true;
    }
}
