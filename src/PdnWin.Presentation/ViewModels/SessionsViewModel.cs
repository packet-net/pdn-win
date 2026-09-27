using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Packet.Core;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Sessions;

namespace PdnWin.ViewModels;

/// <summary>One transcript line.</summary>
public sealed partial class TranscriptLine : ObservableObject
{
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>Whose it is.</summary>
    public required SessionLineKind Kind { get; init; }

    /// <summary>HH:mm local.</summary>
    public required string Time { get; init; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>A tab in the sessions pane: the console, or one connection.</summary>
public sealed partial class SessionTabViewModel : ObservableObject
{
    private const int Capacity = 6000;
    private readonly Action<SessionTabViewModel> _close;
    private TranscriptLine? _lastReceived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsBusy), nameof(IsDown))]
    private LinkState _state = LinkState.Disconnected;

    [ObservableProperty]
    private bool _hasUnread;

    [ObservableProperty]
    private bool _isSelected;

    internal SessionTabViewModel(string title, PacketSession? session, Action<SessionTabViewModel> close)
    {
        Title = title;
        Session = session;
        _close = close;
    }

    /// <summary>The tab text.</summary>
    public string Title { get; }

    /// <summary>The connection, or null for the console.</summary>
    public PacketSession? Session { get; }

    /// <summary>Whether this is the console tab.</summary>
    public bool IsConsole => Session is null;

    /// <summary>Whether the link is up.</summary>
    public bool IsConnected => State == LinkState.Connected;

    /// <summary>Whether it is connecting or disconnecting.</summary>
    public bool IsBusy => State is LinkState.Connecting or LinkState.Disconnecting;

    /// <summary>Whether it is down (and so can be reconnected).</summary>
    public bool IsDown => State == LinkState.Disconnected && !IsConsole;

    /// <summary>The transcript.</summary>
    public ObservableCollection<TranscriptLine> Lines { get; } = [];

    /// <summary>Raised after a line is added, for auto-scrolling.</summary>
    public event Action? LineAdded;

    /// <summary>Adds a line. UI thread.</summary>
    public void Add(SessionLine line)
    {
        if (line.Continues && line.Kind == SessionLineKind.Received && _lastReceived is not null)
        {
            _lastReceived.Text += line.Text;
            LineAdded?.Invoke();
            return;
        }

        var item = new TranscriptLine
        {
            Kind = line.Kind,
            Time = line.Time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
            Text = line.Kind == SessionLineKind.Notice ? "*** " + line.Text : line.Text,
        };
        Lines.Add(item);
        if (line.Kind == SessionLineKind.Received)
        {
            _lastReceived = item;
        }

        if (Lines.Count > Capacity)
        {
            Lines.RemoveAt(0);
        }

        if (!IsSelected && line.Kind == SessionLineKind.Received)
        {
            HasUnread = true;
        }

        LineAdded?.Invoke();
    }

    /// <summary>A console notice.</summary>
    public void Note(string text) => Add(new SessionLine(DateTimeOffset.Now, SessionLineKind.Notice, text));

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            HasUnread = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (Session is not null)
        {
            await Session.ReconnectAsync();
        }
    }

    [RelayCommand]
    private void Close() => _close(this);
}

