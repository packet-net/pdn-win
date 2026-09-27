using System.Text;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Ax25.Transport;
using Packet.Core;
using PdnWin.Core.Monitoring;

namespace PdnWin.Core.Sessions;

/// <summary>How the session layer behaves on air.</summary>
public sealed record SessionOptions
{
    /// <summary>Our callsign, with SSID.</summary>
    public required Callsign MyCall { get; init; }

    /// <summary>Whether other stations may connect to us.</summary>
    public bool AcceptIncoming { get; init; } = true;

    /// <summary>Sent to a station that connects to us; null or empty sends nothing.</summary>
    public string? WelcomeText { get; init; }

    /// <summary>The most information bytes per I-frame. 128 suits 1200 baud.</summary>
    public int Paclen { get; init; } = 128;

    /// <summary>The idle-link poll interval (T3).</summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>How long a connect may take before it is given up.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(90);
}

/// <summary>
/// The connected-mode side of the station: many simultaneous sessions over one packet.net
/// <see cref="Ax25Listener"/>, as QtTermTCP has a tab per connection.
/// </summary>
/// <remarks>
/// <para><b>On-air behaviour is set here, never inherited</b>, the same policy as packet-term-tui
/// and axcall: a 300 s T3 rather than the library's 30 s (an idle terminal should not poll a node
/// twice a minute on a shared channel), a plain SABM dial rather than SABME-first (mod-8 nodes
/// answer SABME with FRMR, and the fallback skips the pre-connect XID that negotiates SREJ), and
/// T1 timed from the end of our own transmission, because at 1200 baud with a handheld's TXDELAY
/// the time a frame spends queued and on air is most of a default T1.</para>
/// <para><b>Links are found by the other station's callsign.</b> The listener keeps one
/// <see cref="Ax25Session"/> per peer and reuses it, and its signals can arrive before the call
/// that caused them has returned: the peer's first I-frame can land before
/// <see cref="Ax25Listener.ConnectAsync(Callsign, CancellationToken)"/> completes, and
/// <see cref="Ax25Listener.SessionAccepted"/> fires for our own outbound connects as well as for
/// incoming ones. So the session that owns a peer is recorded the moment a dial starts or a
/// connect is accepted, and everything is routed by the peer's callsign.</para>
/// <para><b>Direct connects only, for now.</b> The listener dials without a digipeater path;
/// connecting via a digipeater needs that in packet.net first. UI frames do take a path (see
/// <see cref="SendUiAsync"/>).</para>
/// </remarks>
public sealed class SessionManager : IAsyncDisposable
{
    private readonly IAx25Transport _transport;
    private readonly SessionOptions _options;
    private readonly Ax25Listener _listener;
    private readonly Lock _gate = new();
    private readonly Dictionary<Callsign, Owner> _owners = [];
    private readonly List<PacketSession> _sessions = [];
    private readonly TimeProvider _time;
    private int _paclen;

    /// <summary>Builds the manager; nothing happens on air until <see cref="StartAsync"/>.</summary>
    public SessionManager(IAx25Transport transport, SessionOptions options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        _transport = transport;
        _options = options;
        _time = time ?? TimeProvider.System;
        _listener = new Ax25Listener(transport, new Ax25ListenerOptions
        {
            MyCall = options.MyCall,
            T3 = options.KeepAlive,
            PreferExtendedConnect = false,
            PreConnectXidNegotiatesSrej = true,
            RestartT1OnTxComplete = transport is ITxCompletionTransport,
            ConfigureSession = link => link.DataLinkSignalEmitted += OnSignal,
        });
        _listener.AcceptIncoming = options.AcceptIncoming;
        _listener.SessionAccepted += OnAccepted;
        WelcomeText = options.WelcomeText;
        Paclen = options.Paclen;
    }

    /// <summary>Our callsign.</summary>
    public Callsign MyCall => _options.MyCall;

