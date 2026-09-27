using Packet.Core;
using PdnWin.Core.Sessions;
using PdnWin.Core.Tests.Fakes;

namespace PdnWin.Core.Tests;

public class SessionManagerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static async Task<(SessionManager A, SessionManager B)> PairAsync(string? welcome = null)
    {
        (FramePipe a, FramePipe b) = FramePipe.Create();
        var ma = new SessionManager(a, new SessionOptions { MyCall = Callsign.Parse("M0LTE-1") });
        var mb = new SessionManager(b, new SessionOptions { MyCall = Callsign.Parse("M0LTE-2"), WelcomeText = welcome });
        await ma.StartAsync(TestContext.Current.CancellationToken);
        await mb.StartAsync(TestContext.Current.CancellationToken);
        return (ma, mb);
    }

    [Fact]
    public async Task A_connect_opens_a_session_at_both_ends_and_text_flows_both_ways()
    {
        (SessionManager a, SessionManager b) = await PairAsync();
        await using SessionManager _ = a;
        await using SessionManager __ = b;
        var incoming = new TaskCompletionSource<PacketSession>();
        b.SessionOpened += s => incoming.TrySetResult(s);

        PacketSession outgoing = await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        outgoing.State.Should().Be(LinkState.Connected);
        PacketSession theirs = await incoming.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        theirs.Incoming.Should().BeTrue();
        theirs.Remote.Should().Be(Callsign.Parse("M0LTE-1"));

        var heard = new TaskCompletionSource<string>();
        theirs.Line += (_, line) =>
        {
            if (line.Kind == SessionLineKind.Received)
            {
                heard.TrySetResult(line.Text);
            }
        };
        outgoing.Send("hello from one");
        (await heard.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().Be("hello from one");

        var back = new TaskCompletionSource<string>();
        outgoing.Line += (_, line) =>
        {
            if (line.Kind == SessionLineKind.Received)
            {
                back.TrySetResult(line.Text);
            }
        };
        theirs.Send("and back");
        (await back.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().Be("and back");
    }

    [Fact]
    public async Task A_disconnect_takes_both_ends_down_and_the_session_can_reconnect()
    {
        (SessionManager a, SessionManager b) = await PairAsync();
        await using SessionManager _ = a;
        await using SessionManager __ = b;
        var incoming = new TaskCompletionSource<PacketSession>();
        b.SessionOpened += s => incoming.TrySetResult(s);
        PacketSession outgoing = await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        PacketSession theirs = await incoming.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var theirsDown = new TaskCompletionSource();
        theirs.StateChanged += (_, state) =>
        {
            if (state == LinkState.Disconnected)
            {
                theirsDown.TrySetResult();
            }
        };

        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);
        outgoing.State.Should().Be(LinkState.Disconnected);
        await theirsDown.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        await outgoing.ReconnectAsync(TestContext.Current.CancellationToken);
        outgoing.State.Should().Be(LinkState.Connected);
        a.Sessions.Should().ContainSingle("a reconnect reuses the session, and so its tab");
    }

    [Fact]
    public async Task A_station_that_connects_to_us_is_sent_the_welcome_text()
    {
        (SessionManager a, SessionManager b) = await PairAsync(welcome: "Welcome to M0LTE-2");
        await using SessionManager _ = a;
        await using SessionManager __ = b;
        var welcome = new TaskCompletionSource<string>();
        a.SessionOpened += s => s.Line += (_, line) =>
        {
            if (line.Kind == SessionLineKind.Received)
            {
                welcome.TrySetResult(line.Text);
            }
        };

        await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        (await welcome.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().Be("Welcome to M0LTE-2");
    }

    [Fact]
    public async Task A_welcome_text_changed_while_running_is_what_the_next_caller_gets()
    {
        (SessionManager a, SessionManager b) = await PairAsync(welcome: "Welcome to M0LTE-2");
        await using SessionManager _ = a;
        await using SessionManager __ = b;
        b.WelcomeText = "Changed while on the air";
        var welcome = new TaskCompletionSource<string>();
        a.SessionOpened += s => s.Line += (_, line) =>
        {
            if (line.Kind == SessionLineKind.Received)
            {
                welcome.TrySetResult(line.Text);
            }
        };

        await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        (await welcome.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().Be("Changed while on the air");
    }

    [Fact]
    public async Task A_paclen_of_nothing_is_refused_rather_than_sending_forever()
    {
        (SessionManager a, SessionManager b) = await PairAsync();
        await using SessionManager _ = a;
        await using SessionManager __ = b;

        FluentActions.Invoking(() => a.Paclen = 0).Should().Throw<ArgumentOutOfRangeException>();
        a.Paclen.Should().Be(128);
    }

    [Fact]
    public async Task A_line_longer_than_paclen_is_split_across_frames_and_reassembled()
    {
        (SessionManager a, SessionManager b) = await PairAsync();
        await using SessionManager _ = a;
        await using SessionManager __ = b;
        var incoming = new TaskCompletionSource<PacketSession>();
        b.SessionOpened += s => incoming.TrySetResult(s);
        PacketSession outgoing = await a.ConnectAsync(Callsign.Parse("M0LTE-2"), TestContext.Current.CancellationToken);
        PacketSession theirs = await incoming.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        var text = new System.Text.StringBuilder();
        var complete = new TaskCompletionSource();
        string longLine = new('x', 300);
        theirs.Line += (_, line) =>
        {
            if (line.Kind != SessionLineKind.Received)
            {
                return;
            }

            text.Append(line.Text);
            if (text.Length >= longLine.Length)
            {
                complete.TrySetResult();
            }
        };

        outgoing.Send(longLine);
        await complete.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        text.ToString().Should().Be(longLine);
    }

    [Fact]
    public async Task A_connect_nobody_answers_ends_disconnected_with_a_notice_rather_than_throwing()
    {
        (FramePipe a, _) = FramePipe.Create();
        await using var manager = new SessionManager(a, new SessionOptions
        {
            MyCall = Callsign.Parse("M0LTE-1"),
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
        });
        await manager.StartAsync(TestContext.Current.CancellationToken);
        var notices = new List<string>();
        manager.SessionOpened += s => s.Line += (_, line) => notices.Add(line.Text);

        PacketSession session = await manager.ConnectAsync(Callsign.Parse("GB7XXX"), TestContext.Current.CancellationToken);

        session.State.Should().Be(LinkState.Disconnected);
        notices.Should().Contain(n => n.Contains("no answer", StringComparison.Ordinal));
    }
}
