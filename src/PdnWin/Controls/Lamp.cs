using System.Windows;
using System.Windows.Media;

namespace PdnWin.Controls;

/// <summary>An indicator lamp that glows when lit: TX, DCD, link state.</summary>
public sealed class Lamp : FrameworkElement
{
    /// <summary>Whether it is lit.</summary>
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(Lamp), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Its colour when lit.</summary>
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(Lamp), new FrameworkPropertyMetadata(Colors.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether it is lit.</summary>
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>Its colour when lit.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(12, 12);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = Math.Min(ActualWidth, ActualHeight) / 2 - 1.5;
        if (!IsOn)
        {
            var off = new RadialGradientBrush(Color.FromRgb(0x2A, 0x33, 0x3F), Color.FromRgb(0x14, 0x19, 0x20));
            dc.DrawEllipse(off, new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x2E, 0x3A)), 1), centre, r, r);
            return;
        }

        Color c = Color;
        var glow = new RadialGradientBrush(Color.FromArgb(0x70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
        dc.DrawEllipse(glow, null, centre, r * 2.4, r * 2.4);
        var body = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            GradientStops =
            {
                new GradientStop(Color.FromRgb((byte)Math.Min(255, c.R + 90), (byte)Math.Min(255, c.G + 90), (byte)Math.Min(255, c.B + 90)), 0),
                new GradientStop(c, 0.55),
                new GradientStop(Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)), 1),
            },
        };
        dc.DrawEllipse(body, null, centre, r, r);
    }
}
