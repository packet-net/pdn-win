using M0LTE.Radio.Audio;

namespace PdnWin.Core.Stations.SoundModem;

/// <summary>
/// A sound card and its PTT, opened: what <see cref="SoundModemStation"/> runs over. The Windows
/// app supplies one over WASAPI and a CM108/AIOC HID or serial PTT; the tests supply one over
/// in-memory audio.
/// </summary>
public interface ISoundCard : IDisposable
{
    /// <summary>What to call it, e.g. "AIOC Audio".</summary>
    string Name { get; }

    /// <summary>Audio from the radio, at the card's rate.</summary>
    IAudioInput Input { get; }

    /// <summary>Audio to the radio, at the same rate as <see cref="Input"/>.</summary>
    IAudioOutput Output { get; }

    /// <summary>The transmitter key.</summary>
    IPttControl Ptt { get; }
}
