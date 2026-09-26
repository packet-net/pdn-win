using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdnWin.Core.Stations;

namespace PdnWin.Controls;

/// <summary>
/// The waterfall: lines scrolling down in the station page's inferno colour map, our own
/// transmissions in a cyan-to-white ramp so they never look like a strong station, and a label on
/// every frame heard at the line it was heard.
/// </summary>
public sealed class WaterfallView : FrameworkElement
{
    /// <summary>Where lines come from.</summary>
    public static readonly DependencyProperty FeedProperty = DependencyProperty.Register(
        nameof(Feed), typeof(BandFeed), typeof(WaterfallView), new PropertyMetadata(null, (d, _) => ((WaterfallView)d).Hook()));

    /// <summary>Hz shown from 0.</summary>
    public static readonly DependencyProperty SpanHzProperty = DependencyProperty.Register(
        nameof(SpanHz), typeof(double), typeof(WaterfallView), new FrameworkPropertyMetadata(3000.0, (d, _) => ((WaterfallView)d).Reset()));

    /// <summary>dBFS at the bottom of the colour map.</summary>
    public static readonly DependencyProperty FloorDbProperty = DependencyProperty.Register(
        nameof(FloorDb), typeof(double), typeof(WaterfallView), new PropertyMetadata(-90.0, (d, _) => ((WaterfallView)d).BuildMap()));

    /// <summary>Whether the colour map follows the noise floor (the station page's Auto).</summary>
    public static readonly DependencyProperty AutoRangeProperty = DependencyProperty.Register(
        nameof(AutoRange), typeof(bool), typeof(WaterfallView), new PropertyMetadata(true, (d, _) => ((WaterfallView)d).BuildMap()));

    /// <summary>dBFS at the top of the colour map.</summary>
    public static readonly DependencyProperty TopDbProperty = DependencyProperty.Register(
        nameof(TopDb), typeof(double), typeof(WaterfallView), new PropertyMetadata(-30.0, (d, _) => ((WaterfallView)d).BuildMap()));

    // pdn-soundmodem waterfall.html: INFERNO and TX_RAMP, anchors evenly spaced.
    private static readonly byte[][] Inferno =
    [
        [0, 0, 4], [12, 8, 38], [36, 12, 79], [66, 10, 104], [93, 18, 110], [120, 28, 109],
        [147, 38, 103], [174, 48, 92], [199, 62, 76], [221, 81, 58], [237, 105, 37], [248, 133, 15],
        [252, 165, 10], [250, 197, 39], [242, 230, 97], [252, 255, 164],
    ];

    private static readonly byte[][] TxRamp =
    [
        [6, 18, 26], [10, 44, 60], [12, 74, 96], [16, 110, 134], [26, 150, 172],
        [70, 188, 205], [140, 215, 226], [205, 238, 243], [255, 255, 255],
    ];

