using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace PdnWin.App.Controls;

/// <summary>
/// The station page's input meter: -60 to 0 dBFS, the target band in green, red
/// from -6, grey below -30, an RMS hairline and a falling peak-hold, easing towards each reading.
/// </summary>
public sealed class LevelMeter : Control
{
    /// <summary>Peak, dBFS.</summary>
    public static readonly StyledProperty<double> PeakDbProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(PeakDb), -120);

    /// <summary>RMS, dBFS.</summary>
    public static readonly StyledProperty<double> RmsDbProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(RmsDb), -120);

    private const double MinDb = -60;
    private const double HotDb = -6;
    private const double QuietDb = -30;
    private const double ZoneLowDb = -18;
    private const double ZoneHighDb = -9;

    private static readonly IBrush Back = new ImmutableSolidColorBrush(Color.Parse("#0C1016"));
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1E2632")), 1);
    private static readonly IBrush Zone = new ImmutableSolidColorBrush(Color.Parse("#4C5AC878"));
    private static readonly IBrush HotZone = new ImmutableSolidColorBrush(Color.Parse("#4CE2503C"));
    private static readonly IBrush GoodBar = Gradient("#4FB87C", "#9BF0BB");
    private static readonly IBrush QuietBar = Gradient("#4D5866", "#7C8894");
    private static readonly IBrush HotBar = Gradient("#B83626", "#FF6E55");
    private static readonly IPen RmsPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#A0FFFFFF")), 1);
    private static readonly IPen HoldPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#F5A83C")), 2);

    private readonly DispatcherTimer _animation;
    private double _shownPeak = MinDb;
    private double _hold = MinDb;
    private DateTime _holdSince;

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(PeakDbProperty, RmsDbProperty);
    }

    /// <summary>Creates the meter.</summary>
    public LevelMeter()
    {
        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    /// <summary>Peak, dBFS.</summary>
    public double PeakDb
    {
        get => GetValue(PeakDbProperty);
        set => SetValue(PeakDbProperty, value);
    }

    /// <summary>RMS, dBFS.</summary>
    public double RmsDb
    {
        get => GetValue(RmsDbProperty);
        set => SetValue(RmsDbProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PeakDbProperty)
        {
            if (PeakDb >= _hold)
            {
                _hold = PeakDb;
                _holdSince = DateTime.UtcNow;
            }

            _animation.Start();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 140 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 12 : Math.Min(availableSize.Height, 22));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.DrawRectangle(Back, Edge, new Rect(0.5, 0.5, w - 1, h - 1), 3, 3);

        double X(double db) => 1 + ((Math.Clamp(db, MinDb, 0) - MinDb) / -MinDb * (w - 2));
        context.DrawRectangle(Zone, null, new Rect(X(ZoneLowDb), 1, X(ZoneHighDb) - X(ZoneLowDb), h - 2));
        context.DrawRectangle(HotZone, null, new Rect(X(HotDb), 1, X(0) - X(HotDb), h - 2));

        double peak = _shownPeak;
        IBrush bar = peak > HotDb ? HotBar : peak < QuietDb ? QuietBar : GoodBar;
        double barWidth = X(peak) - 1;
        if (barWidth > 0)
        {
            context.DrawRectangle(bar, null, new Rect(1, 3, barWidth, Math.Max(0, h - 6)), 1.5, 1.5);
        }

        if (RmsDb > MinDb)
        {
            double x = Math.Round(X(RmsDb)) + 0.5;
            context.DrawLine(RmsPen, new Point(x, 1), new Point(x, h - 1));
        }

        if (_hold > MinDb + 1)
        {
            double x = X(_hold);
            context.DrawLine(HoldPen, new Point(x, 2), new Point(x, h - 2));
        }
    }

    private void Tick()
    {
        double target = Math.Max(MinDb, PeakDb);
        double rate = target > _shownPeak ? 0.55 : 0.18;
        _shownPeak += (target - _shownPeak) * rate;
        if ((DateTime.UtcNow - _holdSince).TotalSeconds > 1.2)
        {
            _hold = Math.Max(target, _hold - 0.6);
        }

        InvalidateVisual();
        if (Math.Abs(target - _shownPeak) < 0.05 && Math.Abs(_hold - target) < 0.1)
        {
            _animation.Stop();
        }
    }

    private static IBrush Gradient(string from, string to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
    }.ToImmutable();
}
