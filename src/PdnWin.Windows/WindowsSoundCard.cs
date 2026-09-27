using M0LTE.Radio.Audio;
using Packet.SoundModem.Channel;
using Packet.SoundModem.Windows;
using PdnWin.Core.Settings;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Hardware;

/// <summary>
/// A radio interface on Windows, opened: WASAPI capture and render at 48 kHz and whichever PTT the
/// settings name.
/// </summary>
public sealed class WindowsSoundCard : ISoundCard
{
    private readonly WasapiAudioInput _input;
    private readonly WasapiAudioOutput _output;
    private readonly IPttControl _ptt;

    private WindowsSoundCard(string name, WasapiAudioInput input, WasapiAudioOutput output, IPttControl ptt)
    {
        Name = name;
        _input = input;
        _output = output;
        _ptt = ptt;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IAudioInput Input => _input;

    /// <inheritdoc />
    public IAudioOutput Output => _output;

    /// <inheritdoc />
    public IPttControl Ptt => _ptt;

    /// <summary>Raised if either direction of the audio fails (the interface was unplugged).</summary>
    public event Action<Exception>? Faulted;

    /// <summary>Opens the interface the settings describe. Releases anything it opened on failure.</summary>
    public static WindowsSoundCard Open(InterfaceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WasapiAudioInput? input = null;
        WasapiAudioOutput? output = null;
        IPttControl? ptt = null;
        try
        {
            input = new WasapiAudioInput(settings.CaptureEndpointId);
            output = new WasapiAudioOutput(settings.RenderEndpointId);
            ptt = OpenPtt(settings);
            var card = new WindowsSoundCard(settings.Name ?? "sound card", input, output, ptt);
            input.Faulted += ex => card.Faulted?.Invoke(ex);
            output.Faulted += ex => card.Faulted?.Invoke(ex);
            return card;
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
    }

    private static IPttControl OpenPtt(InterfaceSettings settings) => settings.Ptt switch
    {
        PttKind.Cm108Hid when settings.HidPath is { Length: > 0 } path => OpenHid(path, settings.Gpio),
        PttKind.Serial when settings.SerialPort is { Length: > 0 } port =>
            new SerialPtt(port, useRts: settings.SerialRts, useDtr: settings.SerialDtr),
        PttKind.None => new NullPtt(),
        _ => throw new InvalidOperationException("PTT is not configured: choose the HID device or serial port in Settings"),
    };

    private static HidPtt OpenHid(string path, int gpio)
    {
        // The report length comes from the device; look it up so a collection whose output report
        // is longer than five bytes (padded variants) is written the size it expects.
        HidDevice? device = HidDevices.List().FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        return device is null
            ? throw new InvalidOperationException("the PTT HID device is not present; is the interface plugged in?")
            : new HidPtt(device, gpio);
    }
}
