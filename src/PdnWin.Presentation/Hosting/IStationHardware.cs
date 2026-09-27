using PdnWin.Core.Settings;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Hosting;

/// <summary>
/// A radio interface's own level control (a Windows endpoint level, an ALSA mixer control),
/// never above its ceiling.
/// </summary>
public interface ILevelControl : IDisposable
{
    /// <summary>The bottom of the range, dB.</summary>
    double MinDb { get; }

    /// <summary>The highest level that will be set: 0 dB, or lower if the device stops short.</summary>
    double CeilingDb { get; }

    /// <summary>The level, dB; writes are clamped to [<see cref="MinDb"/>, <see cref="CeilingDb"/>].</summary>
    double LevelDb { get; set; }

    /// <summary>Raised, on any thread, when the level changes here or anywhere else.</summary>
    event Action? Changed;
}

/// <summary>A radio interface, opened and ready for the soundmodem.</summary>
/// <param name="Card">Its audio and PTT.</param>
/// <param name="Interface">The settings as they resolved against the devices present (IDs and
/// paths can move between runs); the caller stores them if they changed.</param>
/// <param name="Receive">The receive level control, where the platform has one.</param>
/// <param name="Transmit">The transmit level control, where the platform has one.</param>
/// <param name="Warnings">Things that are wrong and could not be put right, for the operator.</param>
public sealed record OpenedInterface(
    ISoundCard Card,
    InterfaceSettings Interface,
    ILevelControl? Receive,
    ILevelControl? Transmit,
    IReadOnlyList<string> Warnings);

/// <summary>A radio interface found on this machine, for the settings dialog.</summary>
/// <param name="Name">Its name, e.g. "AIOC Audio".</param>
/// <param name="Kind">"AIOC", "CM108" or "SOUND CARD".</param>
/// <param name="Detail">One line: in and out, HID PTT, the COM port.</param>
/// <param name="IsRadio">Whether it looks like a radio interface rather than the machine's own audio.</param>
/// <param name="Key">What identifies it, for matching against stored settings.</param>
public sealed record InterfaceOption(string Name, string Kind, string Detail, bool IsRadio, string Key);

/// <summary>What the settings dialog finds.</summary>
/// <param name="Options">Complete interfaces (audio both ways), radio interfaces first.</param>
/// <param name="Current">The one the stored settings name, if it is present.</param>
/// <param name="Summary">A line for the dialog: how many devices, how many radio interfaces.</param>
/// <param name="SerialPorts">Every COM or tty port, for serial PTT.</param>
public sealed record DiscoveryResult(
    IReadOnlyList<InterfaceOption> Options,
    InterfaceOption? Current,
    string Summary,
    IReadOnlyList<string> SerialPorts);

/// <summary>
/// The platform's radio interfaces: finding them, putting their audio settings right, opening
/// them. Windows has one (WASAPI, HID PTT, endpoint hygiene); a Linux one (ALSA, hidraw, sysfs
/// discovery) is the next to write. A front-end without one can still run the simulator.
/// </summary>
public interface IStationHardware
{
    /// <summary>Finds the interfaces present.</summary>
    Task<DiscoveryResult> DiscoverAsync(InterfaceSettings current, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settings for <paramref name="option"/>, keeping what the operator chose in
    /// <paramref name="previous"/> where it still applies and suggesting the rest (PTT method,
    /// HID path, serial lines).
    /// </summary>
    InterfaceSettings Suggest(InterfaceOption option, InterfaceSettings previous);

    /// <summary>
    /// Opens the interface <paramref name="settings"/> name: finds it, puts its audio settings
    /// right (reporting each fix through <paramref name="note"/>), applies the stored levels and
    /// opens audio and PTT. Null when it is not plugged in. <paramref name="faulted"/> is called,
    /// on any thread, if it fails or goes away later.
    /// </summary>
    Task<OpenedInterface?> OpenAsync(
        InterfaceSettings settings, Action<string> note, Action<Exception> faulted, CancellationToken cancellationToken = default);
}
