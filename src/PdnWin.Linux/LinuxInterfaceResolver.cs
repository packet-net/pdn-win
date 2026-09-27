using Packet.SoundModem.Linux;
using PdnWin.Core.Settings;

namespace PdnWin.Hardware;

/// <summary>
/// Turns discovered Linux interfaces into settings, and settings back into present interfaces.
/// The settings' endpoint fields hold ALSA device strings here, and the container ID holds the
/// interface's key (USB IDs and serial number, or USB port), which is what survives card numbers
/// and ids moving between boots.
/// </summary>
public static class LinuxInterfaceResolver
{
    /// <summary>Settings for a discovered interface, with its suggested PTT.</summary>
    public static InterfaceSettings From(RadioInterface found, InterfaceSettings? previous = null)
    {
        ArgumentNullException.ThrowIfNull(found);
        InterfaceSettings basis = previous ?? new InterfaceSettings();
        return basis with
        {
            Name = found.Name,
            ContainerId = found.Key,
            CaptureEndpointId = found.Card?.CapturePcm,
            RenderEndpointId = found.Card?.PlaybackPcm,
            Ptt = found.SuggestedPtt switch
            {
                PttMethod.Cm108Hid => PttKind.Cm108Hid,
                PttMethod.Serial => PttKind.Serial,
                _ => PttKind.None,
            },
            HidPath = found.Hidraw,
            SerialPort = found.SerialPort,
            // The AIOC keys on DTR with RTS clear; RTS alone is the older homebrew convention.
            SerialDtr = found.Kind == RadioInterfaceKind.Aioc || basis.SerialDtr,
            SerialRts = found.Kind != RadioInterfaceKind.Aioc && basis.SerialRts,
        };
    }

    /// <summary>
    /// Finds the interface the settings describe among those present, by key, then by capture
    /// device, then by name, and returns settings with its current device strings and paths
    /// (keeping the operator's PTT choice). Null when it is not plugged in.
    /// </summary>
    public static (InterfaceSettings Settings, RadioInterface Found)? Resolve(InterfaceSettings settings, IReadOnlyList<RadioInterface> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        RadioInterface? match =
            present.FirstOrDefault(r => settings.ContainerId is not null && r.Key == settings.ContainerId)
            ?? present.FirstOrDefault(r => settings.CaptureEndpointId is not null && r.Card?.CapturePcm == settings.CaptureEndpointId)
            ?? present.FirstOrDefault(r => r.IsComplete && string.Equals(r.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (match is not { IsComplete: true, Card: { } card })
        {
            return null;
        }

        return (settings with
        {
            Name = match.Name,
            ContainerId = match.Key,
            CaptureEndpointId = card.CapturePcm,
            RenderEndpointId = card.PlaybackPcm,
            HidPath = match.Hidraw ?? settings.HidPath,
            SerialPort = settings.Ptt == PttKind.Serial ? settings.SerialPort ?? match.SerialPort : match.SerialPort ?? settings.SerialPort,
        }, match);
    }
}
