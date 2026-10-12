using Microsoft.Extensions.Time.Testing;
using PdnWin.Core.Updates;

namespace PdnWin.Core.Tests;

public class UpdateCheckTests
{
    private const string Index = """
        Package: pdn-soundmodem
        Version: 9.9.9
        Architecture: amd64

        Package: pdn-lin
        Version: 0.3.1
        Architecture: amd64

        Package: pdn-lin
        Version: 0.3.1
        Architecture: arm64

        Package: pdn-linmail
        Version: 6.0.25.41-pdn1
        Architecture: amd64
        """;

    private static (UpdateCheck Check, List<AvailableUpdate> Found) Check(string current, Func<CancellationToken, Task<string>> fetch, bool fromApt = false, TimeProvider? time = null)
    {
        var found = new List<AvailableUpdate>();
        var check = new UpdateCheck(current, time, fetch, () => fromApt);
        check.Found += found.Add;
        return (check, found);
    }

    [Fact]
    public void The_newest_pdn_lin_is_read_from_the_index_and_nothing_else()
    {
        UpdateCheck.Newest(Index).Should().Be(new Version(0, 3, 1));
        UpdateCheck.Newest(Index.Replace("\n", "\r\n", StringComparison.Ordinal)).Should().Be(new Version(0, 3, 1));
        UpdateCheck.Newest("Package: pdn-soundmodem\nVersion: 1.0.0\n").Should().BeNull();
        UpdateCheck.Newest("<html>not an index</html>").Should().BeNull();
    }

    [Fact]
    public async Task A_newer_release_is_found_with_its_page_and_no_apt_command_off_the_apt_repository()
    {
        (UpdateCheck check, List<AvailableUpdate> found) = Check("0.3.0", _ => Task.FromResult(Index));
        using (check)
        {
            await check.CheckOnceAsync(CancellationToken.None);
        }

        found.Should().Equal(new AvailableUpdate("0.3.0", "0.3.1", "https://github.com/packet-net/pdn-win/releases/tag/0.3.1", null));
    }

    [Fact]
    public async Task A_copy_from_the_apt_repository_is_given_the_apt_command()
    {
        (UpdateCheck check, List<AvailableUpdate> found) = Check("0.3.0", _ => Task.FromResult(Index), fromApt: true);
        using (check)
        {
            await check.CheckOnceAsync(CancellationToken.None);
        }

        found.Should().ContainSingle().Which.AptCommand.Should().Be("sudo apt update && sudo apt install --only-upgrade pdn-lin");
    }

    [Theory]
    [InlineData("0.3.1")]
    [InlineData("0.4.0")]
    [InlineData("0.0.0-dev")]
    public async Task The_same_an_older_or_a_dev_build_finds_nothing(string current)
    {
        (UpdateCheck check, List<AvailableUpdate> found) = Check(current, _ => Task.FromResult(Index));
        using (check)
        {
            await check.CheckOnceAsync(CancellationToken.None);
        }

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task Not_being_able_to_find_out_is_quiet()
    {
        (UpdateCheck check, List<AvailableUpdate> found) = Check("0.3.0", _ => throw new HttpRequestException("offline"));
        using (check)
        {
            await check.Awaiting(c => c.CheckOnceAsync(CancellationToken.None)).Should().NotThrowAsync();
        }

        found.Should().BeEmpty();
    }

    [Fact]
    public void It_looks_soon_after_start_then_daily()
    {
        var time = new FakeTimeProvider();
        int looks = 0;
        (UpdateCheck check, List<AvailableUpdate> found) = Check("0.3.0", _ => { looks++; return Task.FromResult(Index); }, time: time);
        using (check)
        {
            check.Start();
            looks.Should().Be(0);
            time.Advance(check.FirstDelay);
            looks.Should().Be(1);
            time.Advance(TimeSpan.FromHours(23));
            looks.Should().Be(1);
            time.Advance(TimeSpan.FromHours(1));
            looks.Should().Be(2);
        }

        found.Should().HaveCount(2, "every look that finds it says so");
        time.Advance(TimeSpan.FromDays(2));
        looks.Should().Be(2, "disposed, it stops");
    }

    [Fact]
    public void A_dev_build_never_looks()
    {
        var time = new FakeTimeProvider();
        int looks = 0;
        (UpdateCheck check, _) = Check("0.0.0-dev", _ => { looks++; return Task.FromResult(Index); }, time: time);
        using (check)
        {
            check.Start();
            time.Advance(TimeSpan.FromDays(3));
        }

        looks.Should().Be(0);
    }

    [Fact]
    public void The_apt_repository_is_recognised_in_either_sources_format_but_not_in_a_comment()
    {
        AptSources.NamesRepo(["deb [signed-by=/usr/share/keyrings/packet-net.gpg] https://packet-net.github.io/apt ./"]).Should().BeTrue();
        AptSources.NamesRepo(["Types: deb\nURIs: https://packet-net.github.io/apt\nSuites: ./\n"]).Should().BeTrue();
        AptSources.NamesRepo(["# deb https://packet-net.github.io/apt ./", "deb http://deb.debian.org/debian trixie main"]).Should().BeFalse();
    }

    [Fact]
    public void Apt_sources_are_read_from_the_file_and_the_directory()
    {
        string dir = Directory.CreateTempSubdirectory("pdnwin-apt-").FullName;
        try
        {
            string list = Path.Combine(dir, "sources.list");
            string d = Path.Combine(dir, "sources.list.d");
            Directory.CreateDirectory(d);
            File.WriteAllText(list, "deb http://deb.debian.org/debian trixie main\n");
            AptSources.ThisMachine(list, d).Should().BeFalse();
            File.WriteAllText(Path.Combine(d, "packet-net.sources"), "Types: deb\nURIs: https://packet-net.github.io/apt\n");
            AptSources.ThisMachine(list, d).Should().BeTrue();
            AptSources.ThisMachine(Path.Combine(dir, "missing"), Path.Combine(dir, "missing.d")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
