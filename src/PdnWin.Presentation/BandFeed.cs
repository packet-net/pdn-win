using System.Collections.Concurrent;
using PdnWin.Core.Stations;

namespace PdnWin.Presentation;

/// <summary>A spectrum line as the display keeps it.</summary>
/// <param name="Index">Line number.</param>
/// <param name="Bins">A private copy of the bins.</param>
/// <param name="BinWidthHz">Hertz per bin.</param>
/// <param name="Transmitting">Whether we were keyed when it was taken.</param>
public sealed record BandLine(long Index, byte[] Bins, double BinWidthHz, bool Transmitting);

/// <summary>A label on the waterfall: who was heard, at the line they were heard on.</summary>
/// <param name="Index">The line it belongs to.</param>
/// <param name="Hz">Where across the band.</param>
/// <param name="Text">What it says; the callsign is the part before the first space.</param>
/// <param name="Transmitted">Our own frame.</param>
public sealed record BandTag(long Index, double Hz, string Text, bool Transmitted);

/// <summary>
/// The hand-off between the station's receive thread and the spectrum and waterfall controls.
/// The station pushes lines as they come (30 a second); the controls take them on the UI thread
/// at render time, so nothing is marshalled per line.
/// </summary>
public sealed class BandFeed
{
    private readonly ConcurrentQueue<BandLine> _lines = new();
    private readonly ConcurrentQueue<BandTag> _tags = new();
    private volatile BandLine? _latest;
    private long _lastIndex;

    /// <summary>The newest line, for the spectrum.</summary>
    public BandLine? Latest => _latest;

    /// <summary>The newest line's index.</summary>
    public long LastIndex => Interlocked.Read(ref _lastIndex);

    /// <summary>Takes a line from the station. Called on its receive thread.</summary>
    public void Push(SpectrumLine line, bool transmitting)
    {
        var copy = new BandLine(line.Index, line.Bins.ToArray(), line.BinWidthHz, transmitting);
        _latest = copy;
        Interlocked.Exchange(ref _lastIndex, line.Index);
        _lines.Enqueue(copy);
        while (_lines.Count > 240)
        {
            _lines.TryDequeue(out _);
        }
    }

    /// <summary>Labels the current line.</summary>
    public void Tag(double hz, string text, bool transmitted) => _tags.Enqueue(new BandTag(LastIndex, hz, text, transmitted));

    /// <summary>Takes every line that has arrived since the last call.</summary>
    public int Drain(List<BandLine> into)
    {
        int n = 0;
        while (_lines.TryDequeue(out BandLine? line))
        {
            into.Add(line);
            n++;
        }

        return n;
    }

    /// <summary>Takes every tag that has arrived since the last call.</summary>
    public void DrainTags(List<BandTag> into)
    {
        while (_tags.TryDequeue(out BandTag? tag))
        {
            into.Add(tag);
        }
    }

    /// <summary>Forgets everything, as when the station restarts.</summary>
    public void Clear()
    {
        _lines.Clear();
        _tags.Clear();
        _latest = null;
    }
}

/// <summary>A shaded part of the band: a modem's passband.</summary>
/// <param name="LowHz">Lower edge.</param>
/// <param name="HighHz">Upper edge.</param>
/// <param name="Label">What it is, e.g. "0 afsk1200".</param>
/// <param name="Marks">Tone frequencies worth a dashed line (mark and space).</param>
public sealed record Passband(double LowHz, double HighHz, string Label, IReadOnlyList<double> Marks);
