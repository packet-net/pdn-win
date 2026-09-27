using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using PdnWin.Core.Stations;
using PdnWin.Presentation;

namespace PdnWin.Ava.Controls;

/// <summary>
/// The waterfall (as the WPF one): inferno lines scrolling down, our own transmissions in the
/// cyan-to-white ramp, labels on decoded frames, the colour range following the noise floor.
/// </summary>
public sealed class WaterfallView : Control
{
    /// <summary>Where lines come from.</summary>
    public static readonly StyledProperty<BandFeed?> FeedProperty = AvaloniaProperty.Register<WaterfallView, BandFeed?>(nameof(Feed));

    /// <summary>Hz shown from 0.</summary>
    public static readonly StyledProperty<double> SpanHzProperty = AvaloniaProperty.Register<WaterfallView, double>(nameof(SpanHz), 3000);

    private const int LinesPerRow = 2;

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
    private static readonly Typeface Mono = new(Palette.Mono, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush TagBack = new ImmutableSolidColorBrush(Color.Parse("#C80A0D11"));
    private static readonly IBrush TagTx = new ImmutableSolidColorBrush(Color.Parse("#7FE3F7"));
    private static readonly IBrush TagRest = new ImmutableSolidColorBrush(Color.Parse("#DCDCDC"));
    private static readonly IPen TagPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#B0F5A83C")), 1);

    private readonly DispatcherTimer _frames;
    private readonly List<BandLine> _pending = [];
    private readonly List<BandLine> _rows = [];
    private readonly List<BandTag> _newTags = [];
    private readonly List<BandTag> _tags = [];
    private readonly byte[] _map = new byte[256];
    private readonly byte[] _sortScratch = new byte[8192];
    private WriteableBitmap? _bitmap;
    private uint[] _row = [];
    private byte[] _accumulated = [];
    private int _accumulatedCount;
    private long _top = -1;
    private double _autoFloor = double.NaN;
    private double _mappedFloor = double.NaN;

    /// <summary>Creates the view.</summary>
    public WaterfallView()
    {
        ClipToBounds = true;
        BuildMap();
        _frames = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnFrame());
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
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Reset();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (_bitmap is null)
        {
            return;
        }

        context.DrawImage(_bitmap, new Rect(Bounds.Size));

        double scale = Bounds.Height / _bitmap.PixelSize.Height / LinesPerRow;
        var placed = new List<Rect>();
        foreach (BandTag tag in _tags)
        {
            double y = (_top - tag.Index) * scale;
            if (y < 0 || y > Bounds.Height)
            {
                continue;
            }

            double x = Math.Clamp(tag.Hz / SpanHz * Bounds.Width, 4, Bounds.Width - 4);
            int split = tag.Text.IndexOf(' ', StringComparison.Ordinal);
            string call = split > 0 ? tag.Text[..split] : tag.Text;
            string rest = split > 0 ? tag.Text[split..] : string.Empty;
            var callText = new FormattedText(call, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10.5, tag.Transmitted ? TagTx : Palette.Amber);
            var restText = new FormattedText(rest, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 10.5, TagRest);
            double width = callText.Width + restText.Width + 10;
            double left = x + width + 8 > Bounds.Width ? x - width - 6 : x + 6;
            var box = new Rect(left, y - 8, width, 16);
            while (placed.Any(r => r.Intersects(box)))
            {
                box = box.Translate(new Vector(0, 17));
            }

            placed.Add(box);
            y = box.Y + 8;
            context.DrawLine(TagPen, new Point(x, y - 6), new Point(x, y + 6));
            context.DrawRectangle(TagBack, null, box, 3, 3);
            context.DrawText(callText, new Point(left + 5, y - 7));
            context.DrawText(restText, new Point(left + 5 + callText.Width, y - 7));
        }
    }

    private void Reset()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = (int)Math.Max(1, Bounds.Width * scaling);
        int height = (int)Math.Max(1, Bounds.Height * scaling);
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            _bitmap = null;
            return;
        }

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer fb = _bitmap.Lock())
        {
            // Black, not transparent garbage, until the first lines arrive.
            var black = new byte[fb.RowBytes];
            for (int i = 3; i < black.Length; i += 4)
            {
                black[i] = 0xFF;
            }

            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(black, 0, fb.Address + (y * fb.RowBytes), fb.RowBytes);
            }
        }

        _row = new uint[width];
        InvalidateVisual();
    }

    private void BuildMap()
    {
        double floor = double.IsNaN(_autoFloor) ? -90 : _autoFloor - 4;
        double top = floor + 45;
        _mappedFloor = _autoFloor;
        for (int b = 0; b < 256; b++)
        {
            double db = SpectrumScale.ToDb((byte)b);
            _map[b] = (byte)Math.Clamp((db - floor) / (top - floor) * 255, 0, 255);
        }
    }

    private void TrackFloor(BandLine line)
    {
        int bins = Math.Min(line.Bins.Length, Math.Min(_sortScratch.Length, (int)(SpanHz / line.BinWidthHz)));
        if (bins < 16)
        {
            return;
        }

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

    private void OnFrame()
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

        _tags.AddRange(_newTags);
        _newTags.Clear();

        _rows.Clear();
        foreach (BandLine line in _pending)
        {
            if (!line.Transmitting)
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

            if (++_accumulatedCount >= LinesPerRow)
            {
                _rows.Add(line with { Bins = (byte[])_accumulated.Clone() });
                _accumulatedCount = 0;
            }
        }

        if (_bitmap is { } bitmap && _rows.Count > 0 && IsEffectivelyVisible)
        {
            Scroll(bitmap, _rows);
        }

        if (_pending.Count > 0)
        {
            _top = _pending[^1].Index;
        }

        if (_bitmap is { } b2)
        {
            _tags.RemoveAll(t => (_top - t.Index) / LinesPerRow > b2.PixelSize.Height);
        }

        InvalidateVisual();
    }

    private unsafe void Scroll(WriteableBitmap bitmap, List<BandLine> lines)
    {
        int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
        int rows = Math.Min(lines.Count, height);
        using ILockedFramebuffer fb = bitmap.Lock();
        int stride = fb.RowBytes / 4;
        var pixels = new Span<uint>((void*)fb.Address, stride * height);
        pixels[..(stride * (height - rows))].CopyTo(pixels[(stride * rows)..]);
        for (int i = 0; i < rows; i++)
        {
            Render(lines[lines.Count - 1 - i], width);
            _row.AsSpan(0, width).CopyTo(pixels.Slice(stride * i, width));
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
}
