using System.IO.Ports;
using Packet.SoundModem.Windows;
using PdnWin.Core.Settings;
using PdnWin.Hosting;

namespace PdnWin.Hardware;

/// <summary>
/// Radio interfaces on Windows: discovery by container ID, endpoint hygiene (nothing above 0 dB,
/// enhancements and AGC off, monitor paths closed), endpoint levels, and WASAPI with HID or serial
/// PTT, all through pdn-soundmodem-windows.
/// </summary>
public sealed class WindowsStationHardware : IStationHardware
{
    /// <inheritdoc />
    public async Task<DiscoveryResult> DiscoverAsync(InterfaceSettings current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        IReadOnlyList<RadioInterface> found = await Task.Run(RadioInterfaces.Discover, cancellationToken).ConfigureAwait(false);
        int radios = found.Count(r => r.IsComplete && r.Kind != RadioInterfaceKind.SoundCard);
        string summary = found.Count == 0
            ? "No audio devices found."
            : $"{found.Count} audio device{(found.Count == 1 ? "" : "s")}, {radios} radio interface{(radios == 1 ? "" : "s")}."
              + (radios == 0 ? " Plug in an AIOC or CM108 interface and press Rescan." : string.Empty);

        List<InterfaceOption> options = found.Where(r => r.IsComplete).Select(Describe).ToList();
        InterfaceOption? matched = InterfaceResolver.Resolve(current, found) is { } resolved
            ? options.FirstOrDefault(o => o.Key == Key(resolved.Found))
            : null;
        IReadOnlyList<string> ports = SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new DiscoveryResult(options, matched, summary, ports);
    }

    /// <inheritdoc />
    public InterfaceSettings Suggest(InterfaceOption option, InterfaceSettings previous)
    {
        ArgumentNullException.ThrowIfNull(option);
        RadioInterface? found = RadioInterfaces.Discover().FirstOrDefault(r => Key(r) == option.Key);
        return found is null ? previous : InterfaceResolver.From(found, previous);
    }

    /// <inheritdoc />
    public async Task<OpenedInterface?> OpenAsync(
        InterfaceSettings settings, Action<string> note, Action<Exception> faulted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(faulted);

        IReadOnlyList<RadioInterface> present = await Task.Run(RadioInterfaces.Discover, cancellationToken).ConfigureAwait(false);
        if (InterfaceResolver.Resolve(settings, present) is not { } resolved)
        {
            return null;
        }

        InterfaceSettings iface = resolved.Settings;
        List<string> warnings = await Task.Run(() => ApplyHygiene(iface, note), cancellationToken).ConfigureAwait(false);

        EndpointLevelControl? receive = null;
        EndpointLevelControl? transmit = null;
        try
        {
            receive = new EndpointLevelControl(EndpointLevel.Open(iface.CaptureEndpointId!));
            transmit = new EndpointLevelControl(EndpointLevel.Open(iface.RenderEndpointId!));
            if (iface.CaptureLevelDb is { } rx)
            {
                receive.LevelDb = rx;
            }

            if (iface.RenderLevelDb is { } tx)
            {
                transmit.LevelDb = tx;
            }

            WindowsSoundCard card = await Task.Run(() => WindowsSoundCard.Open(iface), cancellationToken).ConfigureAwait(false);
            card.Faulted += faulted;
            return new OpenedInterface(card, iface, receive, transmit, warnings);
        }
        catch
        {
            receive?.Dispose();
            transmit?.Dispose();
            throw;
        }
    }

    private static List<string> ApplyHygiene(InterfaceSettings settings, Action<string> note)
    {
        var remaining = new List<string>();
        foreach ((string? id, AudioFlow flow) in new[] { (settings.CaptureEndpointId, AudioFlow.Capture), (settings.RenderEndpointId, AudioFlow.Render) })
        {
            if (id is null)
            {
                continue;
            }

            IReadOnlyList<EndpointIssue> found = EndpointHygiene.Check(id, flow);
            if (found.Count == 0)
            {
                continue;
            }

            foreach (EndpointIssue issue in found.Where(i => i.CanFix))
            {
                note($"fixed {(flow == AudioFlow.Capture ? "receive" : "transmit")} audio: {issue.Description}");
            }

            remaining.AddRange(EndpointHygiene.Fix(id, flow).Select(i => i.Description));
        }

        return remaining;
    }

    private static string Key(RadioInterface r) => r.ContainerId?.ToString() ?? r.Capture?.Id ?? r.Name;

    private static InterfaceOption Describe(RadioInterface r)
    {
        var parts = new List<string> { r.IsComplete ? "in + out" : r.Capture is null ? "output only" : "input only" };
        if (r.Hid is not null)
        {
            parts.Add("HID PTT");
        }

        if (r.SerialPort is not null)
        {
            parts.Add(r.SerialPort);
        }

        string kind = r.Kind switch
        {
            RadioInterfaceKind.Aioc => "AIOC",
            RadioInterfaceKind.Cm108 => "CM108",
            _ => "SOUND CARD",
        };
        return new InterfaceOption(r.Name, kind, string.Join("  -  ", parts), r.Kind != RadioInterfaceKind.SoundCard, Key(r));
    }

    /// <summary>A Windows endpoint level as an <see cref="ILevelControl"/>.</summary>
    private sealed class EndpointLevelControl(EndpointLevel level) : ILevelControl
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

        public void Dispose() => level.Dispose();
    }
}
