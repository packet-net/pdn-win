using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Packet.Core;
using Packet.SoundModem.Windows;
using PdnWin.Core.Settings;
using PdnWin.Core.Stations;
using PdnWin.Core.Stations.SoundModem;
using PdnWin.Hardware;

namespace PdnWin.ViewModels;

/// <summary>An interface in the picker.</summary>
public sealed class InterfaceChoice(RadioInterface found)
{
    /// <summary>What was discovered.</summary>
    public RadioInterface Found { get; } = found;

    /// <summary>Its name.</summary>
    public string Name => Found.Name;

    /// <summary>"AIOC", "CM108" or "Sound card".</summary>
    public string Kind => Found.Kind switch
    {
        RadioInterfaceKind.Aioc => "AIOC",
        RadioInterfaceKind.Cm108 => "CM108",
        _ => "SOUND CARD",
    };

    /// <summary>Whether it looks like a radio interface rather than the PC's own audio.</summary>
    public bool IsRadio => Found.Kind != RadioInterfaceKind.SoundCard;

    /// <summary>A line of detail.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Found.IsComplete ? "in + out" : Found.Capture is null ? "output only" : "input only");
            if (Found.Hid is not null)
            {
                parts.Add("HID PTT");
            }

            if (Found.SerialPort is not null)
            {
                parts.Add(Found.SerialPort);
            }

            return string.Join("  -  ", parts);
        }
    }
}

