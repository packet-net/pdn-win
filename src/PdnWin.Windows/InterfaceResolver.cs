using Packet.SoundModem.Windows;
using PdnWin.Core.Settings;

namespace PdnWin.Hardware;

/// <summary>Turns discovered devices into settings, and settings back into present devices.</summary>
public static class InterfaceResolver
{
    /// <summary>Settings for a discovered interface, with its suggested PTT.</summary>
    public static InterfaceSettings From(RadioInterface found, InterfaceSettings? previous = null)
    {
        ArgumentNullException.ThrowIfNull(found);
        InterfaceSettings basis = previous ?? new InterfaceSettings();
        return basis with
        {
            Name = found.Name,
            ContainerId = found.ContainerId?.ToString(),
            CaptureEndpointId = found.Capture?.Id,
            RenderEndpointId = found.Render?.Id,
            Ptt = found.SuggestedPtt switch
            {
                PttMethod.Cm108Hid => PttKind.Cm108Hid,
                PttMethod.Serial => PttKind.Serial,
                _ => PttKind.None,
            },
            HidPath = found.Hid?.Path,
            SerialPort = found.SerialPort,
            // The AIOC keys on DTR with RTS clear; RTS alone is the older homebrew convention.
            SerialDtr = found.Kind == RadioInterfaceKind.Aioc || basis.SerialDtr,
            SerialRts = found.Kind != RadioInterfaceKind.Aioc && basis.SerialRts,
        };
    }

    /// <summary>
    /// Finds the interface the settings describe among those present, by endpoint, then by
    /// container, then by name, and returns settings with its current IDs and paths (keeping the
    /// operator's PTT choice). Null when it is not plugged in.
    /// </summary>
    public static (InterfaceSettings Settings, RadioInterface Found)? Resolve(InterfaceSettings settings, IReadOnlyList<RadioInterface> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        RadioInterface? match =
            present.FirstOrDefault(r => r.Capture?.Id == settings.CaptureEndpointId && settings.CaptureEndpointId is not null)
            ?? present.FirstOrDefault(r => settings.ContainerId is not null && r.ContainerId?.ToString() == settings.ContainerId)
            ?? present.FirstOrDefault(r => r.IsComplete && string.Equals(r.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (match is null || !match.IsComplete)
        {
            return null;
        }

        return (settings with
        {
            Name = match.Name,
            ContainerId = match.ContainerId?.ToString(),
            CaptureEndpointId = match.Capture!.Id,
            RenderEndpointId = match.Render!.Id,
            HidPath = match.Hid?.Path ?? settings.HidPath,
            SerialPort = settings.Ptt == PttKind.Serial ? settings.SerialPort ?? match.SerialPort : match.SerialPort ?? settings.SerialPort,
        }, match);
    }
}
