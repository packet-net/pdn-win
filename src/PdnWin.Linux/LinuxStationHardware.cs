using System.IO.Ports;
using Packet.SoundModem.Audio;
using Packet.SoundModem.Linux;
using PdnWin.Core.Settings;
using PdnWin.Hosting;

namespace PdnWin.Hardware;

/// <summary>
/// Radio interfaces on Linux: discovery by USB device in sysfs, the checks that say what the
/// operator cannot open and why, mixer hygiene (nothing above 0 dB, AGC and boost off, monitor
/// paths closed), mixer levels, and ALSA with CM108 or serial PTT, through pdn-soundmodem-linux
/// and the pdn-soundmodem core. The card is opened directly; when the desktop's sound server holds
/// it, the station goes through PipeWire instead and says so.
/// </summary>
public sealed class LinuxStationHardware : IStationHardware, IDisposable
{
    /// <summary>What to tell an operator who cannot open the interface's device nodes.</summary>
    public const string PermissionHelp =
        "pdn-lin's udev rule gives these to the logged-in user and the audio group (the package installs it; replug the interface after installing)";

    private readonly DeviceWatcher _watcher = new();

    /// <summary>Starts watching for interfaces arriving and leaving.</summary>
    public LinuxStationHardware()
    {
        _watcher.Changed += () => DevicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public event Action? DevicesChanged;

    /// <inheritdoc />
    public async Task<DiscoveryResult> DiscoverAsync(InterfaceSettings current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        IReadOnlyList<RadioInterface> found = await Task.Run(RadioInterfaces.Discover, cancellationToken).ConfigureAwait(false);
        int radios = found.Count(r => r.IsComplete && r.Kind != RadioInterfaceKind.SoundCard);
        string summary = found.Count == 0
            ? "No sound cards found."
            : $"{found.Count} sound card{(found.Count == 1 ? "" : "s")}, {radios} radio interface{(radios == 1 ? "" : "s")}."
              + (radios == 0 ? " Plug in an AIOC or CM108 interface; it will appear here." : string.Empty);

        // Say now, not at start-up, if the interface this would pick cannot be opened.
        RadioInterface? likely = LinuxInterfaceResolver.Resolve(current, found)?.Found
            ?? found.FirstOrDefault(r => r.IsComplete && r.Kind != RadioInterfaceKind.SoundCard);
        if (likely is not null && Describe(DeviceAccess.Check(likely, likely.SuggestedPtt)) is { } access)
        {
            summary += " " + access;
        }

        List<InterfaceOption> options = found.Where(r => r.IsComplete).Select(Describe).ToList();
        InterfaceOption? matched = LinuxInterfaceResolver.Resolve(current, found) is { } resolved
            ? options.FirstOrDefault(o => o.Key == resolved.Found.Key)
            : null;
        return new DiscoveryResult(options, matched, summary, SerialPorts(found));
    }

    /// <inheritdoc />
    public InterfaceSettings Suggest(InterfaceOption option, InterfaceSettings previous)
    {
        ArgumentNullException.ThrowIfNull(option);
        RadioInterface? found = RadioInterfaces.Discover().FirstOrDefault(r => r.Key == option.Key);
        return found is null ? previous : LinuxInterfaceResolver.From(found, previous);
    }

    /// <inheritdoc />
    public async Task<OpenedInterface?> OpenAsync(
        InterfaceSettings settings, Action<string> note, Action<Exception> faulted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(faulted);
        return await Task.Run(() => Open(settings, note, faulted), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _watcher.Dispose();

    private static OpenedInterface? Open(InterfaceSettings settings, Action<string> note, Action<Exception> faulted)
    {
        if (LinuxInterfaceResolver.Resolve(settings, RadioInterfaces.Discover()) is not { } resolved)
        {
            return null;
        }

        InterfaceSettings iface = resolved.Settings;
        RadioInterface radio = resolved.Found;
        AlsaCard card = radio.Card!;
        PttMethod ptt = iface.Ptt switch
        {
            PttKind.Cm108Hid => PttMethod.Cm108Hid,
            PttKind.Serial => PttMethod.Serial,
            _ => PttMethod.None,
        };

        // The mixer and the transmitter key are ours alone: nobody else opens them, so a refusal
        // there is permissions and nothing else. The PCMs are checked by opening them, below,
        // because a busy card and a forbidden one need different answers.
        IReadOnlyList<AccessProblem> problems = DeviceAccess.Check(radio, ptt, iface.SerialPort)
            .Where(p => p.Purpose is DevicePurpose.Ptt || p.Missing)
            .ToList();
        if (Describe(problems) is { } refused)
        {
            throw new InvalidOperationException(refused);
        }

        var warnings = new List<string>();
        AlsaMixer? mixer = null;
        LevelControl? receive = null;
        LevelControl? transmit = null;
        try
        {
            // All mixer work before a PCM is opened: pdn-soundmodem's rule, for the reason it
            // gives (a capture stream started and then left waiting overruns).
            if (AlsaMixer.TryOpen(card.Mixer, out mixer, out string why))
            {
                warnings.AddRange(MixerHygiene.Fix(mixer!, fix => note($"fixed {radio.Name}: {fix}")).Select(i => i.Description));
                MixerRoles roles = MixerHygiene.Identify(mixer!);
                receive = LevelControl.Open(mixer!, roles.Capture, MixerDirection.Capture);
                transmit = LevelControl.Open(mixer!, roles.Playback, MixerDirection.Playback);
                if (receive is not null && iface.CaptureLevelDb is { } rx)
                {
                    receive.LevelDb = rx;
                }

                if (transmit is not null && iface.RenderLevelDb is { } tx)
                {
                    transmit.LevelDb = tx;
                }
            }
            else
            {
                warnings.Add($"the card's mixer could not be opened ({why}), so its levels and AGC are as the card has them");
            }

            LinuxSoundCard opened = OpenAudio(iface, radio, card, mixer, note);
            opened.Faulted += faulted;
            return new OpenedInterface(opened, iface, receive, transmit, warnings);
        }
        catch
        {
            receive?.Dispose();
            transmit?.Dispose();
            mixer?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The card direct, or, when something already has it open, through PipeWire if that is
    /// what holds it.
    /// </summary>
    private static LinuxSoundCard OpenAudio(InterfaceSettings iface, RadioInterface radio, AlsaCard card, AlsaMixer? mixer, Action<string> note)
    {
        try
        {
            return LinuxSoundCard.Open(iface, card.CapturePcm!, card.PlaybackPcm!, mixer);
        }
        catch (InvalidOperationException direct) when (Holder(card) is { } holder)
        {
            if (!holder.StartsWith("pipewire", StringComparison.Ordinal) || PipeWire.NodesFor(card.Number) is not { CapturePcm: { } capture, PlaybackPcm: { } playback })
            {
                throw new InvalidOperationException(
                    $"{radio.Name} is in use by {holder}. Close it there, or keep the desktop's sound server off it (pdn-lin's WirePlumber rule does this for the AIOC).",
                    direct);
            }

            LinuxSoundCard through = LinuxSoundCard.Open(iface, capture, playback, mixer);
            note($"{radio.Name} is held by {holder}, so the station is going through PipeWire. For the card direct, keep PipeWire off it: pdn-lin's WirePlumber rule does this for the AIOC.");
            return through;
        }
        catch (InvalidOperationException ex) when (DeviceAccess.Check(radio, PttMethod.None).Where(p => p.Purpose is DevicePurpose.Receive or DevicePurpose.Transmit).ToList() is { Count: > 0 } refused)
        {
            throw new InvalidOperationException(Describe(refused)!, ex);
        }
    }

    private static string? Holder(AlsaCard card) => CardUsers.Holder(card, capture: true) ?? CardUsers.Holder(card, capture: false);

    private static string? Describe(IReadOnlyList<AccessProblem> problems)
    {
        if (problems.Count == 0)
        {
            return null;
        }

        IEnumerable<string> missing = problems.Where(p => p.Missing).Select(p => $"{p.Node} ({Purpose(p.Purpose)}) is not there");
        List<string> refused = problems.Where(p => !p.Missing).Select(p => $"{p.Node} ({Purpose(p.Purpose)})").ToList();
        var parts = missing.ToList();
        if (refused.Count > 0)
        {
            parts.Add($"no permission to open {string.Join(", ", refused)}: {PermissionHelp}");
        }

        return string.Join("; ", parts) + ".";
    }

    private static string Purpose(DevicePurpose purpose) => purpose switch
    {
        DevicePurpose.Receive => "receive audio",
        DevicePurpose.Transmit => "transmit audio",
        DevicePurpose.Mixer => "levels",
        _ => "PTT",
    };

    private static InterfaceOption Describe(RadioInterface r)
    {
        var parts = new List<string> { "in + out" };
        if (r.Hidraw is not null)
        {
            parts.Add("HID PTT");
        }

        if (r.SerialPort is not null)
        {
            parts.Add(Path.GetFileName(r.SerialPort));
        }

        parts.Add(r.Card!.Mixer);
        string kind = r.Kind switch
        {
            RadioInterfaceKind.Aioc => "AIOC",
            RadioInterfaceKind.Cm108 => "CM108",
            _ => "SOUND CARD",
        };
        return new InterfaceOption(r.Name, kind, string.Join("  -  ", parts), r.Kind != RadioInterfaceKind.SoundCard, r.Key);
    }

    /// <summary>Serial ports by their stable names first, then any others.</summary>
    private static List<string> SerialPorts(IReadOnlyList<RadioInterface> found)
    {
        var ports = new List<string>();
        try
        {
            if (Directory.Exists("/dev/serial/by-id"))
            {
                ports.AddRange(Directory.EnumerateFileSystemEntries("/dev/serial/by-id").Order(StringComparer.Ordinal));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        foreach (string port in found.Select(r => r.SerialPort).OfType<string>().Concat(SerialPort.GetPortNames().Order(StringComparer.Ordinal)))
        {
            if (!ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    /// <summary>A mixer level as an <see cref="ILevelControl"/>.</summary>
    private sealed class LevelControl(AlsaLevel level) : ILevelControl
    {
        public double MinDb => level.MinDb;

        public double CeilingDb => level.CeilingDb;

        public double LevelDb
        {
            get => level.LevelDb;
            set => level.LevelDb = value;
        }

        public event Action? Changed
        {
            add => level.Changed += value;
            remove => level.Changed -= value;
        }

        public static LevelControl? Open(IAlsaMixer mixer, string? control, MixerDirection direction) =>
            control is not null && AlsaLevel.Open(mixer, control, direction) is { } level ? new LevelControl(level) : null;

        public void Dispose() => level.Dispose();
    }
}