/// <summary>The settings dialog.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _myCall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private InterfaceChoice? _selectedInterface;

    [ObservableProperty]
    private PttKind _ptt;

    [ObservableProperty]
    private int _gpio;

    [ObservableProperty]
    private string? _serialPort;

    [ObservableProperty]
    private bool _serialDtr;

    [ObservableProperty]
    private bool _serialRts;

    [ObservableProperty]
    private string _mode;

    [ObservableProperty]
    private int _txDelayMs;

    [ObservableProperty]
    private int _persistence;

    [ObservableProperty]
    private int _slotTimeMs;

    [ObservableProperty]
    private int _txTailMs;

    [ObservableProperty]
    private bool _acceptIncoming;

    [ObservableProperty]
    private string _welcomeText;

    [ObservableProperty]
    private int _paclen;

    [ObservableProperty]
    private bool _beaconEnabled;

    [ObservableProperty]
    private int _beaconMinutes;

    [ObservableProperty]
    private string _beaconPath;

    [ObservableProperty]
    private string _beaconText;

    [ObservableProperty]
    private bool _scanning;

    [ObservableProperty]
    private string _scanResult = string.Empty;

    /// <summary>Loads the dialog from <paramref name="settings"/>.</summary>
    public SettingsViewModel(AppSettings settings)
    {
        _original = settings;
        _myCall = settings.MyCall;
        _ptt = settings.Interface.Ptt;
        _gpio = settings.Interface.Gpio;
        _serialPort = settings.Interface.SerialPort;
        _serialDtr = settings.Interface.SerialDtr;
        _serialRts = settings.Interface.SerialRts;
        _mode = settings.Modem.Mode;
        ChannelAccess a = settings.Modem.ChannelAccess;
        _txDelayMs = a.TxDelayMs;
        _persistence = a.Persistence;
        _slotTimeMs = a.SlotTimeMs;
        _txTailMs = a.TxTailMs;
        _acceptIncoming = settings.Sessions.AcceptIncoming;
        _welcomeText = settings.Sessions.WelcomeText;
        _paclen = settings.Sessions.Paclen;
        _beaconEnabled = settings.Beacon.Enabled;
        _beaconMinutes = settings.Beacon.IntervalMinutes;
        _beaconPath = settings.Beacon.Path;
        _beaconText = settings.Beacon.Text;
    }

    /// <summary>Discovered interfaces, radio interfaces first.</summary>
    public ObservableCollection<InterfaceChoice> Interfaces { get; } = [];

    /// <summary>Every COM port, for serial PTT.</summary>
    public ObservableCollection<string> SerialPorts { get; } = [];

    /// <summary>The FM modes.</summary>
    public IReadOnlyList<string> Modes { get; } = FmModes.Names;

    /// <summary>The PTT choices.</summary>
    public IReadOnlyList<PttKind> PttKinds { get; } = [PttKind.Cm108Hid, PttKind.Serial, PttKind.None];

    /// <summary>Whether enough is filled in to save.</summary>
    public bool CanSave => Callsign.TryParse(MyCall.Trim().ToUpperInvariant(), out _) && SelectedInterface is { Found.IsComplete: true };

    /// <summary>Finds the interfaces present and selects the configured one, or the first radio one.</summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        Scanning = true;
        try
        {
            IReadOnlyList<RadioInterface> found = await Task.Run(RadioInterfaces.Discover);
            int radios = found.Count(r => r.IsComplete && r.Kind != RadioInterfaceKind.SoundCard);
            ScanResult = found.Count == 0
                ? "No audio devices found."
                : $"{found.Count} audio device{(found.Count == 1 ? "" : "s")}, {radios} radio interface{(radios == 1 ? "" : "s")}."
                  + (radios == 0 ? " Plug in an AIOC or CM108 interface and press Rescan." : string.Empty);
            Interfaces.Clear();
            foreach (RadioInterface r in found.Where(r => r.IsComplete))
            {
                Interfaces.Add(new InterfaceChoice(r));
            }

            SerialPorts.Clear();
            foreach (string port in System.IO.Ports.SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                SerialPorts.Add(port);
            }

            InterfaceSettings current = _original.Interface;
            SelectedInterface =
                (InterfaceResolver.Resolve(current, found) is { } resolved
                    ? Interfaces.FirstOrDefault(i => ReferenceEquals(i.Found, resolved.Found))
                    : null)
                ?? Interfaces.FirstOrDefault(i => i.IsRadio)
                ?? Interfaces.FirstOrDefault();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ScanResult = "Could not list devices: " + ex.Message;
        }
        finally
        {
            Scanning = false;
        }
    }

    partial void OnSelectedInterfaceChanged(InterfaceChoice? value)
    {
        if (value is null || _original.Interface.ContainerId == value.Found.ContainerId?.ToString())
        {
            return;
        }

        // A different device: take its suggested PTT.
        InterfaceSettings suggested = InterfaceResolver.From(value.Found, _original.Interface);
        Ptt = suggested.Ptt;
        SerialPort = suggested.SerialPort ?? SerialPort;
        SerialDtr = suggested.SerialDtr;
        SerialRts = suggested.SerialRts;
    }

    /// <summary>The settings as edited.</summary>
    public AppSettings Build()
    {
        InterfaceSettings iface = SelectedInterface is { } choice
            ? InterfaceResolver.From(choice.Found, _original.Interface)
            : _original.Interface;
        iface = iface with
        {
            Ptt = Ptt,
            Gpio = Math.Clamp(Gpio, 1, 8),
            SerialPort = SerialPort,
            SerialDtr = SerialDtr,
            SerialRts = SerialRts,
            HidPath = SelectedInterface?.Found.Hid?.Path ?? iface.HidPath,
        };

        return _original with
        {
            MyCall = MyCall.Trim().ToUpperInvariant(),
            Interface = iface,
            Modem = _original.Modem with
            {
                Mode = Mode,
                ChannelAccess = new ChannelAccess(
                    Math.Clamp(TxDelayMs, 10, 2550), Math.Clamp(Persistence, 0, 255), Math.Clamp(SlotTimeMs, 10, 2550), Math.Clamp(TxTailMs, 0, 2550)),
            },
            Sessions = _original.Sessions with
            {
                AcceptIncoming = AcceptIncoming,
                WelcomeText = WelcomeText,
                Paclen = Math.Clamp(Paclen, 16, 256),
            },
            Beacon = new BeaconSettingsModel
            {
                Enabled = BeaconEnabled,
                IntervalMinutes = Math.Max(5, BeaconMinutes),
                Path = BeaconPath.Trim().ToUpperInvariant(),
                Text = BeaconText,
            },
        };
    }
}
