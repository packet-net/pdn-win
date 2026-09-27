using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PdnWin.App.Controls;

/// <summary>An indicator lamp that glows when lit: TX, DCD, link state.</summary>
public sealed class Lamp : Control
{
    /// <summary>Whether it is lit.</summary>
    public static readonly StyledProperty<bool> IsOnProperty = AvaloniaProperty.Register<Lamp, bool>(nameof(IsOn));

    /// <summary>Its colour when lit.</summary>
    public static readonly StyledProperty<Color> ColorProperty = AvaloniaProperty.Register<Lamp, Color>(nameof(Color), Colors.LimeGreen);

    static Lamp()
    {
        AffectsRender<Lamp>(IsOnProperty, ColorProperty);
    }

    /// <summary>Whether it is lit.</summary>
    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>Its colour when lit.</summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(12, 12);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 1.5;
        if (!IsOn)
        {
            var off = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Color.Parse("#2A333F"), 0), new GradientStop(Color.Parse("#141920"), 1) },
            };
            context.DrawEllipse(off, new Pen(new SolidColorBrush(Color.Parse("#252E3A")), 1), centre, r, r);
            return;
        }

        Color c = Color;
        var glow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(0x70, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
        };
        context.DrawEllipse(glow, null, centre, r * 2.4, r * 2.4);
        var body = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb((byte)Math.Min(255, c.R + 90), (byte)Math.Min(255, c.G + 90), (byte)Math.Min(255, c.B + 90)), 0),
                new GradientStop(c, 0.55),
                new GradientStop(Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)), 1),
            },
        };
        context.DrawEllipse(body, null, centre, r, r);
    }
}
