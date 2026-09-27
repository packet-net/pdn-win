using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PdnWin.Core.Stations;
using PdnWin.Presentation;

namespace PdnWin.Controls;

/// <summary>
/// The spectrum: the latest line as a cyan trace over a soft fill, a decaying amber peak-hold,
/// the modem's passband shaded behind, and a frequency ruler along the bottom.
/// </summary>
public sealed class SpectrumView : FrameworkElement
{
    /// <summary>Where lines come from.</summary>
    public static readonly DependencyProperty FeedProperty = DependencyProperty.Register(
        nameof(Feed), typeof(BandFeed), typeof(SpectrumView), new PropertyMetadata(null, (d, _) => ((SpectrumView)d).Restart()));

    /// <summary>Hz shown from 0.</summary>
    public static readonly DependencyProperty SpanHzProperty = DependencyProperty.Register(
        nameof(SpanHz), typeof(double), typeof(SpectrumView), new FrameworkPropertyMetadata(3000.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The modem's passband.</summary>
    public static readonly DependencyProperty PassbandProperty = DependencyProperty.Register(
        nameof(Passband), typeof(Passband), typeof(SpectrumView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double FloorDb = -100;
    private const double TopDb = -10;
    private const double RulerHeight = 20;

    private static readonly Typeface Mono = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0x88, 0x94)));
    private static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xA8, 0x3C)));
    private static readonly Pen Grid = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x7C, 0x88, 0x94))), 1));
    private static readonly Pen TracePen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0xC8, 0xDD))), 1.3) { LineJoin = PenLineJoin.Round });
    private static readonly Pen TxTracePen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xCD, 0xEE, 0xF3))), 1.3) { LineJoin = PenLineJoin.Round });
    private static readonly Pen HoldPen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0xB9, 0x7F, 0x2E))), 1));
    private static readonly Pen MarkPen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xF5, 0xA8, 0x3C))), 1) { DashStyle = new DashStyle([3, 3], 0) });
    private static readonly Pen TickPen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x34, 0x41))), 1));
    private static readonly Brush PassFill = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0x56, 0xC8, 0xDD)));
    private static readonly Brush Fill = Freeze(new LinearGradientBrush(Color.FromArgb(0x66, 0x56, 0xC8, 0xDD), Color.FromArgb(0x04, 0x56, 0xC8, 0xDD), 90));

    private double[] _average = [];
    private double[] _hold = [];
    private long _shown = -1;
    private bool _transmitting;
    private bool _hooked;

    /// <summary>Where lines come from.</summary>
    public BandFeed? Feed
    {
        get => (BandFeed?)GetValue(FeedProperty);
        set => SetValue(FeedProperty, value);
    }

    /// <summary>Hz shown from 0.</summary>
    public double SpanHz
    {
        get => (double)GetValue(SpanHzProperty);
        set => SetValue(SpanHzProperty, value);
    }

    /// <summary>The modem's passband.</summary>
    public Passband? Passband
    {
        get => (Passband?)GetValue(PassbandProperty);
        set => SetValue(PassbandProperty, value);
    }

    /// <summary>Draws a frequency ruler: ticks every 100 Hz, labels every 500.</summary>
    internal static void DrawRuler(DrawingContext dc, double width, double top, double spanHz, double dpi, bool labels = true)
    {
        double step = spanHz > 6000 ? 1000 : 500;
        double minor = step / 5;
        for (double hz = 0; hz <= spanHz; hz += minor)
        {
            double x = Math.Round(hz / spanHz * width) + 0.5;
            bool major = Math.Abs(hz % step) < 0.01;
            dc.DrawLine(TickPen, new Point(x, top), new Point(x, top + (major ? 6 : 3)));
            if (major && labels && hz > 0 && hz < spanHz)
            {
                string text = hz >= 1000 ? (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k" : hz.ToString("0", CultureInfo.InvariantCulture);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10, Muted, dpi);
                dc.DrawText(ft, new Point(x - (ft.Width / 2), top + 6));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight - RulerHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double span = SpanHz;
        double Y(double db) => (1 - ((Math.Clamp(db, FloorDb, TopDb) - FloorDb) / (TopDb - FloorDb))) * h;

        if (Passband is { } pass)
        {
            double x0 = pass.LowHz / span * w, x1 = pass.HighHz / span * w;
            dc.DrawRectangle(PassFill, null, new Rect(x0, 0, Math.Max(0, x1 - x0), h));
            foreach (double mark in pass.Marks)
            {
                double x = Math.Round(mark / span * w) + 0.5;
                dc.DrawLine(MarkPen, new Point(x, 0), new Point(x, h));
            }

            var label = new FormattedText(pass.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10, Amber, dpi);
            dc.DrawText(label, new Point(Math.Max(2, ((x0 + x1) / 2) - (label.Width / 2)), 3));
        }

        for (double db = -20; db > FloorDb; db -= 20)
        {
            double y = Math.Round(Y(db)) + 0.5;
            dc.DrawLine(Grid, new Point(0, y), new Point(w, y));
            var ft = new FormattedText(db.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, Muted, dpi);
            dc.DrawText(ft, new Point(3, y - ft.Height - 1));
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
                // One point per pixel column, the loudest bin in it: a 3 kHz span is 500 bins.
                int columns = (int)Math.Max(1, w);
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
                    if (!started)
                    {
                        t.BeginFigure(point, false, false);
                        a.BeginFigure(new Point(0, h), true, true);
                        a.LineTo(point, false, false);
                        p.BeginFigure(holdPoint, false, false);
                        started = true;
                    }
                    else
                    {
                        t.LineTo(point, true, true);
                        a.LineTo(point, false, false);
                        p.LineTo(holdPoint, true, true);
                    }
                }

                a.LineTo(new Point(columns, h), false, false);
            }

            area.Freeze();
            trace.Freeze();
            hold.Freeze();
            dc.DrawGeometry(Fill, null, area);
            dc.DrawGeometry(null, HoldPen, hold);
            dc.DrawGeometry(null, _transmitting ? TxTracePen : TracePen, trace);
        }

        dc.DrawLine(TickPen, new Point(0, h + 0.5), new Point(w, h + 0.5));
        DrawRuler(dc, w, h, span, dpi);
    }

    private void Restart()
    {
        _shown = -1;
        if (!_hooked && Feed is not null)
        {
            _hooked = true;
            CompositionTarget.Rendering += OnFrame;
            Unloaded += (_, _) => Unhook();
            Loaded += (_, _) =>
            {
                if (!_hooked)
                {
                    _hooked = true;
                    CompositionTarget.Rendering += OnFrame;
                }
            };
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (Feed?.Latest is not { } line || line.Index == _shown || !IsVisible)
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

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Pen FreezePen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
