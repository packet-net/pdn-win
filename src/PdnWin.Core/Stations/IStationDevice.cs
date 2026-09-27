using Packet.Ax25.Transport;

namespace PdnWin.Core.Stations;

/// <summary>
/// The hardware a station talks through: whatever turns AX.25 frames into RF and back.
/// </summary>
/// <remarks>
/// <para><b>This is the seam for other hardware.</b> The in-process soundmodem is one
/// implementation (<see cref="SoundModem.SoundModemStation"/>). A NinoTNC on a serial port, a KISS
/// TNC over TCP or a remote pdn-soundmodem would each be another: all of them reduce to a packet.net
/// <see cref="IAx25Transport"/> for the session layer, plus a monitor feed. Everything else a
/// device can do is an optional feature interface (<see cref="ISpectrumFeature"/>,
/// <see cref="IInputLevelFeature"/>, <see cref="IModeFeature"/>, <see cref="ITxTestFeature"/>,
/// <see cref="IChannelAccessFeature"/>), and the UI shows the panes and controls for the features
/// the device has, so a NinoTNC simply has no waterfall.</para>
/// <para><b>Events are raised on device threads.</b> Consumers marshal to their own.</para>
/// </remarks>
public interface IStationDevice : IAsyncDisposable
{
    /// <summary>What to call it in the UI, e.g. "AIOC Audio".</summary>
    string DisplayName { get; }

    /// <summary>The frame transport the session layer (packet.net's listener) runs over.</summary>
    IAx25Transport Transport { get; }

    /// <summary>
    /// Every frame the device heard or sent, for the monitor. A superset of what
    /// <see cref="Transport"/> delivers upward: a device may show the operator frames it does not
    /// hand to the host (the soundmodem's monitor-only IL2P frames).
    /// </summary>
    event Action<HeardFrame>? FrameHeard;

    /// <summary>Raised when the transmitter keys (true) and unkeys (false).</summary>
    event Action<bool>? TransmittingChanged;

    /// <summary>A line worth showing the operator: a device fault, a refused transmission.</summary>
    event Action<StationNotice>? Notice;
}

/// <summary>Which way a frame went.</summary>
public enum FrameDirection
{
    /// <summary>Heard off air.</summary>
    Received,

    /// <summary>Sent by us.</summary>
    Transmitted,
}

/// <summary>One frame for the monitor.</summary>
/// <param name="Time">When it was decoded, or when it finished going out.</param>
/// <param name="Direction">Heard or sent.</param>
/// <param name="Bytes">The AX.25 frame, no flags or FCS.</param>
/// <param name="Quality">Receive diagnostics, where the device has them.</param>
/// <param name="Hold">For one of ours, how long it waited for the channel and why, where the device
/// measures it.</param>
public sealed record HeardFrame(DateTimeOffset Time, FrameDirection Direction, byte[] Bytes, HeardFrameQuality? Quality = null, TransmitHold? Hold = null);

/// <summary>How long one of our frames waited to go out, and what held it.</summary>
/// <param name="For">From being queued to being keyed up.</param>
/// <param name="Because">The cause that took most of it ("5.2s channel busy"), or null when no
/// one cause took half.</param>
public sealed record TransmitHold(TimeSpan For, string? Because);

/// <summary>What the device could say about a received frame. Every field is optional.</summary>
/// <param name="Mode">The mode that decoded it, e.g. "afsk1200".</param>
/// <param name="SnrDb">In-band signal to noise, when worth showing.</param>
/// <param name="FrequencyOffsetHz">How far off tune the sender was.</param>
/// <param name="PeakDbFs">Peak level over the frame, when the mode's slicer reads level.</param>
/// <param name="LevelVerdict">"TOO LOUD" or "TOO QUIET" where the level cost the mode something.</param>
/// <param name="CorrectedBytes">FEC corrections, for the FEC modes.</param>
/// <param name="MonitorOnly">Shown to the operator but not handed to the session layer.</param>
public sealed record HeardFrameQuality(
    string? Mode = null,
    double? SnrDb = null,
    double? FrequencyOffsetHz = null,
    double? PeakDbFs = null,
    string? LevelVerdict = null,
    int? CorrectedBytes = null,
    bool MonitorOnly = false);

/// <summary>How much a notice matters.</summary>
public enum NoticeLevel
{
    /// <summary>For information.</summary>
    Info,

    /// <summary>Something the operator should look at.</summary>
    Warning,

    /// <summary>Something has stopped working.</summary>
    Error,
}

/// <summary>A line for the operator.</summary>
public sealed record StationNotice(NoticeLevel Level, string Text);
