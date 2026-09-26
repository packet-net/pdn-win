namespace PdnWin.Core.Stations;

/// <summary>A device that can show the audio band: spectrum and waterfall.</summary>
public interface ISpectrumFeature
{
    /// <summary>
    /// One line per display frame: a dB-scaled byte per bin from 0 Hz upward, on an absolute
    /// scale (see <see cref="SpectrumScale"/>). The buffer is reused; copy it to keep it. Raised on
    /// the device's receive thread.
    /// </summary>
    event Action<SpectrumLine>? SpectrumLine;
}

/// <summary>One spectrum line.</summary>
/// <param name="Index">Increasing line number, for placing tags on the waterfall.</param>
/// <param name="Bins">dB-scaled bytes, bin 0 = DC.</param>
/// <param name="BinWidthHz">Hertz per bin.</param>
/// <param name="LinesPerSecond">How many lines a second arrive.</param>
public readonly record struct SpectrumLine(long Index, ReadOnlyMemory<byte> Bins, double BinWidthHz, int LinesPerSecond);

/// <summary>The spectrum byte scale: byte 0 is <see cref="FloorDb"/>, byte 255 is 0 dBFS.</summary>
public static class SpectrumScale
{
    /// <summary>dBFS at byte 0.</summary>
    public const double FloorDb = -100;

    /// <summary>A byte back to dBFS.</summary>
    public static double ToDb(byte value) => FloorDb + (value * -FloorDb / 255.0);
}

/// <summary>A device that meters the audio it receives.</summary>
public interface IInputLevelFeature
{
    /// <summary>A reading every 200 ms or so. Raised on the device's receive thread.</summary>
    event Action<LevelReading>? InputLevel;
}

/// <summary>One level meter reading.</summary>
/// <param name="PeakDbFs">Peak over the interval.</param>
/// <param name="RmsDbFs">RMS over the interval.</param>
/// <param name="Clipped">Whether any sample the card delivered sat on a rail.</param>
public readonly record struct LevelReading(double PeakDbFs, double RmsDbFs, bool Clipped);

/// <summary>A device whose modulation can be chosen.</summary>
public interface IModeFeature
{
    /// <summary>The modes it offers.</summary>
    IReadOnlyList<string> Modes { get; }

    /// <summary>The mode in use.</summary>
    string Mode { get; }

    /// <summary>Changes mode. Sessions stay up; frames queued in the old mode are dropped.</summary>
    Task SetModeAsync(string mode, CancellationToken cancellationToken = default);
}

/// <summary>A device that can transmit a test tone for setting transmit level.</summary>
public interface ITxTestFeature
{
    /// <summary>Whether a test is on the air (or waiting for the channel).</summary>
    bool TxTestRunning { get; }

    /// <summary>Sends a test, waiting for a clear channel like any transmission. Completes when
    /// it has finished, been stopped, or been refused.</summary>
    Task RunTxTestAsync(TxTestRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ends a running test.</summary>
    void StopTxTest();
}

/// <summary>A test transmission.</summary>
/// <param name="TwoTone">700 + 1900 Hz (the SSB check) rather than one tone (the FM check).</param>
/// <param name="ToneHz">The single tone's frequency.</param>
/// <param name="Seconds">How long; capped by the device.</param>
public sealed record TxTestRequest(bool TwoTone, double ToneHz, double Seconds);

/// <summary>A device whose channel access (KISS TXDELAY and friends) can be tuned.</summary>
public interface IChannelAccessFeature
{
    /// <summary>The current parameters.</summary>
    ChannelAccess ChannelAccess { get; }

    /// <summary>Applies new parameters.</summary>
    void SetChannelAccess(ChannelAccess access);
}

/// <summary>KISS-style channel access parameters.</summary>
/// <param name="TxDelayMs">Keyup to data: long enough for the radio to be on air and the far
/// receiver to open. Handhelds need more than mobiles.</param>
/// <param name="Persistence">p-persistence, 0-255 (p = (value + 1) / 256).</param>
/// <param name="SlotTimeMs">Slot time.</param>
/// <param name="TxTailMs">Audio kept flowing after the last frame.</param>
public sealed record ChannelAccess(int TxDelayMs = 300, int Persistence = 63, int SlotTimeMs = 100, int TxTailMs = 30);
