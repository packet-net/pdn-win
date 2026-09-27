using Packet.Ax25;
using Packet.Core;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Stations;

namespace PdnWin.Core.Tests;

public class MonitorFormatterTests
{
    [Fact]
    public void A_ui_frame_shows_its_path_and_its_text_as_lines()
    {
        Ax25Frame frame = Ax25Frame.Ui(
            Callsign.Parse("BEACON"), Callsign.Parse("M0LTE-1"), "one\rtwo\r"u8, digipeaters: [Callsign.Parse("GB7RDG")]);

        MonitorEntry entry = MonitorFormatter.Format(new HeardFrame(DateTimeOffset.UnixEpoch, FrameDirection.Received, frame.ToBytes()));

        entry.Kind.Should().Be(FrameKind.UnnumberedInformation);
        entry.Header.Should().StartWith("M0LTE-1>BEACON,GB7RDG <UI C");
        entry.Info.Should().Equal("one", "two");
    }

    [Fact]
    public void Quality_becomes_a_detail_line_and_a_level_verdict_becomes_a_badge()
    {
        Ax25Frame frame = Ax25Frame.Ui(Callsign.Parse("ID"), Callsign.Parse("GB7RDG"), "x"u8);
        var heard = new HeardFrame(DateTimeOffset.UnixEpoch, FrameDirection.Received, frame.ToBytes(),
            new HeardFrameQuality(Mode: "afsk1200", SnrDb: 17.46, FrequencyOffsetHz: -3, LevelVerdict: "TOO LOUD"));

        MonitorEntry entry = MonitorFormatter.Format(heard);

        entry.Detail.Should().Be("afsk1200, 17.5 dB, -3 Hz");
        entry.Badge.Should().Be("TOO LOUD");
    }

    [Fact]
    public void One_of_ours_that_waited_for_the_channel_says_how_long_and_why()
    {
        Ax25Frame frame = Ax25Frame.Ui(Callsign.Parse("GB7RDG"), Callsign.Parse("M0LTE"), "x"u8);
        var heard = new HeardFrame(DateTimeOffset.UnixEpoch, FrameDirection.Transmitted, frame.ToBytes(),
            Hold: new TransmitHold(TimeSpan.FromSeconds(5.24), "5.2s channel busy"));

        MonitorEntry entry = MonitorFormatter.Format(heard);

        entry.Badge.Should().Be("HELD 5.2s");
        entry.Detail.Should().Be("held: 5.2s channel busy");
    }

    [Fact]
    public void A_wait_with_no_one_cause_still_says_how_long()
    {
        Ax25Frame frame = Ax25Frame.Ui(Callsign.Parse("GB7RDG"), Callsign.Parse("M0LTE"), "x"u8);
        var heard = new HeardFrame(DateTimeOffset.UnixEpoch, FrameDirection.Transmitted, frame.ToBytes(),
            Hold: new TransmitHold(TimeSpan.FromSeconds(2), null));

        MonitorFormatter.Format(heard).Detail.Should().Be("held 2.0s, no one cause");
    }

    [Fact]
    public void Garbage_is_shown_as_undecodable_rather_than_dropped()
    {
        MonitorEntry entry = MonitorFormatter.Format(new HeardFrame(DateTimeOffset.UnixEpoch, FrameDirection.Received, [1, 2, 3]));
        entry.Kind.Should().Be(FrameKind.Undecodable);
        entry.Header.Should().Contain("3 bytes");
    }
}
