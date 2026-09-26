using PdnWin.Core.Stations;

namespace PdnWin.Core.Levels;

/// <summary>What the operator should do to the radio's volume.</summary>
public enum RxLevelVerdict
{
    /// <summary>Not enough readings yet.</summary>
    Measuring,

    /// <summary>Nothing, or next to nothing, is arriving from the radio.</summary>
    NoAudio,

    /// <summary>The squelch is closing between signals; the station runs open squelch.</summary>
    SquelchClosing,

    /// <summary>Audio is arriving but below the target.</summary>
    TooQuiet,

    /// <summary>In the target band.</summary>
    Good,

    /// <summary>Above the target band but not yet clipping.</summary>
    TooLoud,

    /// <summary>The card is running out of codes.</summary>
    Clipping,
}

/// <summary>The advisor's current view.</summary>
/// <param name="Verdict">What to do.</param>
/// <param name="PeakDbFs">The highest peak in the window.</param>
/// <param name="RmsDbFs">The average RMS in the window.</param>
/// <param name="Instruction">One sentence for the operator.</param>
/// <param name="Settled">The level has been good for long enough to call it set.</param>
public sealed record RxLevelAdvice(RxLevelVerdict Verdict, double PeakDbFs, double RmsDbFs, string Instruction, bool Settled);

/// <summary>
/// Guides the operator to set a handheld's volume knob for an open-squelch FM receive path.
/// </summary>
/// <remarks>
/// <para><b>Open squelch changes what "level" means.</b> With the squelch open an FM receiver
/// with nothing to hear produces full discriminator noise, which is the loudest thing on the
/// channel; a signal quiets it. So the level to set is the noise's, and the target is the
/// soundmodem's own meter band: peaks between -18 and -9 dBFS, never into the red from -6, and
/// never clipping. Clipping is the failure that costs decodes; a quiet signal costs almost
/// nothing (pdn-soundmodem docs/04-levels.md).</para>
/// <para><b>It also watches for a closing squelch.</b> Open-squelch noise is steady; audio that
/// keeps dropping away by tens of dB between bursts is a squelch doing its job, which the modem's
/// carrier sense and its decoders both do worse with.</para>
/// <para>Readings are judged over a short window, so one loud burst does not flip the verdict
/// and the peak reported is the window's worst.</para>
/// </remarks>
public sealed class RxLevelAdvisor
{
    /// <summary>The bottom of the target band.</summary>
    public const double TargetLowDbFs = -18;

    /// <summary>The top of the target band.</summary>
    public const double TargetHighDbFs = -9;

    /// <summary>Where red starts.</summary>
    public const double HotDbFs = -6;

    /// <summary>Below this there is no useful audio.</summary>
    public const double NoAudioDbFs = -45;

    private const int Window = 10;        // 2 s of 200 ms readings
    private const int SettleReadings = 15; // 3 s in the band

    private readonly Queue<LevelReading> _readings = new();
    private int _goodRun;

    /// <summary>Takes one reading and returns the advice it leads to.</summary>
    public RxLevelAdvice Add(LevelReading reading)
    {
        _readings.Enqueue(reading);
        while (_readings.Count > Window)
        {
            _readings.Dequeue();
        }

        RxLevelAdvice advice = Judge();
        _goodRun = advice.Verdict == RxLevelVerdict.Good ? _goodRun + 1 : 0;
        return advice with { Settled = _goodRun >= SettleReadings };
    }

    /// <summary>Forgets everything, as when the operator starts again.</summary>
    public void Reset()
    {
        _readings.Clear();
        _goodRun = 0;
    }

    private RxLevelAdvice Judge()
    {
        double peak = _readings.Max(r => r.PeakDbFs);
        double rms = 10 * Math.Log10(_readings.Average(r => Math.Pow(10, r.RmsDbFs / 10)));
        if (_readings.Count < 3)
        {
            return new(RxLevelVerdict.Measuring, peak, rms, "Listening...", false);
        }

        if (_readings.Any(r => r.Clipped))
        {
            return new(RxLevelVerdict.Clipping, peak, rms,
                "Clipping: turn the radio's volume down until the CLIP light stays dark.", false);
        }

        if (peak < NoAudioDbFs)
        {
            return new(RxLevelVerdict.NoAudio, peak, rms,
                "Nothing from the radio: open the squelch fully and turn the volume up.", false);
        }

        double quietest = _readings.Min(r => r.RmsDbFs);
        double loudest = _readings.Max(r => r.RmsDbFs);
        if (loudest - quietest > 20 && quietest < NoAudioDbFs)
        {
            return new(RxLevelVerdict.SquelchClosing, peak, rms,
                "The audio keeps dropping out: the squelch is closing. Set it fully open.", false);
        }

        if (peak > HotDbFs)
        {
            return new(RxLevelVerdict.TooLoud, peak, rms,
                $"Too loud ({peak:0} dBFS): turn the radio's volume down a little.", false);
        }

        if (peak > TargetHighDbFs)
        {
            return new(RxLevelVerdict.TooLoud, peak, rms,
                $"A little hot ({peak:0} dBFS): back the volume off a touch.", false);
        }

        if (peak < TargetLowDbFs)
        {
            return new(RxLevelVerdict.TooQuiet, peak, rms,
                $"Quiet ({peak:0} dBFS): turn the radio's volume up.", false);
        }

        return new(RxLevelVerdict.Good, peak, rms, $"Good ({peak:0} dBFS). Leave the volume here.", false);
    }
}
