using Packet.Core;

namespace PdnWin.Core.Sessions;

/// <summary>Where a connected-mode link is.</summary>
public enum LinkState
{
    /// <summary>SABM sent, waiting for UA.</summary>
    Connecting,

    /// <summary>Up.</summary>
    Connected,

    /// <summary>DISC sent, waiting for UA.</summary>
    Disconnecting,

    /// <summary>Down.</summary>
    Disconnected,
}

/// <summary>What a transcript line is.</summary>
public enum SessionLineKind
{
    /// <summary>Text from the other station.</summary>
    Received,

    /// <summary>Text we sent.</summary>
    Sent,

    /// <summary>The session's own notices: connected, disconnected, retries exceeded.</summary>
    Notice,
}

/// <summary>One line of a session's transcript.</summary>
/// <param name="Time">When.</param>
/// <param name="Kind">Whose it is.</param>
/// <param name="Text">The text, without its line terminator.</param>
/// <param name="Continues">It continues the previous line of the same kind rather than starting
/// a new one (a line longer than a frame arrives in pieces).</param>
public sealed record SessionLine(DateTimeOffset Time, SessionLineKind Kind, string Text, bool Continues = false);

/// <summary>
/// One conversation with one station: a tab in the UI. Created by <see cref="SessionManager"/> for
/// an outgoing connect or an accepted incoming one, and kept (Disconnected) after the link drops so
/// that its transcript survives and it can be reconnected.
/// </summary>
public sealed class PacketSession
{
    private readonly SessionManager _manager;
    private readonly Lock _gate = new();
    private LinkState _state;

    internal PacketSession(SessionManager manager, Callsign remote, bool incoming, LinkState state)
    {
        _manager = manager;
        Remote = remote;
        Incoming = incoming;
        _state = state;
    }

    /// <summary>The other station.</summary>
    public Callsign Remote { get; }

    /// <summary>Whether they called us.</summary>
    public bool Incoming { get; }

    /// <summary>The link's state.</summary>
    public LinkState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Raised for every transcript line. On a stack thread; marshal to the UI.</summary>
    public event Action<PacketSession, SessionLine>? Line;

    /// <summary>Raised when <see cref="State"/> changes. On a stack thread.</summary>
    public event Action<PacketSession, LinkState>? StateChanged;

    /// <summary>
    /// Sends a line of text, CR-terminated, split into frames of at most the configured PACLEN.
    /// </summary>
    /// <exception cref="InvalidOperationException">The link is not up.</exception>
    public void Send(string text) => _manager.Send(this, text);

    /// <summary>Asks the other station to disconnect, and waits briefly for it to agree.</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _manager.DisconnectAsync(this, cancellationToken);

    /// <summary>Reconnects a disconnected session to the same station.</summary>
    public Task ReconnectAsync(CancellationToken cancellationToken = default) =>
        _manager.ReconnectAsync(this, cancellationToken);

    internal void SetState(LinkState state)
    {
        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

    internal void Emit(SessionLine line) => Line?.Invoke(this, line);
}
