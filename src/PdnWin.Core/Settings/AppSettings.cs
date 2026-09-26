using System.Text.Json;
using System.Text.Json.Serialization;
using PdnWin.Core.Stations;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Core.Settings;

/// <summary>How the transmitter is keyed.</summary>
public enum PttKind
{
    /// <summary>No PTT line (VOX).</summary>
    None,

    /// <summary>CM108-compatible HID GPIO (CM108 dongles, the AIOC).</summary>
    Cm108Hid,

    /// <summary>A serial port's control lines.</summary>
    Serial,
}

/// <summary>Everything the app remembers. Stored as JSON in %APPDATA%\pdn-win.</summary>
public sealed record AppSettings
{
    /// <summary>Our callsign with SSID; empty until the operator sets it.</summary>
    public string MyCall { get; init; } = string.Empty;

    /// <summary>The radio interface and how it is keyed.</summary>
    public InterfaceSettings Interface { get; init; } = new();

    /// <summary>The modem.</summary>
    public ModemSettings Modem { get; init; } = new();

    /// <summary>The session layer.</summary>
    public SessionSettings Sessions { get; init; } = new();

    /// <summary>The beacon.</summary>
    public BeaconSettingsModel Beacon { get; init; } = new();

    /// <summary>The window, the dock layout and other display choices.</summary>
    public UiSettings Ui { get; init; } = new();

    /// <summary>Whether enough is set to put a station on the air.</summary>
    [JsonIgnore]
    public bool IsComplete =>
        MyCall.Length > 0 && Interface.CaptureEndpointId is not null && Interface.RenderEndpointId is not null;
}

/// <summary>Which devices make up the radio interface.</summary>
public sealed record InterfaceSettings
{
    /// <summary>The interface's name, for display ("AIOC Audio").</summary>
    public string? Name { get; init; }

    /// <summary>The Windows container ID of the physical device, for finding it again if its
    /// endpoint or HID paths change (a serial-less CM108 moved to another USB port).</summary>
    public string? ContainerId { get; init; }

    /// <summary>The WASAPI capture endpoint ID.</summary>
    public string? CaptureEndpointId { get; init; }

    /// <summary>The WASAPI render endpoint ID.</summary>
    public string? RenderEndpointId { get; init; }

    /// <summary>How PTT is done.</summary>
    public PttKind Ptt { get; init; } = PttKind.Cm108Hid;

    /// <summary>The HID device path, for <see cref="PttKind.Cm108Hid"/>.</summary>
    public string? HidPath { get; init; }

    /// <summary>The CM108 GPIO pin.</summary>
    public int Gpio { get; init; } = 3;

    /// <summary>The serial port, for <see cref="PttKind.Serial"/>.</summary>
    public string? SerialPort { get; init; }

    /// <summary>Key with RTS.</summary>
    public bool SerialRts { get; init; }

    /// <summary>Key with DTR (the AIOC's serial PTT is DTR set, RTS clear).</summary>
    public bool SerialDtr { get; init; } = true;

    /// <summary>The Windows capture level to hold, in dB (never above 0); null leaves it alone.</summary>
    public double? CaptureLevelDb { get; init; }

    /// <summary>The Windows render level to hold, in dB (never above 0); null leaves it alone.</summary>
    public double? RenderLevelDb { get; init; }
}

/// <summary>The modem and channel access.</summary>
public sealed record ModemSettings
{
    /// <summary>The mode.</summary>
    public string Mode { get; init; } = FmModes.Default;

    /// <summary>The audio centre for modes that take one; null for the mode's default.</summary>
    public double? CentreFrequencyHz { get; init; }

    /// <summary>TXDELAY and friends. Handhelds want a longer TXDELAY than the 300 ms default.</summary>
    public ChannelAccess ChannelAccess { get; init; } = new(TxDelayMs: 400);
}

/// <summary>The session layer.</summary>
public sealed record SessionSettings
{
    /// <summary>Whether other stations may connect to us.</summary>
    public bool AcceptIncoming { get; init; } = true;

    /// <summary>Sent to a station that connects to us.</summary>
    public string WelcomeText { get; init; } = "Welcome to {MYCALL}, running pdn-win.";

    /// <summary>Information bytes per frame.</summary>
    public int Paclen { get; init; } = 128;

    /// <summary>Recently connected callsigns, most recent first.</summary>
    public IReadOnlyList<string> RecentCalls { get; init; } = [];
}

/// <summary>The beacon, as stored.</summary>
public sealed record BeaconSettingsModel
{
    /// <summary>Whether it is sent.</summary>
    public bool Enabled { get; init; }

    /// <summary>Minutes between beacons.</summary>
    public int IntervalMinutes { get; init; } = 30;

    /// <summary>Destination and digipeaters, comma separated: "BEACON" or "ID,GB7RDG".</summary>
    public string Path { get; init; } = "BEACON";

    /// <summary>The text; {MYCALL} is replaced.</summary>
    public string Text { get; init; } = "{MYCALL} packet station, pdn-win";
}

/// <summary>Display state.</summary>
public sealed record UiSettings
{
    /// <summary>The dock layout, as the dock host serialises it.</summary>
    public JsonElement? Layout { get; init; }

    /// <summary>The window's normal bounds: left, top, width, height.</summary>
    public double[]? WindowBounds { get; init; }

    /// <summary>Whether the window was maximised.</summary>
    public bool Maximized { get; init; }

    /// <summary>Show RR/RNR frames in the monitor.</summary>
    public bool MonitorSupervisory { get; init; } = true;

    /// <summary>Waterfall floor, dBFS.</summary>
    public double WaterfallFloorDb { get; init; } = -90;

    /// <summary>Waterfall top, dBFS.</summary>
    public double WaterfallTopDb { get; init; } = -30;

    /// <summary>Spectrum and waterfall span, Hz from 0.</summary>
    public double SpanHz { get; init; } = 3000;
}

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The usual place: %APPDATA%\pdn-win\settings.json.</summary>
    public static SettingsStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pdn-win", "settings.json"));

    /// <summary>Where the file is.</summary>
    public string FilePath { get; } = path;

    /// <summary>Reads the settings, or returns defaults when there is no file or it is unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            // Keep the broken file for the operator to look at rather than overwrite it silently.
            File.Copy(FilePath, FilePath + ".broken", overwrite: true);
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings atomically.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
