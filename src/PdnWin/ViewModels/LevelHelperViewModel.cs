using CommunityToolkit.Mvvm.ComponentModel;
using PdnWin.Core.Levels;
using PdnWin.Core.Stations;

namespace PdnWin.ViewModels;

/// <summary>A transmit test tone and the deviation its carrier null calibrates.</summary>
/// <param name="ToneHz">The tone.</param>
/// <param name="DeviationHz">Where the carrier nulls (first Bessel zero, index 2.405).</param>
public sealed record TxTestPreset(double ToneHz, double DeviationHz)
{
    /// <summary>For the picker.</summary>
    public string Label => $"{ToneHz:0} Hz  -  null at {DeviationHz / 1000:0.0} kHz deviation";

    /// <summary>The station page's presets: 1.2, 2.4, 3.0 and 5.0 kHz.</summary>
    public static IReadOnlyList<TxTestPreset> All { get; } =
        Packet.SoundModem.Audio.TestTone.BesselNullTonesHz
            .Select(t => new TxTestPreset(t, Packet.SoundModem.Audio.TestTone.BesselNullDeviationHz(t)))
            .ToList();
}

/// <summary>The level helper pane: set the handheld's volume, then the transmit level.</summary>
public sealed partial class LevelHelperViewModel : ObservableObject
{
    private readonly RxLevelAdvisor _advisor = new();

    [ObservableProperty]
    private RxLevelVerdict _verdict = RxLevelVerdict.Measuring;

    [ObservableProperty]
    private string _instruction = "Waiting for audio from the radio...";

    [ObservableProperty]
    private double _peakDb = -120;

    [ObservableProperty]
    private bool _settled;

    /// <summary>Takes a reading. UI thread.</summary>
    public void Add(LevelReading reading)
    {
        RxLevelAdvice advice = _advisor.Add(reading);
        Verdict = advice.Verdict;
        Instruction = advice.Instruction;
        PeakDb = advice.PeakDbFs;
        Settled = advice.Settled;
    }

    /// <summary>Starts again, as after a change of radio.</summary>
    public void Reset()
    {
        _advisor.Reset();
        Verdict = RxLevelVerdict.Measuring;
        Instruction = "Waiting for audio from the radio...";
        Settled = false;
    }
}