/// <summary>The sessions pane and the input line.</summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly Func<SessionManager?> _manager;
    private readonly Action<string> _rememberCall;
    private readonly List<string> _history = [];
    private int _historyIndex;

    [ObservableProperty]
    private SessionTabViewModel _selected;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _connectTo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Prompt))]
    private bool _online;

    /// <summary>Creates the pane over the manager the station currently has.</summary>
    public SessionsViewModel(Func<SessionManager?> manager, Action<string> rememberCall)
    {
        _manager = manager;
        _rememberCall = rememberCall;
        Console = new SessionTabViewModel("Console", null, _ => { });
        Tabs.Add(Console);
        _selected = Console;
        Console.IsSelected = true;
        Console.Note("Type C CALLSIGN to connect, or U DEST text to send unproto. Enter on a session tab sends to that station.");
    }

    /// <summary>The console tab.</summary>
    public SessionTabViewModel Console { get; }

    /// <summary>The console and every session.</summary>
    public ObservableCollection<SessionTabViewModel> Tabs { get; } = [];

    /// <summary>Recently connected calls for the connect box.</summary>
    public ObservableCollection<string> RecentCalls { get; } = [];

    /// <summary>What the input line is talking to.</summary>
    public string Prompt => Selected is { IsConsole: false } tab ? $"{tab.Title} >" : Online ? "cmd >" : "offline";

    /// <summary>Wires a session into a tab. Called on the UI thread.</summary>
    public void Attach(PacketSession session, Action<Action> onUi)
    {
        ArgumentNullException.ThrowIfNull(session);
        SessionTabViewModel? tab = Tabs.FirstOrDefault(t => ReferenceEquals(t.Session, session));
        if (tab is null)
        {
            tab = new SessionTabViewModel(MonitorFormatter.Callsign(session.Remote), session, CloseTab)
            {
                State = session.State,
            };
            session.Line += (_, line) => onUi(() => tab.Add(line));
            session.StateChanged += (_, state) => onUi(() =>
            {
                tab.State = state;
                OnPropertyChanged(nameof(Prompt));
            });
            Tabs.Add(tab);
        }

        Selected = tab;
    }

    partial void OnSelectedChanged(SessionTabViewModel? oldValue, SessionTabViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        OnPropertyChanged(nameof(Prompt));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        string text = ConnectTo.Trim().ToUpperInvariant();
        if (!Callsign.TryParse(text, out Callsign remote))
        {
            Console.Note($"'{ConnectTo}' is not a callsign");
            Selected = Console;
            return;
        }

        await DialAsync(remote);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        string line = Input;
        Input = string.Empty;
        if (line.Length > 0)
        {
            _history.Remove(line);
            _history.Add(line);
        }

        _historyIndex = _history.Count;
        SessionTabViewModel tab = Selected;

        if (tab.Session is { } session)
        {
            if (session.State != LinkState.Connected)
            {
                tab.Note("not connected; press Reconnect to call again");
                return;
            }

            try
            {
                session.Send(line);
            }
            catch (InvalidOperationException ex)
            {
                tab.Note(ex.Message);
            }

            return;
        }

        if (line.Trim().Length == 0)
        {
            return;
        }

        Console.Add(new SessionLine(DateTimeOffset.Now, SessionLineKind.Sent, line));
        switch (InputCommand.Parse(line))
        {
            case InputCommand.Connect connect:
                await DialAsync(connect.Remote);
                break;

            case InputCommand.Unproto unproto when _manager() is { } manager:
                try
                {
                    await manager.SendUiAsync(unproto.Destination, unproto.Text, unproto.Via);
                    Console.Note($"sent unproto to {MonitorFormatter.Callsign(unproto.Destination)}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    Console.Note("not sent: " + ex.Message);
                }

                break;

            case InputCommand.Unproto:
                Console.Note("the station is not running");
                break;

            case InputCommand.Invalid invalid:
                Console.Note(invalid.Message);
                break;
        }
    }

    /// <summary>Steps back through what was typed.</summary>
    public void HistoryUp()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        Input = _history[_historyIndex];
    }

    /// <summary>Steps forward through what was typed.</summary>
    public void HistoryDown()
    {
        _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
        Input = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
    }

    private async Task DialAsync(Callsign remote)
    {
        if (_manager() is not { } manager)
        {
            Console.Note("the station is not running; check Settings");
            Selected = Console;
            return;
        }

        string call = MonitorFormatter.Callsign(remote);
        _rememberCall(call);
        RecentCalls.Remove(call);
        RecentCalls.Insert(0, call);
        ConnectTo = string.Empty;
        try
        {
            await manager.ConnectAsync(remote);
        }
        catch (InvalidOperationException ex)
        {
            Console.Note(ex.Message);
        }
    }

    private void CloseTab(SessionTabViewModel tab)
    {
        if (tab.IsConsole)
        {
            return;
        }

        if (tab.Session is { State: not LinkState.Disconnected } session)
        {
            _ = session.DisconnectAsync();
        }

        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (ReferenceEquals(Selected, tab) || Selected is null)
        {
            Selected = Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)];
        }
    }
}
