using System.Globalization;
using System.Text;
using Packet.Ax25;
using Packet.Core;
using PdnWin.Core.Stations;

namespace PdnWin.Core.Monitoring;

/// <summary>What sort of frame a monitor line is, for colouring it.</summary>
public enum FrameKind
{
    /// <summary>An I-frame: connected-mode data.</summary>
    Information,

    /// <summary>RR or RNR: supervisory acknowledgement.</summary>
    Supervisory,

    /// <summary>REJ, SREJ or FRMR: something went wrong.</summary>
    Reject,

    /// <summary>SABM, SABME, DISC, UA, DM, XID, TEST: link management.</summary>
    Unnumbered,

    /// <summary>A UI frame: unproto, beacons, APRS.</summary>
    UnnumberedInformation,

    /// <summary>Did not parse.</summary>
    Undecodable,
}

/// <summary>One frame, formatted for the monitor pane.</summary>
/// <param name="Time">When.</param>
/// <param name="Direction">Heard or sent.</param>
/// <param name="Kind">For colour.</param>
/// <param name="Header">The BPQ-style header, e.g. <c>M0LTE-1&gt;GB7RDG &lt;I C R0 S0 P&gt;</c>.</param>
/// <param name="Info">The information field as display lines; empty when there is none.</param>
/// <param name="Detail">Receive diagnostics, e.g. <c>afsk1200 17.5 dB +3 Hz</c>, or for one of ours
/// what held it; empty when none.</param>
/// <param name="Source">The frame's source, when it parsed.</param>
/// <param name="Destination">The frame's destination, when it parsed.</param>
/// <param name="Badge">A level verdict worth a badge ("TOO LOUD"), "HELD 5.2s" for one of ours that
/// waited for the channel, or null.</param>
public sealed record MonitorEntry(
    DateTimeOffset Time,
    FrameDirection Direction,
    FrameKind Kind,
    string Header,
    IReadOnlyList<string> Info,
    string Detail,
    Callsign? Source,
    Callsign? Destination,
    string? Badge);

/// <summary>
/// Formats frames for the monitor the way BPQ and QtTermTCP do. Adapted from packet-term-tui's
/// FrameFormatter (packet-net/packet-term-tui), which follows the web packet terminal's format.
/// </summary>
public static class MonitorFormatter
{
    /// <summary>Formats one heard or sent frame.</summary>
    public static MonitorEntry Format(HeardFrame heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        string detail = FormatQuality(heard.Quality);
        string? badge = heard.Quality?.LevelVerdict;
        if (heard.Hold is { } hold)
        {
            // The station page's HELD tag: a frame of ours that waited, and what it waited for.
            string seconds = hold.For.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            badge = $"HELD {seconds}s";
            detail = hold.Because is { } because ? $"held: {because}" : $"held {seconds}s, no one cause";
        }
        if (!Ax25Frame.TryParse(heard.Bytes, out Ax25Frame? frame))
        {
            return new MonitorEntry(heard.Time, heard.Direction, FrameKind.Undecodable,
                $"<undecodable {heard.Bytes.Length} bytes>", [], detail, null, null, badge);
        }

        return Format(heard.Time, heard.Direction, frame, detail, badge);
    }

    /// <summary>Formats a parsed frame.</summary>
    public static MonitorEntry Format(DateTimeOffset time, FrameDirection direction, Ax25Frame frame, string detail = "", string? badge = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var header = new StringBuilder(64);
        header.Append(Callsign(frame.Source.Callsign)).Append('>').Append(Callsign(frame.Destination.Callsign));
        foreach (Ax25Address digi in frame.Digipeaters)
        {
            header.Append(',').Append(Callsign(digi.Callsign));
            if (digi.CrhBit)
            {
                header.Append('*');
            }
        }

        string kind = Classify(frame);
        header.Append(" <").Append(Body(frame, kind)).Append('>');

        IReadOnlyList<string> info = (kind is "I" or "UI") && frame.Info.Length > 0
            ? ReceivedText.ToLines(frame.Info.Span)
            : [];

        return new MonitorEntry(time, direction, KindOf(kind), header.ToString(), info, detail,
            frame.Source.Callsign, frame.Destination.Callsign, badge);
    }

    /// <summary>A frame's short kind tag: I, RR, RNR, REJ, SREJ, SABM, UA, UI and so on.</summary>
    public static string Classify(Ax25Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        byte control = frame.Control;
        if ((control & 0x01) == 0)
        {
            return "I";
        }

        if ((control & 0x03) == 0x01)
        {
            return (control & 0x0C) switch
            {
                0x00 => "RR",
                0x04 => "RNR",
                0x08 => "REJ",
                _ => "SREJ",
            };
        }

        return (byte)(control & 0xEF) switch
        {
            0x2F => "SABM",
            0x6F => "SABME",
            0x43 => "DISC",
            0x63 => "UA",
            0x0F => "DM",
            0x87 => "FRMR",
            0xAF => "XID",
            0xE3 => "TEST",
            0x03 => "UI",
            _ => "?",
        };
    }

    /// <summary>A callsign as BPQ shows it: SSID 0 hidden.</summary>
    public static string Callsign(Callsign callsign) => callsign.Ssid == 0 ? callsign.Base : callsign.ToString();

    private static FrameKind KindOf(string kind) => kind switch
    {
        "I" => FrameKind.Information,
        "RR" or "RNR" => FrameKind.Supervisory,
        "REJ" or "SREJ" or "FRMR" => FrameKind.Reject,
        "UI" => FrameKind.UnnumberedInformation,
        _ => FrameKind.Unnumbered,
    };

    private static string Body(Ax25Frame frame, string kind)
    {
        string cr = frame.IsCommand ? "C" : frame.IsResponse ? "R" : "?";
        string pf = frame.PollFinal ? (frame.IsCommand ? " P" : " F") : string.Empty;
        byte control = frame.Control;
        int nr = (control >> 5) & 0x07;
        int ns = (control >> 1) & 0x07;
        return kind switch
        {
            "I" => string.Create(CultureInfo.InvariantCulture, $"I {cr} R{nr} S{ns}{pf}"),
            "RR" or "RNR" or "REJ" or "SREJ" => string.Create(CultureInfo.InvariantCulture, $"{kind} {cr} R{nr}{pf}"),
            // BPQ shows the PID only when it is not plain text (F0), which is nearly always.
            "UI" when frame.Pid != Ax25Frame.PidNoLayer3 => string.Create(CultureInfo.InvariantCulture, $"UI {cr}{pf} pid={frame.Pid:X2}"),
            _ => $"{kind} {cr}{pf}",
        };
    }

    private static string FormatQuality(HeardFrameQuality? quality)
    {
        if (quality is null)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (quality.Mode is { } mode)
        {
            parts.Add(mode);
        }

        if (quality.SnrDb is { } snr)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{snr:0.0} dB"));
        }

        if (quality.FrequencyOffsetHz is { } offset)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{offset:+0;-0;0} Hz"));
        }

        if (quality.PeakDbFs is { } peak)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{peak:0.0} dBFS"));
        }

        if (quality.CorrectedBytes is > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"fec {quality.CorrectedBytes}"));
        }

        if (quality.MonitorOnly)
        {
            parts.Add("monitor only");
        }

        return string.Join(", ", parts);
    }
}
