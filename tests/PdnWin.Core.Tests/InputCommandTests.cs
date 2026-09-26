using Packet.Core;
using PdnWin.Core.Sessions;

namespace PdnWin.Core.Tests;

public class InputCommandTests
{
    [Theory]
    [InlineData("C GB7RDG")]
    [InlineData("c gb7rdg")]
    [InlineData("connect GB7RDG")]
    public void C_and_connect_parse_to_a_connect(string line)
    {
        InputCommand.Parse(line).Should().Be(new InputCommand.Connect(Callsign.Parse("GB7RDG")));
    }

    [Fact]
    public void An_ssid_is_kept()
    {
        InputCommand.Parse("C M0LTE-9").Should().Be(new InputCommand.Connect(Callsign.Parse("M0LTE-9")));
    }

    [Fact]
    public void A_via_path_is_refused_with_a_reason_rather_than_silently_dropped()
    {
        InputCommand.Parse("C GB7RDG V GB7XX").Should().BeOfType<InputCommand.Invalid>();
    }

    [Fact]
    public void Unproto_takes_a_destination_a_path_and_the_rest_as_text()
    {
        InputCommand.Unproto command = InputCommand.Parse("U CQ,GB7RDG hello all").Should().BeOfType<InputCommand.Unproto>().Subject;
        command.Destination.Should().Be(Callsign.Parse("CQ"));
        command.Via.Should().Equal(Callsign.Parse("GB7RDG"));
        command.Text.Should().Be("hello all");
    }

    [Fact]
    public void Anything_else_is_explained()
    {
        InputCommand.Parse("hello").Should().BeOfType<InputCommand.Invalid>()
            .Which.Message.Should().Contain("C CALLSIGN");
    }
}