    /// <summary>Every session this run has had, oldest first.</summary>
    public IReadOnlyList<PacketSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _sessions];
            }
        }
    }

    /// <summary>Whether other stations may connect to us.</summary>
    public bool AcceptIncoming
    {
        get => _listener.AcceptIncoming;
        set => _listener.AcceptIncoming = value;
    }

    /// <summary>Sent to a station that connects to us, from the next one on; null or empty sends nothing.</summary>
    public string? WelcomeText { get; set; }

    /// <summary>The most information bytes per I-frame, from the next line sent.</summary>
    public int Paclen
    {
        get => _paclen;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _paclen = value;
        }
    }

    /// <summary>Raised when a session starts a new link, outgoing or incoming: when it is created,
    /// and when a disconnected one is reused for the same station. On a stack thread.</summary>
    public event Action<PacketSession>? SessionOpened;

    /// <summary>Starts listening.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default) => _listener.StartAsync(cancellationToken);

    /// <summary>
    /// Connects to <paramref name="remote"/>. The session exists (Connecting) as soon as this is
    /// called, so its tab can show progress; the task completes when it is up or has failed, and a
    /// failure is reported on the session's transcript rather than thrown.
    /// </summary>
    /// <exception cref="InvalidOperationException">We already have a live link to that station.</exception>
    public async Task<PacketSession> ConnectAsync(Callsign remote, CancellationToken cancellationToken = default)
    {
        PacketSession session;
        lock (_gate)
        {
            if (_owners.TryGetValue(remote, out Owner? owner) && owner.Session.State != LinkState.Disconnected)
            {
                throw new InvalidOperationException($"already connected to {MonitorFormatter.Callsign(remote)}");
            }

            session = _sessions.LastOrDefault(s => s.Remote.Equals(remote) && !s.Incoming && s.State == LinkState.Disconnected)
                ?? new PacketSession(this, remote, incoming: false, LinkState.Connecting);
            if (!_sessions.Contains(session))
            {
                _sessions.Add(session);
            }
        }

        SessionOpened?.Invoke(session);

        await DialAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <summary>
    /// Sends a UI (unproto) frame: a beacon, a CQ, an APRS-style status. Built here rather than by
    /// the listener because the listener's UI path takes no digipeater path.
    /// </summary>
    public Task SendUiAsync(Callsign destination, string text, IReadOnlyList<Callsign>? via = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] info = Encoding.Latin1.GetBytes(text.EndsWith('\r') ? text : text + "\r");
        Ax25Frame frame = Ax25Frame.Ui(destination, _options.MyCall, info, Ax25Frame.PidNoLayer3, isCommand: true, digipeaters: via);
        return _transport.SendAsync(frame.ToBytes(), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _listener.DisposeAsync().ConfigureAwait(false);

    internal void Send(PacketSession session, string text)
    {
        Ax25Session? link;
        lock (_gate)
        {
            link = _owners.TryGetValue(session.Remote, out Owner? owner) && ReferenceEquals(owner.Session, session) ? owner.Link : null;
        }

        if (link is null || session.State != LinkState.Connected)
        {
            throw new InvalidOperationException($"not connected to {MonitorFormatter.Callsign(session.Remote)}");
        }

        byte[] bytes = Encoding.Latin1.GetBytes(text + "\r");
        int paclen = Paclen;
        for (int offset = 0; offset < bytes.Length; offset += paclen)
        {
            int length = Math.Min(paclen, bytes.Length - offset);
            _listener.SendData(link, bytes.AsMemory(offset, length));
        }

        session.Emit(new SessionLine(_time.GetUtcNow(), SessionLineKind.Sent, text));
    }

    internal async Task DisconnectAsync(PacketSession session, CancellationToken cancellationToken)
    {
        Ax25Session? link;
        lock (_gate)
        {
            link = _owners.TryGetValue(session.Remote, out Owner? owner) && ReferenceEquals(owner.Session, session) ? owner.Link : null;
        }

        if (link is null || session.State is LinkState.Disconnected)
        {
            return;
        }

        session.SetState(LinkState.Disconnecting);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, DataLinkSignal signal)
        {
            if (signal is DataLinkDisconnectConfirm or DataLinkDisconnectIndication)
            {
                done.TrySetResult();
            }
        }

        link.DataLinkSignalEmitted += Handler;
        try
        {
            link.PostEvent(new DlDisconnectRequest());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30), _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Note(session, "no answer to the disconnect; treating the link as down");
            Down(session);
        }
        finally
        {
            link.DataLinkSignalEmitted -= Handler;
        }
    }

    internal Task ReconnectAsync(PacketSession session, CancellationToken cancellationToken) =>
        session.State == LinkState.Disconnected ? DialAsync(session, cancellationToken) : Task.CompletedTask;

    private async Task DialAsync(PacketSession session, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _owners[session.Remote] = new Owner(session);
        }

        session.SetState(LinkState.Connecting);
        Note(session, $"connecting to {MonitorFormatter.Callsign(session.Remote)}");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.ConnectTimeout);
        try
        {
            Ax25Session link = await _listener.ConnectAsync(session.Remote, budget.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_owners.TryGetValue(session.Remote, out Owner? owner) && ReferenceEquals(owner.Session, session))
                {
                    owner.Link = link;
                }
            }

            session.SetState(LinkState.Connected);
            Note(session, $"connected to {MonitorFormatter.Callsign(session.Remote)}");
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            Down(session);
            Note(session, cancellationToken.IsCancellationRequested
                ? "connect abandoned"
                : $"no answer from {MonitorFormatter.Callsign(session.Remote)}");
        }
        catch (InvalidOperationException ex)
        {
            Down(session);
            Note(session, $"{MonitorFormatter.Callsign(session.Remote)} refused the connection ({ex.Message})");
        }
    }

    private void OnAccepted(object? sender, Ax25SessionEventArgs e)
    {
        Ax25Session link = e.Session;
        Callsign peer = link.Context.Remote;
        PacketSession session;
        lock (_gate)
        {
            if (_owners.TryGetValue(peer, out Owner? owner) && owner.Session.State != LinkState.Disconnected)
            {
                // Our own dial completing (the listener raises this for outbound connects too),
                // or a repeat for a link already up. Either way the owner stands.
                owner.Link = link;
                return;
            }

            session = _sessions.LastOrDefault(s => s.Remote.Equals(peer) && s.Incoming && s.State == LinkState.Disconnected)
                ?? new PacketSession(this, peer, incoming: true, LinkState.Connected);
            if (!_sessions.Contains(session))
            {
                _sessions.Add(session);
            }

            _owners[peer] = new Owner(session) { Link = link };
        }

        SessionOpened?.Invoke(session);

        session.SetState(LinkState.Connected);
        Note(session, $"{MonitorFormatter.Callsign(peer)} connected to us");
        if (WelcomeText is { } welcome && !string.IsNullOrWhiteSpace(welcome))
        {
            foreach (string line in welcome.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                Send(session, line);
            }
        }
    }

    private void OnSignal(object? sender, DataLinkSignal signal)
    {
        if (sender is not Ax25Session link)
        {
            return;
        }

        Owner? owner;
        lock (_gate)
        {
            _owners.TryGetValue(link.Context.Remote, out owner);
            if (owner is not null)
            {
                owner.Link ??= link;
            }
        }

        if (owner is null)
        {
            return;
        }

        PacketSession session = owner.Session;
        switch (signal)
        {
            case DataLinkDataIndication data:
                Deliver(owner, data.Info.Span);
                break;

            case DataLinkDisconnectIndication or DataLinkDisconnectConfirm:
                // A dial in progress hears about its own refusal through ConnectAsync.
                if (session.State is LinkState.Connected or LinkState.Disconnecting)
                {
                    Note(session, $"disconnected from {MonitorFormatter.Callsign(session.Remote)}");
                    Down(session);
                }

                break;

            case DataLinkErrorIndication error:
                Note(session, "link error " + error.Code);
                break;
        }
    }

    private void Deliver(Owner owner, ReadOnlySpan<byte> info)
    {
        ReceivedText.Chunk chunk = ReceivedText.Split(info);
        bool open;
        lock (_gate)
        {
            open = owner.LineOpen;
            owner.LineOpen = chunk.Lines.Count != 0 && chunk.Incomplete;
        }

        DateTimeOffset now = _time.GetUtcNow();
        for (int i = 0; i < chunk.Lines.Count; i++)
        {
            owner.Session.Emit(new SessionLine(now, SessionLineKind.Received, chunk.Lines[i], Continues: i == 0 && open));
        }
    }

    private void Down(PacketSession session)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(session.Remote, out Owner? owner) && ReferenceEquals(owner.Session, session))
            {
                _owners.Remove(session.Remote);
            }
        }

        session.SetState(LinkState.Disconnected);
    }

    private void Note(PacketSession session, string text) =>
        session.Emit(new SessionLine(_time.GetUtcNow(), SessionLineKind.Notice, text));

    /// <summary>Which session a peer's link belongs to, and the link once it is known.</summary>
    private sealed class Owner(PacketSession session)
    {
        public PacketSession Session { get; } = session;

        public Ax25Session? Link { get; set; }

        public bool LineOpen { get; set; }
    }
}
