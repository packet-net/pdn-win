using System.Windows;
using System.Windows.Media;

namespace PdnWin.Controls;

/// <summary>
/// The station page's input meter: -60 to 0 dBFS with the target band painted in green, red from
/// -6, a grey bar below -30, the RMS as a hairline and a slowly falling peak-hold tick. The bar
/// eases towards each new reading rather than jumping, which is what makes it read as a meter.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    /// <summary>Peak, dBFS.</summary>
    public static readonly DependencyProperty PeakDbProperty = DependencyProperty.Register(
        nameof(PeakDb), typeof(double), typeof(LevelMeter), new PropertyMetadata(-120.0, OnReading));

    /// <summary>RMS, dBFS.</summary>
    public static readonly DependencyProperty RmsDbProperty = DependencyProperty.Register(
        nameof(RmsDb), typeof(double), typeof(LevelMeter), new PropertyMetadata(-120.0, OnReading));

    /// <summary>Bottom of the band that reads as good.</summary>
    public static readonly DependencyProperty ZoneLowDbProperty = DependencyProperty.Register(
        nameof(ZoneLowDb), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(-18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Top of the band that reads as good.</summary>
    public static readonly DependencyProperty ZoneHighDbProperty = DependencyProperty.Register(
        nameof(ZoneHighDb), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(-9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MinDb = -60;
    private const double MaxDb = 0;
    private const double HotDb = -6;
    private const double QuietDb = -30;

    private static readonly Brush Back = Frozen(Color.FromRgb(0x0C, 0x10, 0x16));
    private static readonly Pen Edge = new Pen(Frozen(Color.FromRgb(0x1E, 0x26, 0x32)), 1).GetAsFrozen() as Pen ?? throw new InvalidOperationException();
    private static readonly Brush Zone = Frozen(Color.FromArgb(0x4C, 0x5A, 0xC8, 0x78));
    private static readonly Brush HotZone = Frozen(Color.FromArgb(0x4C, 0xE2, 0x50, 0x3C));
    private static readonly Brush GoodBar = Gradient(Color.FromRgb(0x4F, 0xB8, 0x7C), Color.FromRgb(0x9B, 0xF0, 0xBB));
    private static readonly Brush QuietBar = Gradient(Color.FromRgb(0x4D, 0x58, 0x66), Color.FromRgb(0x7C, 0x88, 0x94));
    private static readonly Brush HotBar = Gradient(Color.FromRgb(0xB8, 0x36, 0x26), Color.FromRgb(0xFF, 0x6E, 0x55));
    private static readonly Pen RmsPen = new Pen(Frozen(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), 1).GetAsFrozen() as Pen ?? throw new InvalidOperationException();
    private static readonly Pen HoldPen = new Pen(Frozen(Color.FromRgb(0xF5, 0xA8, 0x3C)), 2).GetAsFrozen() as Pen ?? throw new InvalidOperationException();

    private double _shownPeak = MinDb;
    private double _hold = MinDb;
    private DateTime _holdSince;
    private bool _animating;

    /// <summary>Peak, dBFS.</summary>
    public double PeakDb
    {
        get => (double)GetValue(PeakDbProperty);
        set => SetValue(PeakDbProperty, value);
    }

    /// <summary>RMS, dBFS.</summary>
    public double RmsDb
    {
        get => (double)GetValue(RmsDbProperty);
        set => SetValue(RmsDbProperty, value);
    }

    /// <summary>Bottom of the good band.</summary>
    public double ZoneLowDb
    {
        get => (double)GetValue(ZoneLowDbProperty);
        set => SetValue(ZoneLowDbProperty, value);
    }

    /// <summary>Top of the good band.</summary>
    public double ZoneHighDb
    {
        get => (double)GetValue(ZoneHighDbProperty);
        set => SetValue(ZoneHighDbProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 140 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 12 : Math.Min(availableSize.Height, 14));

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        dc.DrawRoundedRectangle(Back, Edge, rect, 3, 3);

        double X(double db) => 1 + ((Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb) * (w - 2));
        dc.DrawRectangle(Zone, null, new Rect(X(ZoneLowDb), 1, X(ZoneHighDb) - X(ZoneLowDb), h - 2));
        dc.DrawRectangle(HotZone, null, new Rect(X(HotDb), 1, X(MaxDb) - X(HotDb), h - 2));

        double peak = _shownPeak;
        Brush bar = peak > HotDb ? HotBar : peak < QuietDb ? QuietBar : GoodBar;
        double barWidth = X(peak) - 1;
        if (barWidth > 0)
        {
            dc.DrawRoundedRectangle(bar, null, new Rect(1, 3, barWidth, h - 6), 1.5, 1.5);
        }

        if (RmsDb > MinDb)
        {
            double x = Math.Round(X(RmsDb)) + 0.5;
            dc.DrawLine(RmsPen, new Point(x, 1), new Point(x, h - 1));
        }

        if (_hold > MinDb + 1)
        {
            double x = X(_hold);
            dc.DrawLine(HoldPen, new Point(x, 2), new Point(x, h - 2));
        }
    }

    private static void OnReading(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var meter = (LevelMeter)d;
        if (meter.PeakDb >= meter._hold)
        {
            meter._hold = meter.PeakDb;
            meter._holdSince = DateTime.UtcNow;
        }

        if (!meter._animating)
        {
            meter._animating = true;
            CompositionTarget.Rendering += meter.Tick;
        }
    }

    private void Tick(object? sender, EventArgs e)
    {
        double target = Math.Max(MinDb, PeakDb);
        // Fast attack, slower release, as a real meter's ballistics.
        double rate = target > _shownPeak ? 0.55 : 0.18;
        _shownPeak += (target - _shownPeak) * rate;

        if ((DateTime.UtcNow - _holdSince).TotalSeconds > 1.2)
        {
            _hold = Math.Max(target, _hold - 0.6);
        }

        InvalidateVisual();
        if (Math.Abs(target - _shownPeak) < 0.05 && Math.Abs(_hold - target) < 0.1)
        {
            _animating = false;
            CompositionTarget.Rendering -= Tick;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, 0);
        brush.Freeze();
        return brush;
    }
}
