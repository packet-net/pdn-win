using System.IO;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Packet.Core;
using Packet.SoundModem.Modems;
using Packet.SoundModem.Windows;
using PdnWin.Controls;
using PdnWin.Core.Beacons;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Sessions;
using PdnWin.Core.Settings;
using PdnWin.Core.Stations;
using PdnWin.Core.Stations.Simulation;
using PdnWin.Core.Stations.SoundModem;
using PdnWin.Hardware;

namespace PdnWin.ViewModels;

/// <summary>Where the station is.</summary>
public enum StationStatus
{
    /// <summary>Not configured, or stopped.</summary>
    Offline,

    /// <summary>Opening the interface.</summary>
    Starting,

    /// <summary>Receiving and ready to transmit.</summary>
    Live,

    /// <summary>Something is wrong; <see cref="MainViewModel.StatusText"/> says what.</summary>
    Fault,
}

/// <summary>A mode in the picker.</summary>
/// <param name="Mode">The soundmodem mode name.</param>
/// <param name="Description">What it is.</param>
public sealed record ModeChoice(string Mode, string Description)
{
    /// <inheritdoc />
    public override string ToString() => Mode;
}

/// <summary>
/// The window's state and the station's lifecycle: opens the interface, runs the soundmodem and
/// the session layer, and fans their events out to the panes.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _ui;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _fast;
    private readonly DispatcherTimer _slow;
    private readonly DispatcherTimer _retry;
    private readonly BeaconScheduler _beacon;
    private WindowsSoundCard? _card;
    private SimulatedChannel? _simulation;
    private SoundModemStation? _station;
    private SessionManager? _sessions;
    private EndpointLevel? _rxLevel;
    private EndpointLevel? _txLevel;
    private DateTime _clipUntil;
    private bool _applyingLevels;
    private bool _starting;
    private volatile bool _transmittingNow;
    private int _generation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private StationStatus _status = StationStatus.Offline;

    [ObservableProperty]
    private string _statusText = "Not started";

    [ObservableProperty]
    private string _interfaceName = "No interface";

    [ObservableProperty]
    private string _myCall = string.Empty;

    [ObservableProperty]
    private ModeChoice? _selectedMode;

    [ObservableProperty]
    private Passband? _passband;

    [ObservableProperty]
    private double _modeCentreHz = 1700;

    [ObservableProperty]
    private bool _levelsAvailable;

    [ObservableProperty]
    private double _rxLevelDb;

    [ObservableProperty]
    private double _rxMinDb = -60;

    [ObservableProperty]
    private double _rxMaxDb;

    [ObservableProperty]
    private double _txLevelDb;

    [ObservableProperty]
    private double _txMinDb = -60;

    [ObservableProperty]
    private double _txMaxDb;

    [ObservableProperty]
    private double _peakDb = -120;

    [ObservableProperty]
    private double _rmsDb = -120;

    [ObservableProperty]
    private string _levelText = "no reading";

    [ObservableProperty]
    private bool _clipping;

    [ObservableProperty]
    private bool _transmitting;

    [ObservableProperty]
    private bool _channelBusy;

    [ObservableProperty]
    private TxTestPreset _selectedPreset = TxTestPreset.All[1];

    [ObservableProperty]
    private double _txTestSeconds = 5;

    [ObservableProperty]
    private bool _txTestRunning;

    [ObservableProperty]
    private int _framesHeard;

    [ObservableProperty]
    private int _framesSent;

    [ObservableProperty]
    private string _lastNotice = string.Empty;

    [ObservableProperty]
    private string? _hygieneWarning;

    /// <summary>Creates the view model; <see cref="StartAsync"/> puts the station on.</summary>
    public MainViewModel(SettingsStore store, Dispatcher ui)
    {
        _store = store;
        _ui = ui;
        Settings = store.Load();
        MyCall = Settings.MyCall;
        Modes = FmModes.All.Select(m => new ModeChoice(m.Mode, m.Description)).ToList();
        _selectedMode = Modes.FirstOrDefault(m => m.Mode == Settings.Modem.Mode) ?? Modes[0];
        UpdatePassband(_selectedMode.Mode);

        Sessions = new SessionsViewModel(() => _sessions, RememberCall);
        foreach (string call in Settings.Sessions.RecentCalls)
        {
            Sessions.RecentCalls.Add(call);
        }

        _beacon = new BeaconScheduler(SendBeaconAsync);
        _beacon.SendFailed += ex => OnUi(() => Sessions.Console.Note("beacon not sent: " + ex.Message));

        _fast = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => FastTick(), ui);
        _slow = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Heard.Tick(), ui);
        _retry = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RetryAsync(), ui);
        _fast.Start();
        _slow.Start();
    }

    /// <summary>What is stored.</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>Run against the built-in simulated channel rather than a radio (--simulate).</summary>
    public bool Simulate { get; init; }

    /// <summary>The app's version, as the release pipeline stamped it ("0.0.0-dev" from a plain build).</summary>
    public string Version { get; } =
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainViewModel).Assembly)
            ?.InformationalVersion ?? "dev").Split('+')[0];

    /// <summary>Whether the station is up.</summary>
    public bool IsLive => Status == StationStatus.Live;

    /// <summary>The modes on offer.</summary>
    public IReadOnlyList<ModeChoice> Modes { get; }

    /// <summary>The transmit test presets.</summary>
    public IReadOnlyList<TxTestPreset> TxTestPresets => TxTestPreset.All;

    /// <summary>Spectrum and waterfall lines.</summary>
    public BandFeed Band { get; } = new();

    /// <summary>The monitor pane.</summary>
    public MonitorViewModel Monitor { get; } = new();

    /// <summary>The heard list.</summary>
    public HeardViewModel Heard { get; } = new();

    /// <summary>The sessions pane and input line.</summary>
    public SessionsViewModel Sessions { get; }

    /// <summary>The level helper.</summary>
    public LevelHelperViewModel Levels { get; } = new();

    /// <summary>Raised when the operator asks for settings; the view opens the dialog.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the operator asks for the level helper; the view shows the pane.</summary>
    public event Action? LevelHelperRequested;

    /// <summary>Puts the station on the air with the stored settings.</summary>
    public async Task StartAsync()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;
        int generation = ++_generation;
        try
        {
            _retry.Stop();
            await StopAsync();
            if (Simulate)
            {
                await StartSimulatedAsync(generation);
                return;
            }

            if (!Settings.IsComplete)
            {
                Status = StationStatus.Offline;
                StatusText = Settings.MyCall.Length == 0 ? "Set your callsign in Settings" : "Choose an interface in Settings";
                return;
            }

            Status = StationStatus.Starting;
            StatusText = "Opening the interface...";
            InterfaceName = Settings.Interface.Name ?? "Interface";

            IReadOnlyList<RadioInterface> present = await Task.Run(RadioInterfaces.Discover);
            if (InterfaceResolver.Resolve(Settings.Interface, present) is not { } resolved)
            {
                Fault($"{Settings.Interface.Name ?? "The interface"} is not plugged in; waiting for it", retry: true);
                return;
            }

            if (resolved.Settings != Settings.Interface)
            {
                Save(Settings with { Interface = resolved.Settings });
            }

            await ApplyHygieneAsync(resolved.Settings);
            OpenLevels(resolved.Settings);

            WindowsSoundCard card = await Task.Run(() => WindowsSoundCard.Open(resolved.Settings));
            card.Faulted += ex => OnUi(() =>
            {
                if (generation == _generation)
                {
                    Fault($"{InterfaceName} stopped: {ex.Message}", retry: true);
                    _ = StopAsync();
                }
            });
            _card = card;

            await RunStationAsync(card, generation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            await StopAsync();
            Fault(ex.Message, retry: false);
        }
        finally
        {
            _starting = false;
        }
    }

    private async Task StartSimulatedAsync(int generation)
    {
        Status = StationStatus.Starting;
        InterfaceName = "Simulator";
        if (Settings.MyCall.Length == 0)
        {
            MyCall = "N0CALL";
            Settings = Settings with { MyCall = MyCall };
        }

        _simulation = await SimulatedChannel.StartAsync(Settings.Modem.Mode);
        Sessions.Console.Note($"simulator: a channel with {SimulatedChannel.NodeCall} on it; try C {SimulatedChannel.NodeCall}");
        await RunStationAsync(_simulation.Card, generation);
    }

    private async Task RunStationAsync(ISoundCard card, int generation)
    {
        var station = new SoundModemStation(card, new SoundModemStationOptions
        {
            Mode = Settings.Modem.Mode,
            CentreFrequencyHz = Settings.Modem.CentreFrequencyHz,
            ChannelAccess = Settings.Modem.ChannelAccess,
            Modes = FmModes.Names,
        });
        _station = station;
        Band.Clear();
        station.FrameHeard += frame => OnFrame(frame, generation);
        station.TransmittingChanged += keyed => OnUi(() => Transmitting = keyed);
        station.Notice += notice => OnUi(() => Note(notice));
        station.SpectrumLine += line => Band.Push(line, _transmittingNow);
        station.InputLevel += reading => OnUi(() => OnLevel(reading));
        station.TransmittingChanged += keyed => _transmittingNow = keyed;

        var sessions = new SessionManager(station.Transport, new SessionOptions
        {
            MyCall = Callsign.Parse(Settings.MyCall),
            AcceptIncoming = Settings.Sessions.AcceptIncoming,
            WelcomeText = Settings.Sessions.WelcomeText.Replace("{MYCALL}", Settings.MyCall, StringComparison.OrdinalIgnoreCase),
            Paclen = Settings.Sessions.Paclen,
        });
        sessions.SessionOpened += session => OnUi(() => Sessions.Attach(session, OnUi));
        await sessions.StartAsync();
        _sessions = sessions;
        ConfigureBeacon();

        Sessions.Online = true;
        Status = StationStatus.Live;
        StatusText = $"{InterfaceName} - {station.Mode} - {Settings.MyCall}";
        Sessions.Console.Note($"station on the air as {Settings.MyCall}, {station.Mode} on {InterfaceName}");
    }

    /// <summary>Takes the station off the air and releases the interface.</summary>
    public async Task StopAsync()
    {
        _beacon.Configure(new BeaconSettings(false, TimeSpan.FromHours(1), Callsign.Parse("BEACON"), [], string.Empty));
        Sessions.Online = false;
        SessionManager? sessions = _sessions;
        SoundModemStation? station = _station;
        WindowsSoundCard? card = _card;
        _sessions = null;
        _station = null;
        _card = null;
        if (sessions is not null)
        {
            await sessions.DisposeAsync();
        }

        if (station is not null)
        {
            // The station disposes its card.
            await station.DisposeAsync();
        }
        else
        {
            card?.Dispose();
        }

        if (_simulation is { } simulation)
        {
            _simulation = null;
            await simulation.DisposeAsync();
        }

        _rxLevel?.Dispose();
        _txLevel?.Dispose();
        _rxLevel = null;
        _txLevel = null;
        LevelsAvailable = false;
        Transmitting = false;
        ChannelBusy = false;
        if (Status != StationStatus.Fault)
        {
            Status = StationStatus.Offline;
        }
    }

    /// <summary>Stores new settings and restarts the station with them.</summary>
    public async Task ApplySettingsAsync(AppSettings settings)
    {
        Save(settings);
        MyCall = settings.MyCall;
        SelectedMode = Modes.FirstOrDefault(m => m.Mode == settings.Modem.Mode) ?? SelectedMode;
        Levels.Reset();
        await StartAsync();
    }

    /// <summary>Remembers the dock layout and window placement.</summary>
    public void SaveUi(UiSettings ui) => Save(Settings with { Ui = ui });

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _fast.Stop();
        _slow.Stop();
        _retry.Stop();
        _beacon.Dispose();
        await StopAsync();
    }

    partial void OnSelectedModeChanged(ModeChoice? value)
    {
        if (value is null)
        {
            return;
        }

        UpdatePassband(value.Mode);
        if (Settings.Modem.Mode != value.Mode)
        {
            Save(Settings with { Modem = Settings.Modem with { Mode = value.Mode } });
        }

        if (_station is { } station && station.Mode != value.Mode)
        {
            _ = SetModeAsync(station, value.Mode);
            _ = _simulation?.SetModeAsync(value.Mode);
        }
    }

    partial void OnRxLevelDbChanged(double value)
    {
        if (_applyingLevels || _rxLevel is null)
        {
            return;
        }

        _rxLevel.LevelDb = value;
        Save(Settings with { Interface = Settings.Interface with { CaptureLevelDb = Math.Round(value, 1) } });
    }

    partial void OnTxLevelDbChanged(double value)
    {
        if (_applyingLevels || _txLevel is null)
        {
            return;
        }

        _txLevel.LevelDb = value;
        Save(Settings with { Interface = Settings.Interface with { RenderLevelDb = Math.Round(value, 1) } });
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void ShowLevelHelper() => LevelHelperRequested?.Invoke();

    [RelayCommand]
    private Task Restart() => StartAsync();

    [RelayCommand]
    private async Task ToggleTxTestAsync()
    {
        if (_station is not { } station)
        {
            return;
        }

        if (station.TxTestRunning)
        {
            station.StopTxTest();
            return;
        }

        TxTestRunning = true;
        try
        {
            await station.RunTxTestAsync(new TxTestRequest(false, SelectedPreset.ToneHz, TxTestSeconds));
        }
        catch (InvalidOperationException ex)
        {
            Sessions.Console.Note(ex.Message);
        }
        finally
        {
            TxTestRunning = false;
        }
    }

    [RelayCommand]
    private async Task SendBeaconNowAsync()
    {
        if (_sessions is null)
        {
            Sessions.Console.Note("the station is not running");
            return;
        }

        await SendBeaconAsync(BeaconFromSettings());
        Sessions.Console.Note("beacon sent");
    }

    private async Task SetModeAsync(SoundModemStation station, string mode)
    {
        try
        {
            await station.SetModeAsync(mode);
            StatusText = $"{InterfaceName} - {mode} - {Settings.MyCall}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Sessions.Console.Note("mode not changed: " + ex.Message);
        }
    }

    private void UpdatePassband(string mode)
    {
        double centre = ModemCatalog.DefaultCentreFrequencyFor(mode) ?? 1700;
        ModeCentreHz = centre;
        Passband = mode.StartsWith("afsk1200", StringComparison.Ordinal)
            ? new Passband(centre - 800, centre + 800, $"0 {mode}", [centre - 500, centre + 500])
            : new Passband(centre - 1300, centre + 1300, $"0 {mode}", []);
    }

    private async Task ApplyHygieneAsync(InterfaceSettings settings)
    {
        var remaining = new List<string>();
        foreach ((string? id, AudioFlow flow) in new[] { (settings.CaptureEndpointId, AudioFlow.Capture), (settings.RenderEndpointId, AudioFlow.Render) })
        {
            if (id is null)
            {
                continue;
            }

            IReadOnlyList<EndpointIssue> found = await Task.Run(() => EndpointHygiene.Check(id, flow));
            if (found.Count == 0)
            {
                continue;
            }

            foreach (EndpointIssue issue in found.Where(i => i.CanFix))
            {
                Sessions.Console.Note($"fixed {(flow == AudioFlow.Capture ? "receive" : "transmit")} audio: {issue.Description}");
            }

            IReadOnlyList<EndpointIssue> left = await Task.Run(() => EndpointHygiene.Fix(id, flow));
            remaining.AddRange(left.Select(i => i.Description));
        }

        HygieneWarning = remaining.Count == 0 ? null : string.Join(" ", remaining);
        foreach (string text in remaining)
        {
            Sessions.Console.Note("check: " + text);
        }
    }

    private void OpenLevels(InterfaceSettings settings)
    {
        _applyingLevels = true;
        try
        {
            _rxLevel = EndpointLevel.Open(settings.CaptureEndpointId!);
            _txLevel = EndpointLevel.Open(settings.RenderEndpointId!);
            if (settings.CaptureLevelDb is { } rx)
            {
                _rxLevel.LevelDb = rx;
            }

            if (settings.RenderLevelDb is { } tx)
            {
                _txLevel.LevelDb = tx;
            }

            RxMinDb = Math.Max(_rxLevel.MinDb, -60);
            RxMaxDb = _rxLevel.CeilingDb;
            TxMinDb = Math.Max(_txLevel.MinDb, -60);
            TxMaxDb = _txLevel.CeilingDb;
            ReadLevels();
            _rxLevel.Changed += () => OnUi(ReadLevels);
            _txLevel.Changed += () => OnUi(ReadLevels);
            LevelsAvailable = true;
        }
        finally
        {
            _applyingLevels = false;
        }
    }

    private void ReadLevels()
    {
        if (_rxLevel is null || _txLevel is null)
        {
            return;
        }

        _applyingLevels = true;
        try
        {
            // Something else on the machine may have pushed a level above the ceiling; put it
            // back, since nothing above 0 dB is ever right for a radio interface.
            if (_rxLevel.LevelDb > _rxLevel.CeilingDb + 0.01)
            {
                _rxLevel.LevelDb = _rxLevel.CeilingDb;
            }

            if (_txLevel.LevelDb > _txLevel.CeilingDb + 0.01)
            {
                _txLevel.LevelDb = _txLevel.CeilingDb;
            }

            RxLevelDb = Math.Round(_rxLevel.LevelDb, 1);
            TxLevelDb = Math.Round(_txLevel.LevelDb, 1);
        }
        finally
        {
            _applyingLevels = false;
        }
    }

    private void OnFrame(HeardFrame frame, int generation)
    {
        MonitorEntry entry = MonitorFormatter.Format(frame);
        if (entry.Direction == FrameDirection.Received && entry.Source is { } source)
        {
            string snr = frame.Quality?.SnrDb is { } s ? string.Create(CultureInfo.InvariantCulture, $" {s:0.0} dB") : string.Empty;
            double hz = ModeCentreHz + (frame.Quality?.FrequencyOffsetHz ?? 0);
            Band.Tag(hz, MonitorFormatter.Callsign(source) + snr, transmitted: false);
        }
        else if (entry.Direction == FrameDirection.Transmitted && entry.Source is { } me)
        {
            Band.Tag(ModeCentreHz, MonitorFormatter.Callsign(me) + " tx", transmitted: true);
        }

        OnUi(() =>
        {
            if (generation != _generation)
            {
                return;
            }

            Monitor.Add(entry);
            Heard.Add(entry);
            if (entry.Direction == FrameDirection.Received)
            {
                FramesHeard++;
            }
            else
            {
                FramesSent++;
            }
        });
    }

    private void OnLevel(LevelReading reading)
    {
        PeakDb = reading.PeakDbFs;
        RmsDb = reading.RmsDbFs;
        LevelText = reading.PeakDbFs <= -119 ? "no signal" : string.Create(CultureInfo.InvariantCulture, $"{reading.PeakDbFs:0.0} dBFS");
        if (reading.Clipped)
        {
            // Latched: a clip is one reading in five a second and would blink past unseen.
            _clipUntil = DateTime.UtcNow.AddSeconds(3);
            Clipping = true;
        }

        Levels.Add(reading);
    }

    private void FastTick()
    {
        ChannelBusy = _station is { } station && station.ChannelBusy && !Transmitting;
        if (Clipping && DateTime.UtcNow > _clipUntil)
        {
            Clipping = false;
        }

        TxTestRunning = _station?.TxTestRunning ?? false;
    }

    private async Task RetryAsync()
    {
        if (Status == StationStatus.Fault && !_starting)
        {
            await StartAsync();
        }
    }

    private void Fault(string text, bool retry)
    {
        Status = StationStatus.Fault;
        StatusText = text;
        Sessions.Console.Note(text);
        if (retry)
        {
            _retry.Start();
        }
    }

    private void Note(StationNotice notice)
    {
        LastNotice = notice.Text;
        if (notice.Level != NoticeLevel.Info || notice.Text.StartsWith("tx test", StringComparison.Ordinal) || notice.Text.StartsWith("mode", StringComparison.Ordinal))
        {
            Sessions.Console.Note(notice.Text);
        }
    }

    private void ConfigureBeacon() => _beacon.Configure(BeaconFromSettings());

    private BeaconSettings BeaconFromSettings()
    {
        BeaconSettingsModel b = Settings.Beacon;
        string[] path = b.Path.ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Callsign destination = path.Length > 0 && Callsign.TryParse(path[0], out Callsign d) ? d : Callsign.Parse("BEACON");
        var via = path.Skip(1).Select(p => Callsign.TryParse(p, out Callsign c) ? c : (Callsign?)null).OfType<Callsign>().ToList();
        return new BeaconSettings(b.Enabled, TimeSpan.FromMinutes(b.IntervalMinutes), destination, via,
            b.Text.Replace("{MYCALL}", Settings.MyCall, StringComparison.OrdinalIgnoreCase));
    }

    private Task SendBeaconAsync(BeaconSettings beacon) =>
        _sessions?.SendUiAsync(beacon.Destination, beacon.Text, beacon.Via) ?? Task.CompletedTask;

    private void RememberCall(string call)
    {
        var recent = Settings.Sessions.RecentCalls.Where(c => !string.Equals(c, call, StringComparison.OrdinalIgnoreCase)).Prepend(call).Take(20).ToList();
        Save(Settings with { Sessions = Settings.Sessions with { RecentCalls = recent } });
    }

    private void Save(AppSettings settings)
    {
        Settings = settings;
        try
        {
            _store.Save(settings);
        }
        catch (IOException ex)
        {
            LastNotice = "settings not saved: " + ex.Message;
        }
    }

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ui.BeginInvoke(action);
        }
    }
}