    private static readonly uint[] InfernoLut = Lut(Inferno);
    private static readonly uint[] TxLut = Lut(TxRamp);
    private static readonly Typeface Mono = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush TagBack = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0x0A, 0x0D, 0x11)));
    private static readonly Brush TagCall = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xA8, 0x3C)));
    private static readonly Brush TagTx = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xE3, 0xF7)));
    private static readonly Pen TagPen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xF5, 0xA8, 0x3C))), 1));

    private readonly List<BandLine> _pending = [];
    private readonly List<BandTag> _newTags = [];
    private readonly List<BandTag> _tags = [];
    private readonly byte[] _map = new byte[256];

    // Two lines to a pixel row: 30 lines a second is 15 rows, so a 300 px pane holds 20 s.
    private const int LinesPerRow = 2;
    private byte[] _accumulated = [];
    private int _accumulatedCount;
    private readonly List<BandLine> _rows = [];
    private readonly byte[] _sortScratch = new byte[8192];
    private double _autoFloor = double.NaN;
    private double _mappedFloor = double.NaN;
    private WriteableBitmap? _bitmap;
    private uint[] _row = [];
    private long _top = -1;
    private bool _hooked;

    /// <summary>Creates the view.</summary>
    public WaterfallView()
    {
        BuildMap();
        SizeChanged += (_, _) => Reset();
        ClipToBounds = true;
        Unloaded += (_, _) => Unhook();
        Loaded += (_, _) => Hook();
    }

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

    /// <summary>dBFS at the bottom of the colour map.</summary>
    public double FloorDb
    {
        get => (double)GetValue(FloorDbProperty);
        set => SetValue(FloorDbProperty, value);
    }

    /// <summary>Whether the colour map follows the noise floor.</summary>
    public bool AutoRange
    {
        get => (bool)GetValue(AutoRangeProperty);
        set => SetValue(AutoRangeProperty, value);
    }

    /// <summary>The floor the auto range has settled on, dBFS.</summary>
    public double AutoFloorDb => _autoFloor;

    /// <summary>dBFS at the top of the colour map.</summary>
    public double TopDb
    {
        get => (double)GetValue(TopDbProperty);
        set => SetValue(TopDbProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_bitmap is null)
        {
            return;
        }

        dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));

        // Labels ride down with the line they were stamped on.
        double scale = ActualHeight / _bitmap.PixelHeight / LinesPerRow;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var placed = new List<Rect>();
        foreach (BandTag tag in _tags)
        {
            double y = (_top - tag.Index) * scale;
            if (y < 0 || y > ActualHeight)
            {
                continue;
            }

            double x = Math.Clamp(tag.Hz / SpanHz * ActualWidth, 4, ActualWidth - 4);
            int split = tag.Text.IndexOf(' ', StringComparison.Ordinal);
            string call = split > 0 ? tag.Text[..split] : tag.Text;
            string rest = split > 0 ? tag.Text[split..] : string.Empty;
            var callText = new FormattedText(call, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10.5, tag.Transmitted ? TagTx : TagCall, dpi);
            var restText = new FormattedText(rest, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10.5, Brushes.Gainsboro, dpi);
            double width = callText.Width + restText.Width + 10;
            double left = x + width + 8 > ActualWidth ? x - width - 6 : x + 6;

            // Frames heard together would print over each other; step each one below the last.
            var box = new Rect(left, y - 8, width, 16);
            while (placed.Any(r => r.IntersectsWith(box)))
            {
                box.Offset(0, 17);
            }

            placed.Add(box);
            y = box.Y + 8;
            dc.DrawLine(TagPen, new Point(x, y - 6), new Point(x, y + 6));
            dc.DrawRoundedRectangle(TagBack, null, new Rect(left, y - 8, width, 16), 3, 3);
            dc.DrawText(callText, new Point(left + 5, y - 7));
            dc.DrawText(restText, new Point(left + 5 + callText.Width, y - 7));
        }
    }

    private void Hook()
    {
        if (!_hooked && Feed is not null)
        {
            _hooked = true;
            CompositionTarget.Rendering += OnFrame;
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

    private void Reset()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Max(1, ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Max(1, ActualHeight * dpi.DpiScaleY);
        if (ActualWidth < 1 || ActualHeight < 1)
        {
            _bitmap = null;
            return;
        }

        _bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgr32, null);
        _row = new uint[width];
        InvalidateVisual();
    }

    private void BuildMap()
    {
        // Auto: the floor sits just under the band's noise and the map spans 45 dB above it, so
        // open-squelch hiss reads as a dark ground and a signal stands out whatever the level.
        double floor = AutoRange && !double.IsNaN(_autoFloor) ? _autoFloor - 4 : FloorDb;
        double top = AutoRange && !double.IsNaN(_autoFloor) ? floor + 45 : Math.Max(FloorDb + 1, TopDb);
        _mappedFloor = _autoFloor;
        for (int b = 0; b < 256; b++)
        {
            double db = SpectrumScale.ToDb((byte)b);
            _map[b] = (byte)Math.Clamp((db - floor) / (top - floor) * 255, 0, 255);
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (Feed is not { } feed)
        {
            return;
        }

        _pending.Clear();
        feed.Drain(_pending);
        feed.DrainTags(_newTags);
        if (_pending.Count == 0 && _newTags.Count == 0)
        {
            return;
        }

        if (_newTags.Count > 0)
        {
            _tags.AddRange(_newTags);
            _newTags.Clear();
        }

        _rows.Clear();
        foreach (BandLine line in _pending)
        {
            if (AutoRange && !line.Transmitting)
            {
                TrackFloor(line);
            }

            if (_accumulated.Length != line.Bins.Length)
            {
                _accumulated = new byte[line.Bins.Length];
                _accumulatedCount = 0;
            }

            for (int i = 0; i < line.Bins.Length; i++)
            {
                if (_accumulatedCount == 0 || line.Bins[i] > _accumulated[i])
                {
                    _accumulated[i] = line.Bins[i];
                }
            }

            _accumulatedCount++;
            if (_accumulatedCount >= LinesPerRow)
            {
                _rows.Add(line with { Bins = (byte[])_accumulated.Clone() });
                _accumulatedCount = 0;
            }
        }

        if (_bitmap is { } bitmap && _rows.Count > 0 && IsVisible)
        {
            Scroll(bitmap, _rows);
        }

        if (_pending.Count > 0)
        {
            _top = _pending[^1].Index;
        }

        if (_bitmap is { } b2)
        {
            _tags.RemoveAll(t => (_top - t.Index) / LinesPerRow > b2.PixelHeight);
        }

        InvalidateVisual();
    }

    private void TrackFloor(BandLine line)
    {
        int bins = Math.Min(line.Bins.Length, Math.Min(_sortScratch.Length, (int)(SpanHz / line.BinWidthHz)));
        if (bins < 16)
        {
            return;
        }

        // The quietest quarter of the band is the floor; a median would ride up under a busy
        // channel. Smoothed over a few seconds so a burst does not pump the colours.
        Span<byte> scratch = _sortScratch.AsSpan(0, bins);
        line.Bins.AsSpan(0, bins).CopyTo(scratch);
        scratch.Sort();
        double db = SpectrumScale.ToDb(scratch[bins / 4]);
        _autoFloor = double.IsNaN(_autoFloor) ? db : _autoFloor + ((db - _autoFloor) * 0.02);
        if (double.IsNaN(_mappedFloor) || Math.Abs(_autoFloor - _mappedFloor) > 0.75)
        {
            BuildMap();
        }
    }

    private unsafe void Scroll(WriteableBitmap bitmap, List<BandLine> lines)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        int rows = Math.Min(lines.Count, height);
        bitmap.Lock();
        try
        {
            var pixels = new Span<uint>((void*)bitmap.BackBuffer, bitmap.BackBufferStride / 4 * height);
            int stride = bitmap.BackBufferStride / 4;
            // Everything moves down by the number of new lines; the newest line is row 0.
            pixels[..(stride * (height - rows))].CopyTo(pixels[(stride * rows)..]);
            for (int i = 0; i < rows; i++)
            {
                BandLine line = lines[lines.Count - 1 - i];
                Render(line, width);
                _row.AsSpan(0, width).CopyTo(pixels.Slice(stride * i, width));
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    private void Render(BandLine line, int width)
    {
        uint[] lut = line.Transmitting ? TxLut : InfernoLut;
        double binsPerPixel = SpanHz / line.BinWidthHz / width;
        for (int x = 0; x < width; x++)
        {
            int b0 = (int)(x * binsPerPixel);
            int b1 = Math.Max(b0 + 1, (int)((x + 1) * binsPerPixel));
            byte best = 0;
            for (int b = b0; b < b1 && b < line.Bins.Length; b++)
            {
                if (line.Bins[b] > best)
                {
                    best = line.Bins[b];
                }
            }

            _row[x] = lut[_map[best]];
        }
    }

    private static uint[] Lut(byte[][] anchors)
    {
        var lut = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            double x = i / 255.0 * (anchors.Length - 1);
            int a = (int)Math.Floor(x);
            double f = x - a;
            byte[] c0 = anchors[a], c1 = anchors[Math.Min(a + 1, anchors.Length - 1)];
            uint r = (uint)(c0[0] + ((c1[0] - c0[0]) * f));
            uint g = (uint)(c0[1] + ((c1[1] - c0[1]) * f));
            uint bl = (uint)(c0[2] + ((c1[2] - c0[2]) * f));
            lut[i] = 0xFF000000 | (r << 16) | (g << 8) | bl;
        }

        return lut;
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
