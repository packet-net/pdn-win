using System.Text;
using Packet.Ax25;
using Packet.Core;
using PdnWin.Core.Sessions;
using PdnWin.Core.Stations.SoundModem;

namespace PdnWin.Core.Stations.Simulation;

/// <summary>
/// A channel with somebody on it, for developing and demonstrating without a radio: a second
/// soundmodem station, <see cref="NodeCall"/>, on the other end of a real-time audio link with
/// open-squelch-like hiss. It accepts connects (a welcome, then echoes what it is sent), beacons,
/// and sends UI frames from a few made-up stations so the monitor, heard list and waterfall have
/// something to show. Nothing here touches a real device.
/// </summary>
public sealed class SimulatedChannel : IAsyncDisposable
{
    /// <summary>The simulated node's callsign.</summary>
    public static readonly Callsign NodeCall = Callsign.Parse("GB7SIM");

    private static readonly string[] Chatter =
    [
        "M0ABC-7|APRS|!5130.00N/00005.00W-Mobile on the M4",
        "G4XYZ|BEACON|G4XYZ packet node, Reading. Connect for BBS",
        "2E0DEF-9|CQ|CQ CQ de 2E0DEF anyone about?",
        "GB7XX-1|NODES|ÿGB7XX  BBS",
    ];

    private readonly AudioWire _toUs;
    private readonly AudioWire _toThem;
    private readonly SoundModemStation _node;
    private readonly SessionManager _sessions;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _chatter;

    private SimulatedChannel(string mode)
    {
        // Hiss at about -30 dBFS RMS, as an open-squelch receiver set with a little headroom.
        _toUs = new AudioWire(48000, noiseRms: 0.03f, gain: 0.2f);
        _toThem = new AudioWire(48000, noiseRms: 0.03f, gain: 0.2f);
        Card = new WiredSoundCard("Simulator", fromRadio: _toUs, toRadio: _toThem);
        var nodeCard = new WiredSoundCard("GB7SIM", fromRadio: _toThem, toRadio: _toUs);
        _node = new SoundModemStation(nodeCard, new SoundModemStationOptions
        {
            Mode = mode,
            ChannelAccess = new ChannelAccess(TxDelayMs: 250, Persistence: 127, SlotTimeMs: 60, TxTailMs: 30),
        });
        _sessions = new SessionManager(_node.Transport, new SessionOptions
        {
            MyCall = NodeCall,
            WelcomeText = $"Welcome to GB7SIM, the {AppIdentity.Name} simulator.\nAnything you type is echoed back. B or BYE disconnects.",
        });
        _sessions.SessionOpened += Echo;
        _chatter = Task.Run(() => ChatterAsync(_stop.Token));
    }

    /// <summary>Our end of the link.</summary>
    public WiredSoundCard Card { get; }

    /// <summary>Starts a simulated channel in <paramref name="mode"/>.</summary>
    public static async Task<SimulatedChannel> StartAsync(string mode)
    {
        var channel = new SimulatedChannel(mode);
        await channel._sessions.StartAsync().ConfigureAwait(false);
        return channel;
    }

    /// <summary>Follows our station's mode.</summary>
    public Task SetModeAsync(string mode) => _node.SetModeAsync(mode);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _chatter.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _sessions.DisposeAsync().ConfigureAwait(false);
        await _node.DisposeAsync().ConfigureAwait(false);
        _toUs.Dispose();
        _toThem.Dispose();
        _stop.Dispose();
    }

    private void Echo(PacketSession session)
    {
        session.Line += (s, line) =>
        {
            if (line.Kind != SessionLineKind.Received || line.Continues)
            {
                return;
            }

            if (line.Text.Trim().ToUpperInvariant() is "B" or "BYE")
            {
                s.Send("73, bye.");
                _ = s.DisconnectAsync();
                return;
            }

            try
            {
                s.Send($"GB7SIM heard: {line.Text}");
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    private async Task ChatterAsync(CancellationToken cancellation)
    {
        var random = new Random();
        await Task.Delay(TimeSpan.FromSeconds(4), cancellation).ConfigureAwait(false);
        int next = 0;
        while (!cancellation.IsCancellationRequested)
        {
            string[] parts = Chatter[next++ % Chatter.Length].Split('|');
            Ax25Frame frame = Ax25Frame.Ui(
                Callsign.Parse(parts[1]), Callsign.Parse(parts[0]), Encoding.Latin1.GetBytes(parts[2] + "\r"),
                parts[1] == "NODES" ? (byte)0xCF : Ax25Frame.PidNoLayer3);
            await _node.Transport.SendAsync(frame.ToBytes(), cancellation).ConfigureAwait(false);
            if (next % 3 == 0)
            {
                await _sessions.SendUiAsync(Callsign.Parse("ID"), "GB7SIM simulated node, connect for an echo",
                    cancellationToken: cancellation).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(9 + random.Next(8)), cancellation).ConfigureAwait(false);
        }
    }
}
