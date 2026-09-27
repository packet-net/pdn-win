using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Packet.Core;
using PdnWin.Core.Settings;
using PdnWin.Core.Stations;
using PdnWin.Core.Stations.SoundModem;
using PdnWin.Hosting;

namespace PdnWin.ViewModels;

/// <summary>The settings dialog.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _original;
    private readonly IStationHardware? _hardware;
    private readonly IUiThread? _ui;

    // The selected interface as the hardware resolves it: worked out when the choice changes, not
    // each time Build runs (which is on every edit, for SaveEffect).
    private InterfaceSettings? _resolved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _myCall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private InterfaceOption? _selectedInterface;

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

    /// <summary>Loads the dialog from <paramref name="settings"/>, finding interfaces through
    /// <paramref name="hardware"/> (null where the platform has none yet). With
    /// <paramref name="ui"/>, the list rescans itself when an interface is plugged in or out.</summary>
    public SettingsViewModel(AppSettings settings, IStationHardware? hardware, IUiThread? ui = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _original = settings;
        _hardware = hardware;
        _ui = ui;
        if (hardware is not null && ui is not null)
        {
            hardware.DevicesChanged += OnDevicesChanged;
        }
        _myCall = settings.MyCall;
        _ptt = settings.Interface.Ptt;
        _gpio = settings.Interface.Gpio;
        _serialPort = settings.Interface.SerialPort;
        _serialDtr = settings.Interface.SerialDtr;
        _serialRts = settings.Interface.SerialRts;
        _mode = settings.Modem.Mode;
        ChannelAccess a = settings.Modem.ChannelAccess;
        _txDelayMs = a.TxDelayMs;
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
    public ObservableCollection<InterfaceOption> Interfaces { get; } = [];

    /// <summary>Every COM port, for serial PTT.</summary>
    public ObservableCollection<string> SerialPorts { get; } = [];

    /// <summary>The FM modes.</summary>
    public IReadOnlyList<string> Modes { get; } = FmModes.Names;

    /// <summary>The PTT choices.</summary>
    public IReadOnlyList<PttKind> PttKinds { get; } = [PttKind.Cm108Hid, PttKind.Serial, PttKind.None];

    /// <summary>Whether enough is filled in to save.</summary>
    public bool CanSave => Callsign.TryParse(MyCall.Trim().ToUpperInvariant(), out _) && SelectedInterface is not null;

    /// <summary>Whether the station is on the air, and so what saving does to it.</summary>
    public bool StationRunning { get; init; }

    /// <summary>What saving will do to the station, as the settings stand: the same rule the main
    /// window applies (<see cref="AppSettings.NeedsRestartFor"/>).</summary>
    public string SaveEffect =>
        !StationRunning ? "Saving starts the station."
        : _original.NeedsRestartFor(Build()) ? "Saving restarts the station, which drops any connected sessions."
        : "Saving applies these to the running station.";

    /// <summary>Finds the interfaces present and selects the configured one, or the first radio one.</summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        if (_hardware is null)
        {
            ScanResult = "Radio interfaces are not supported on this platform yet.";
            return;
        }

        Scanning = true;
        string? keep = SelectedInterface?.Key;
        try
        {
            DiscoveryResult found = await _hardware.DiscoverAsync(_original.Interface);
            ScanResult = found.Summary;
            Interfaces.Clear();
            foreach (InterfaceOption option in found.Options)
            {
                Interfaces.Add(option);
            }

            SerialPorts.Clear();
            foreach (string port in found.SerialPorts)
            {
                SerialPorts.Add(port);
            }

            // What the operator had chosen, if it is still there; else the stored one; else the
            // first radio interface.
            InterfaceOption? choice = Interfaces.FirstOrDefault(i => i.Key == keep)
                ?? found.Current
                ?? Interfaces.FirstOrDefault(i => i.IsRadio)
                ?? Interfaces.FirstOrDefault();
            if (Equals(choice, SelectedInterface))
            {
                // Unchanged, so OnSelectedInterfaceChanged will not run; the device's paths may have.
                _resolved = Resolve(choice);
            }

            SelectedInterface = choice;
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_hardware is not null && _ui is not null)
        {
            _hardware.DevicesChanged -= OnDevicesChanged;
        }
    }

    private void OnDevicesChanged() => _ui?.Post(() =>
    {
        if (!Scanning)
        {
            _ = ScanAsync();
        }
    });

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(SaveEffect))
        {
            OnPropertyChanged(nameof(SaveEffect));
        }
    }

    private InterfaceSettings? Resolve(InterfaceOption? option) =>
        option is not null && _hardware is not null ? _hardware.Suggest(option, _original.Interface) : null;

    partial void OnSelectedInterfaceChanged(InterfaceOption? value)
    {
        _resolved = Resolve(value);
        if (value is null || _resolved is not { } suggested || _original.Interface.ContainerId == value.Key)
        {
            return;
        }

        // A different device: take its suggested PTT.
        Ptt = suggested.Ptt;
        SerialPort = suggested.SerialPort ?? SerialPort;
        SerialDtr = suggested.SerialDtr;
        SerialRts = suggested.SerialRts;
    }

    /// <summary>The settings as edited.</summary>
    public AppSettings Build()
    {
        InterfaceSettings iface = _resolved ?? _original.Interface;
        iface = iface with
        {
            Ptt = Ptt,
            Gpio = Math.Clamp(Gpio, 1, 8),
            SerialPort = SerialPort,
            SerialDtr = SerialDtr,
            SerialRts = SerialRts,
        };

        return _original with
        {
            MyCall = MyCall.Trim().ToUpperInvariant(),
            Interface = iface,
            Modem = _original.Modem with
            {
                Mode = Mode,
                ChannelAccess = new ChannelAccess(TxDelayMs: Math.Clamp(TxDelayMs, 10, 2550), TxTailMs: Math.Clamp(TxTailMs, 0, 2550)),
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
