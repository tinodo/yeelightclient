using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Yeelight.TestApp.Controls;

/// <summary>
/// Lays out two children: side by side when there is room for both, and one above the other when there is not. A page that shows a list
/// with its details next to it uses it, so that it works in a window of 900 by 600 pixels as well as in a large one (the width of a page
/// is not the width of the window: the navigation takes a part of it, and that part depends on the width of the window).
/// </summary>
public sealed class ResponsiveSplit : Panel
{
    /// <summary>Identifies the <see cref="Breakpoint"/> property.</summary>
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint),
        typeof(double),
        typeof(ResponsiveSplit),
        new PropertyMetadata(760.0, OnLayoutChanged));

    /// <summary>Identifies the <see cref="Spacing"/> property.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(ResponsiveSplit),
        new PropertyMetadata(16.0, OnLayoutChanged));

    /// <summary>Identifies the <see cref="PrimaryShare"/> property.</summary>
    public static readonly DependencyProperty PrimaryShareProperty = DependencyProperty.Register(
        nameof(PrimaryShare),
        typeof(double),
        typeof(ResponsiveSplit),
        new PropertyMetadata(0.58, OnLayoutChanged));

    /// <summary>Identifies the <see cref="StackedPrimaryShare"/> property.</summary>
    public static readonly DependencyProperty StackedPrimaryShareProperty = DependencyProperty.Register(
        nameof(StackedPrimaryShare),
        typeof(double),
        typeof(ResponsiveSplit),
        new PropertyMetadata(0.5, OnLayoutChanged));

    /// <summary>Gets or sets the width, in pixels, from which the children go side by side.</summary>
    public double Breakpoint
    {
        get => (double)this.GetValue(BreakpointProperty);
        set => this.SetValue(BreakpointProperty, value);
    }

    /// <summary>Gets or sets the space between the two children.</summary>
    public double Spacing
    {
        get => (double)this.GetValue(SpacingProperty);
        set => this.SetValue(SpacingProperty, value);
    }

    /// <summary>Gets or sets the share of the width that the first child gets when they are side by side.</summary>
    public double PrimaryShare
    {
        get => (double)this.GetValue(PrimaryShareProperty);
        set => this.SetValue(PrimaryShareProperty, value);
    }

    /// <summary>Gets or sets the share of the height that the first child gets when they are one above the other.</summary>
    public double StackedPrimaryShare
    {
        get => (double)this.GetValue(StackedPrimaryShareProperty);
        set => this.SetValue(StackedPrimaryShareProperty, value);
    }

    /// <summary>Gets a value indicating whether the children were put side by side the last time that they were arranged.</summary>
    public bool IsSideBySide { get; private set; }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? this.Breakpoint : availableSize.Width;
        var height = availableSize.Height;
        if (this.Children.Count == 0)
        {
            return new Size(width, double.IsInfinity(height) ? 0 : height);
        }

        if (width >= this.Breakpoint)
        {
            var primary = Math.Max(0, (width - this.Spacing) * this.PrimaryShare);
            var secondary = Math.Max(0, width - this.Spacing - primary);
            this.Children[0].Measure(new Size(primary, height));
            if (this.Children.Count > 1)
            {
                this.Children[1].Measure(new Size(secondary, height));
            }

            var wanted = this.Children.Max(child => child.DesiredSize.Height);
            return new Size(width, double.IsInfinity(height) ? wanted : height);
        }

        if (double.IsInfinity(height))
        {
            this.Children[0].Measure(new Size(width, double.PositiveInfinity));
            var total = this.Children[0].DesiredSize.Height;
            if (this.Children.Count > 1)
            {
                this.Children[1].Measure(new Size(width, double.PositiveInfinity));
                total += this.Spacing + this.Children[1].DesiredSize.Height;
            }

            return new Size(width, total);
        }

        var upper = Math.Max(0, (height - this.Spacing) * this.StackedPrimaryShare);
        var lower = Math.Max(0, height - this.Spacing - upper);
        this.Children[0].Measure(new Size(width, upper));
        if (this.Children.Count > 1)
        {
            this.Children[1].Measure(new Size(width, lower));
        }

        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (this.Children.Count == 0)
        {
            return finalSize;
        }

        this.IsSideBySide = finalSize.Width >= this.Breakpoint;
        if (this.IsSideBySide)
        {
            var primary = Math.Max(0, (finalSize.Width - this.Spacing) * this.PrimaryShare);
            var secondary = Math.Max(0, finalSize.Width - this.Spacing - primary);
            this.Children[0].Arrange(new Rect(0, 0, primary, finalSize.Height));
            if (this.Children.Count > 1)
            {
                this.Children[1].Arrange(new Rect(primary + this.Spacing, 0, secondary, finalSize.Height));
            }

            return finalSize;
        }

        var upper = Math.Max(0, (finalSize.Height - this.Spacing) * this.StackedPrimaryShare);
        var lower = Math.Max(0, finalSize.Height - this.Spacing - upper);
        this.Children[0].Arrange(new Rect(0, 0, finalSize.Width, upper));
        if (this.Children.Count > 1)
        {
            this.Children[1].Arrange(new Rect(0, upper + this.Spacing, finalSize.Width, lower));
        }

        return finalSize;
    }

    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ResponsiveSplit)sender).InvalidateMeasure();
    }
}
