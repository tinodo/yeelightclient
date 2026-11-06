using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Yeelight.TestApp.Models;

namespace Yeelight.TestApp.Controls;

/// <summary>A button that shows a colour, and opens a colour picker when it is pressed.</summary>
public sealed partial class ColorButton : UserControl
{
    private Color _color = Colors.White;
    private bool _updating;

    /// <summary>Initializes a new instance of the <see cref="ColorButton"/> class.</summary>
    public ColorButton()
    {
        this.InitializeComponent();
        this.Show();
    }

    /// <summary>Occurs when a person chose another colour, or <see cref="Color"/> was set to another.</summary>
    public event EventHandler? ColorChanged;

    /// <summary>Gets or sets the colour.</summary>
    public Color Color
    {
        get => this._color;
        set
        {
            if (this._color == value)
            {
                return;
            }

            this._color = value;
            this.Show();
            this.ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets the name that a screen reader says for the button.</summary>
    public string AccessibleName
    {
        get => AutomationProperties.GetName(this.Inner);
        set => AutomationProperties.SetName(this.Inner, value);
    }

    /// <summary>Gets the colour as a number as the protocol has it.</summary>
    internal int Rgb => new Rgb24(this._color.R, this._color.G, this._color.B).Value;

    /// <summary>Gets the button that opens the picker; for the smoke test.</summary>
    internal Button Button => this.Inner;

    private void Show()
    {
        this._updating = true;
        this.Swatch.Background = new SolidColorBrush(this._color);
        this.Label.Text = new Rgb24(this._color.R, this._color.G, this._color.B).Hex;
        if (this.Picker.Color != this._color)
        {
            this.Picker.Color = this._color;
        }

        this._updating = false;
    }

    private void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!this._updating)
        {
            this.Color = Color.FromArgb(255, args.NewColor.R, args.NewColor.G, args.NewColor.B);
        }
    }
}
