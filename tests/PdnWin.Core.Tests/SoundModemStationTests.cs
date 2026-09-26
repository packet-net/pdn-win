using Packet.Core;
using PdnWin.Core.Sessions;
using PdnWin.Core.Stations;
using PdnWin.Core.Stations.SoundModem;
using PdnWin.Core.Stations.Simulation;
using PdnWin.Core.Tests.Fakes;

namespace PdnWin.Core.Tests;

/// <summary>
/// Two complete stations, soundmodem and all, joined by a pair of real-time audio wires: the
/// in-process path end to end, the way the app uses it, with nothing but the radio missing.
/// </summary>
public sealed class SoundModemStationTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AudioWire _oneToTwo = new(48000);
    private readonly AudioWire _twoToOne = new(48000);
    private readonly WiredSoundCard _cardOne;
    private readonly WiredSoundCard _cardTwo;
    private readonly SoundModemStation _one;
    private readonly SoundModemStation _two;

    public SoundModemStationTests()
    {
        _cardOne = new WiredSoundCard("one", fromRadio: _twoToOne, toRadio: _oneToTwo);
        _cardTwo = new WiredSoundCard("two", fromRadio: _oneToTwo, toRadio: _twoToOne);
        var options = new SoundModemStationOptions
        {
            Mode = FmModes.Default,
            ChannelAccess = new ChannelAccess(TxDelayMs: 150, Persistence: 255, SlotTimeMs: 50, TxTailMs: 30),
        };
        _one = new SoundModemStation(_cardOne, options);
        _two = new SoundModemStation(_cardTwo, options);
    }

    public async ValueTask DisposeAsync()
    {
        await _one.DisposeAsync();
        await _two.DisposeAsync();
        _oneToTwo.Dispose();
        _twoToOne.Dispose();
    }

    [Fact]
    public async Task A_ui_frame_sent_by_one_station_is_heard_by_the_other_with_its_quality()
    {
        var heard = new TaskCompletionSource<HeardFrame>();
        _two.FrameHeard += frame =>
        {
            if (frame.Direction == FrameDirection.Received)
            {
                heard.TrySetResult(frame);
            }
        };
        await using var sessions = new SessionManager(_one.Transport, new SessionOptions { MyCall = Callsign.Parse("M0LTE-1") });

        await sessions.SendUiAsync(Callsign.Parse("BEACON"), "hello over audio", cancellationToken: TestContext.Current.CancellationToken);

        HeardFrame frame = await heard.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        frame.Quality!.Mode.Should().Be("afsk1200");
        System.Text.Encoding.ASCII.GetString(frame.Bytes).Should().Contain("hello over audio");
        _cardOne.Counting.Keyups.Should().BeGreaterThan(0, "station one keyed its PTT to send it");
    }

    [Fact]
    public async Task Two_stations_connect_over_the_audio_and_exchange_text()
    {
        await using var a = new SessionManager(_one.Transport, new SessionOptions { MyCall = Callsign.Parse("M0LTE-1") });
        await using var b = new SessionManager(_two.Transport, new SessionOptions { MyCall = Callsign.Parse("M0LTE-2") });
        await a.StartAsync(TestContext.Current.CancellationToken);
        await b.StartAsync(TestContext.Current.CancellationToken);
        var incoming = new TaskCompletionSource<PacketSession>();
        b.SessionOpened += s => incoming.TrySetResult(s);

        PacketSession outgoing = await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        outgoing.State.Should().Be(LinkState.Connected);
        PacketSession theirs = await incoming.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        var line = new TaskCompletionSource<string>();
        theirs.Line += (_, l) =>
        {
            if (l.Kind == SessionLineKind.Received)
            {
                line.TrySetResult(l.Text);
            }
        };
        outgoing.Send("73 de M0LTE-1");
        (await line.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().Be("73 de M0LTE-1");
    }

    [Fact]
    public async Task Changing_mode_keeps_the_transport_and_the_new_mode_carries_frames()
    {
        await _one.SetModeAsync("afsk1200-il2p", TestContext.Current.CancellationToken);
        await _two.SetModeAsync("afsk1200-il2p", TestContext.Current.CancellationToken);
        var heard = new TaskCompletionSource<HeardFrame>();
        _two.FrameHeard += frame =>
        {
            if (frame.Direction == FrameDirection.Received)
            {
                heard.TrySetResult(frame);
            }
        };
        await using var sessions = new SessionManager(_one.Transport, new SessionOptions { MyCall = Callsign.Parse("M0LTE-1") });

        await sessions.SendUiAsync(Callsign.Parse("BEACON"), "now in il2p", cancellationToken: TestContext.Current.CancellationToken);

        HeardFrame frame = await heard.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        frame.Quality!.Mode.Should().StartWith("afsk1200-il2p");
    }

    [Fact]
    public async Task The_input_is_metered_and_the_band_is_drawn()
    {
        var level = new TaskCompletionSource<LevelReading>();
        var line = new TaskCompletionSource<SpectrumLine>();
        _one.InputLevel += l => level.TrySetResult(l);
        _one.SpectrumLine += l => line.TrySetResult(l with { Bins = l.Bins.ToArray() });

        LevelReading reading = await level.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        SpectrumLine spectrum = await line.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        reading.PeakDbFs.Should().BeLessThan(-40, "the wire carries only faint noise when nobody transmits");
        spectrum.Bins.Length.Should().BeGreaterThan(500);
        spectrum.BinWidthHz.Should().BeApproximately(5.86, 0.1);
    }
}
