using Microsoft.Extensions.Time.Testing;
using Packet.Core;
using PdnWin.Core.Beacons;

namespace PdnWin.Core.Tests;

public class BeaconSchedulerTests
{
    private static BeaconSettings Every(TimeSpan interval, bool enabled = true) =>
        new(enabled, interval, Callsign.Parse("BEACON"), [], "M0LTE test");

    [Fact]
    public void Nothing_goes_out_when_enabled_only_after_the_first_interval()
    {
        var time = new FakeTimeProvider();
        int sent = 0;
        using var scheduler = new BeaconScheduler(_ => { sent++; return Task.CompletedTask; }, time);

        scheduler.Configure(Every(TimeSpan.FromMinutes(10)));
        sent.Should().Be(0);
        time.Advance(TimeSpan.FromMinutes(10));
        sent.Should().Be(1);
        time.Advance(TimeSpan.FromMinutes(10));
        sent.Should().Be(2);
    }

    [Fact]
    public void An_interval_below_the_minimum_is_raised_to_it()
    {
        var time = new FakeTimeProvider();
        int sent = 0;
        using var scheduler = new BeaconScheduler(_ => { sent++; return Task.CompletedTask; }, time);

        scheduler.Configure(Every(TimeSpan.FromSeconds(10)));
        time.Advance(BeaconScheduler.MinimumInterval - TimeSpan.FromSeconds(1));
        sent.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(1));
        sent.Should().Be(1);
    }

    [Fact]
    public void Disabling_stops_it()
    {
        var time = new FakeTimeProvider();
        int sent = 0;
        using var scheduler = new BeaconScheduler(_ => { sent++; return Task.CompletedTask; }, time);
        scheduler.Configure(Every(TimeSpan.FromMinutes(10)));
        scheduler.Configure(Every(TimeSpan.FromMinutes(10), enabled: false));

        time.Advance(TimeSpan.FromHours(1));
        sent.Should().Be(0);
    }
}
