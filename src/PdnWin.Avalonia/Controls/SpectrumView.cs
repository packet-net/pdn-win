using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using PdnWin.Core.Stations;
using PdnWin.Presentation;

namespace PdnWin.Ava.Controls;

/// <summary>
/// The spectrum (as the WPF one): the latest line as a cyan trace over a soft fill, a decaying
/// amber peak-hold, the modem's passband shaded behind, a frequency ruler along the bottom.
/// </summary>
public sealed class SpectrumView : Control
{
    /// <summary>Where lines come from.</summary>
    public static readonly StyledProperty<BandFeed?> FeedProperty = AvaloniaProperty.Register<SpectrumView, BandFeed?>(nameof(Feed));

    /// <summary>Hz shown from 0.</summary>
    public static readonly StyledProperty<double> SpanHzProperty = AvaloniaProperty.Register<SpectrumView, double>(nameof(SpanHz), 3000);

    /// <summary>The modem's passband.</summary>
    public static readonly StyledProperty<Passband?> PassbandProperty = AvaloniaProperty.Register<SpectrumView, Passband?>(nameof(Passband));

    private const double FloorDb = -100;
    private const double TopDb = -10;
    private const double RulerHeight = 20;

    private static readonly Typeface Mono = new(Palette.Mono);
    private static readonly IPen Grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#307C8894")), 1);
    private static readonly IPen TracePen = new ImmutablePen(Palette.Trace, 1.3, lineJoin: PenLineJoin.Round);
    private static readonly IPen TxTracePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#CDEEF3")), 1.3, lineJoin: PenLineJoin.Round);
    private static readonly IPen HoldPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#90B97F2E")), 1);
    private static readonly IPen MarkPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#70F5A83C")), 1, new ImmutableDashStyle([3, 3], 0));
    private static readonly IPen TickPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#2A3441")), 1);
    private static readonly IBrush PassFill = new ImmutableSolidColorBrush(Color.Parse("#1A56C8DD"));
    private static readonly IBrush Fill = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#6656C8DD"), 0), new GradientStop(Color.Parse("#0456C8DD"), 1) },
    }.ToImmutable();

    private readonly DispatcherTimer _frames;
    private double[] _average = [];
    private double[] _hold = [];
    private long _shown = -1;
    private bool _transmitting;

    /// <summary>Creates the view.</summary>
    public SpectrumView()
    {
        _frames = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnFrame());
        AffectsRender<SpectrumView>(SpanHzProperty, PassbandProperty);
    }

    /// <summary>Where lines come from.</summary>
    public BandFeed? Feed
    {
        get => GetValue(FeedProperty);
        set => SetValue(FeedProperty, value);
    }

    /// <summary>Hz shown from 0.</summary>
    public double SpanHz
    {
        get => GetValue(SpanHzProperty);
        set => SetValue(SpanHzProperty, value);
    }

    /// <summary>The modem's passband.</summary>
    public Passband? Passband
    {
        get => GetValue(PassbandProperty);
        set => SetValue(PassbandProperty, value);
    }

    /// <summary>Draws a frequency ruler: ticks every 100 Hz, labels every 500.</summary>
    internal static void DrawRuler(DrawingContext dc, double width, double top, double spanHz)
    {
        double step = spanHz > 6000 ? 1000 : 500;
        double minor = step / 5;
        for (double hz = 0; hz <= spanHz; hz += minor)
        {
            double x = Math.Round(hz / spanHz * width) + 0.5;
            bool major = Math.Abs(hz % step) < 0.01;
            dc.DrawLine(TickPen, new Point(x, top), new Point(x, top + (major ? 6 : 3)));
            if (major && hz > 0 && hz < spanHz)
            {
                string text = hz >= 1000 ? (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k" : hz.ToString("0", CultureInfo.InvariantCulture);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10, Palette.Muted);
                dc.DrawText(ft, new Point(x - (ft.Width / 2), top + 6));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _frames.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frames.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height - RulerHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        double span = SpanHz;
        double Y(double db) => (1 - ((Math.Clamp(db, FloorDb, TopDb) - FloorDb) / (TopDb - FloorDb))) * h;

        if (Passband is { } pass)
        {
            double x0 = pass.LowHz / span * w, x1 = pass.HighHz / span * w;
            context.DrawRectangle(PassFill, null, new Rect(x0, 0, Math.Max(0, x1 - x0), h));
            foreach (double mark in pass.Marks)
            {
                double x = Math.Round(mark / span * w) + 0.5;
                context.DrawLine(MarkPen, new Point(x, 0), new Point(x, h));
            }

            var label = new FormattedText(pass.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10, Palette.Amber);
            context.DrawText(label, new Point(Math.Max(2, ((x0 + x1) / 2) - (label.Width / 2)), 3));
        }

        for (double db = -20; db > FloorDb; db -= 20)
        {
            double y = Math.Round(Y(db)) + 0.5;
            context.DrawLine(Grid, new Point(0, y), new Point(w, y));
            var ft = new FormattedText(db.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, Palette.Muted);
            context.DrawText(ft, new Point(3, y - ft.Height - 1));
        }

        if (_average.Length > 0 && Feed?.Latest is { } latest)
        {
            double binWidth = latest.BinWidthHz;
            int bins = Math.Min(_average.Length, (int)(span / binWidth) + 1);
            var trace = new StreamGeometry();
            var area = new StreamGeometry();
            var hold = new StreamGeometry();
            using (StreamGeometryContext t = trace.Open())
            using (StreamGeometryContext a = area.Open())
            using (StreamGeometryContext p = hold.Open())
            {
                int columns = (int)Math.Max(1, w);
                a.BeginFigure(new Point(0, h), true);
                bool started = false;
                for (int col = 0; col <= columns; col++)
                {
                    int b0 = (int)(col / w * span / binWidth);
                    int b1 = Math.Max(b0 + 1, (int)((col + 1) / w * span / binWidth));
                    double best = FloorDb, bestHold = FloorDb;
                    for (int b = b0; b < b1 && b < bins; b++)
                    {
                        best = Math.Max(best, _average[b]);
                        bestHold = Math.Max(bestHold, _hold[b]);
                    }

                    var point = new Point(col, Y(best));
                    var holdPoint = new Point(col, Y(bestHold));
                    a.LineTo(point);
                    if (!started)
                    {
                        t.BeginFigure(point, false);
                        p.BeginFigure(holdPoint, false);
                        started = true;
                    }
                    else
                    {
                        t.LineTo(point);
                        p.LineTo(holdPoint);
                    }
                }

                a.LineTo(new Point(columns, h));
                a.EndFigure(true);
                t.EndFigure(false);
                p.EndFigure(false);
            }

            context.DrawGeometry(Fill, null, area);
            context.DrawGeometry(null, HoldPen, hold);
            context.DrawGeometry(null, _transmitting ? TxTracePen : TracePen, trace);
        }

        context.DrawLine(TickPen, new Point(0, h + 0.5), new Point(w, h + 0.5));
        DrawRuler(context, w, h, span);
    }

    private void OnFrame()
    {
        if (Feed?.Latest is not { } line || line.Index == _shown || !IsEffectivelyVisible)
        {
            return;
        }

        _shown = line.Index;
        _transmitting = line.Transmitting;
        if (_average.Length != line.Bins.Length)
        {
            _average = new double[line.Bins.Length];
            _hold = new double[line.Bins.Length];
            Array.Fill(_average, FloorDb);
            Array.Fill(_hold, FloorDb);
        }

        for (int i = 0; i < line.Bins.Length; i++)
        {
            double db = SpectrumScale.ToDb(line.Bins[i]);
            _average[i] += (db - _average[i]) * 0.35;
            _hold[i] = Math.Max(_average[i], _hold[i] - 0.25);
        }

        InvalidateVisual();
    }
}
