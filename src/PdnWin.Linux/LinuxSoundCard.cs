using M0LTE.Radio.Audio;
using Packet.SoundModem.Channel;
using PdnWin.Core.Settings;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Hardware;

/// <summary>
/// A radio interface on Linux, opened: ALSA capture and playback at 48 kHz (straight to the card,
/// or through PipeWire when the desktop will not let go of it) and whichever PTT the settings name.
/// </summary>
public sealed class LinuxSoundCard : ISoundCard
{
    /// <summary>The rate the card runs at: every USB interface seen does 48 kHz natively.</summary>
    public const int SampleRate = 48000;

    private readonly FaultReportingInput _input;
    private readonly AlsaAudioOutput _output;
    private readonly IPttControl _ptt;
    private readonly IDisposable? _owned;

    private LinuxSoundCard(string name, AlsaAudioInput input, AlsaAudioOutput output, IPttControl ptt, IDisposable? owned)
    {
        Name = name;
        _input = new FaultReportingInput(input, ex => Faulted?.Invoke(ex));
        _output = output;
        _ptt = ptt;
        _owned = owned;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IAudioInput Input => _input;

    /// <inheritdoc />
    public IAudioOutput Output => _output;

    /// <inheritdoc />
    public IPttControl Ptt => _ptt;

    /// <summary>Raised if the audio input fails (the interface was unplugged).</summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Opens capture on <paramref name="capturePcm"/>, playback on <paramref name="playbackPcm"/>
    /// and the PTT the settings describe. Releases anything it opened on failure.
    /// <paramref name="owned"/> (the card's mixer) is disposed with the card.
    /// </summary>
    public static LinuxSoundCard Open(InterfaceSettings settings, string capturePcm, string playbackPcm, IDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AlsaAudioInput? input = null;
        AlsaAudioOutput? output = null;
        IPttControl? ptt = null;
        try
        {
            // PTT first, so that a transmitter that cannot be keyed (or released) stops the station
            // before any audio is opened.
            ptt = OpenPtt(settings);
            input = new AlsaAudioInput(capturePcm, SampleRate);
            output = new AlsaAudioOutput(playbackPcm, SampleRate);
            return new LinuxSoundCard(settings.Name ?? "sound card", input, output, ptt, owned);
        }
        catch
        {
            (ptt as IDisposable)?.Dispose();
            output?.Dispose();
            input?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // PTT first: whatever else happens, the transmitter must be released.
        (_ptt as IDisposable)?.Dispose();
        _output.Dispose();
        _input.Dispose();
        _owned?.Dispose();
    }

    private static IPttControl OpenPtt(InterfaceSettings settings) => settings.Ptt switch
    {
        PttKind.Cm108Hid when settings.HidPath is { Length: > 0 } path => new Cm108Ptt(path, settings.Gpio),
        PttKind.Serial when settings.SerialPort is { Length: > 0 } port =>
            new SerialPtt(port, useRts: settings.SerialRts, useDtr: settings.SerialDtr),
        PttKind.None => new NullPtt(),
        _ => throw new InvalidOperationException("PTT is not configured: choose the HID device or serial port in Settings"),
    };

    /// <summary>
    /// The capture stream, reporting the failure that ends it. ALSA says an unplugged card by
    /// throwing from the read, where WASAPI raises an event; either way the station has to hear
    /// about it to wait for the interface to come back.
    /// </summary>
    private sealed class FaultReportingInput(AlsaAudioInput inner, Action<Exception> faulted) : IAudioInput, IDisposable
    {
        private int _disposed;

        public int SampleRate => inner.SampleRate;

        public int Read(Span<float> destination)
        {
            try
            {
                return inner.Read(destination);
            }
            catch (Exception ex) when (Volatile.Read(ref _disposed) == 0)
            {
                faulted(ex);
                throw;
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            inner.Dispose();
        }
    }
}
