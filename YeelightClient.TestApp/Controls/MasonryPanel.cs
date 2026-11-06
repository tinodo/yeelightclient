using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Yeelight.TestApp.Controls;

/// <summary>
/// Lays its children out in columns, each child under the shortest column so far: a wall of cards that fills a wide window with two or three
/// columns and a narrow one with a single column, without the gaps that a grid of equal rows leaves.
/// </summary>
public sealed class MasonryPanel : Panel
{
    /// <summary>Identifies the <see cref="MinColumnWidth"/> property.</summary>
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth),
        typeof(double),
        typeof(MasonryPanel),
        new PropertyMetadata(420.0, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="MaxColumns"/> property.</summary>
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns),
        typeof(int),
        typeof(MasonryPanel),
        new PropertyMetadata(3, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="Spacing"/> property.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(MasonryPanel),
        new PropertyMetadata(12.0, OnLayoutPropertyChanged));

    /// <summary>Gets or sets the narrowest that a column may be: the number of columns is what fits.</summary>
    public double MinColumnWidth
    {
        get => (double)this.GetValue(MinColumnWidthProperty);
        set => this.SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>Gets or sets the largest number of columns.</summary>
    public int MaxColumns
    {
        get => (int)this.GetValue(MaxColumnsProperty);
        set => this.SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Gets or sets the space between two columns and between two children in a column.</summary>
    public double Spacing
    {
        get => (double)this.GetValue(SpacingProperty);
        set => this.SetValue(SpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? this.MinColumnWidth : availableSize.Width;
        var (columns, columnWidth) = this.Columns(width);
        var heights = new double[columns];
        foreach (var child in this.Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            var column = Shortest(heights);
            heights[column] += child.DesiredSize.Height + this.Spacing;
        }

        var tallest = heights.Max();
        return new Size(width, Math.Max(0, tallest - this.Spacing));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, columnWidth) = this.Columns(finalSize.Width);
        var heights = new double[columns];
        foreach (var child in this.Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var column = Shortest(heights);
            child.Arrange(new Rect(column * (columnWidth + this.Spacing), heights[column], columnWidth, child.DesiredSize.Height));
            heights[column] += child.DesiredSize.Height + this.Spacing;
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, Math.Max(0, heights.Max() - this.Spacing)));
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((MasonryPanel)sender).InvalidateMeasure();
    }

    private static int Shortest(double[] heights)
    {
        var shortest = 0;
        for (var index = 1; index < heights.Length; index++)
        {
            // A column has to be shorter by a hair, so that equal columns fill from the left.
            if (heights[index] < heights[shortest] - 0.5)
            {
                shortest = index;
            }
        }

        return shortest;
    }

    private (int Columns, double Width) Columns(double width)
    {
        var columns = (int)Math.Floor((width + this.Spacing) / (this.MinColumnWidth + this.Spacing));
        columns = Math.Clamp(columns, 1, Math.Max(1, this.MaxColumns));
        return (columns, Math.Max(0, (width - ((columns - 1) * this.Spacing)) / columns));
    }
}
