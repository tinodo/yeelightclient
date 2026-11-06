using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Yeelight.TestApp.Controls;

/// <summary>
/// Lays its children out in rows, from left to right, and starts a new row when the next child does not fit: for buttons and small
/// groups of controls that have to work in a narrow window as well as in a wide one.
/// </summary>
public sealed class WrapPanel : Panel
{
    /// <summary>Identifies the <see cref="HorizontalSpacing"/> property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0.0, OnSpacingChanged));

    /// <summary>Identifies the <see cref="VerticalSpacing"/> property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0.0, OnSpacingChanged));

    /// <summary>Gets or sets the space between two children in a row.</summary>
    public double HorizontalSpacing
    {
        get => (double)this.GetValue(HorizontalSpacingProperty);
        set => this.SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Gets or sets the space between two rows.</summary>
    public double VerticalSpacing
    {
        get => (double)this.GetValue(VerticalSpacingProperty);
        set => this.SetValue(VerticalSpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var rowWidth = 0.0;
        var rowHeight = 0.0;
        var totalHeight = 0.0;
        var widest = 0.0;
        var first = true;
        foreach (var child in this.Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (!first && rowWidth + this.HorizontalSpacing + size.Width > availableSize.Width)
            {
                totalHeight += rowHeight + this.VerticalSpacing;
                widest = Math.Max(widest, rowWidth);
                rowWidth = size.Width;
                rowHeight = size.Height;
            }
            else
            {
                rowWidth += (first ? 0 : this.HorizontalSpacing) + size.Width;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            first = false;
        }

        widest = Math.Max(widest, rowWidth);
        return new Size(double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width), totalHeight + rowHeight);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        var start = 0;
        var width = 0.0;
        var height = 0.0;
        var count = this.Children.Count;
        for (var index = 0; index < count; index++)
        {
            var size = this.Children[index].DesiredSize;
            if (index > start && width + this.HorizontalSpacing + size.Width > finalSize.Width)
            {
                this.ArrangeRow(start, index, y, height);
                y += height + this.VerticalSpacing;
                start = index;
                width = 0;
                height = 0;
            }

            width += (index > start ? this.HorizontalSpacing : 0) + size.Width;
            height = Math.Max(height, size.Height);
        }

        if (count > start)
        {
            this.ArrangeRow(start, count, y, height);
            y += height;
        }

        // The whole slot is taken, so that a panel that is stretched in a tall cell is not centred in it.
        return new Size(finalSize.Width, Math.Max(finalSize.Height, y));
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((WrapPanel)sender).InvalidateMeasure();
    }

    // A child is given the height of its row, so that its vertical alignment (a label next to a button) is honoured.
    private void ArrangeRow(int start, int end, double y, double height)
    {
        var x = 0.0;
        for (var index = start; index < end; index++)
        {
            var child = this.Children[index];
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width + this.HorizontalSpacing;
        }
    }
}
