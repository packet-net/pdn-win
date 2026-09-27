using PdnWin.Core.Stations;
using PdnWin.Core.Stations.SoundModem;
using PdnWin.Core.Tests.Fakes;

namespace PdnWin.Core.Tests;

/// <summary>
/// Carrier sense on the receiver every packet FM station has: squelch open, so louder idle than
/// keyed. The modes run at 12 kHz, where only the in-band energy detector works, and it reads this
/// channel backwards: clear through an over and busy for about ten seconds after, which on air
/// held our replies 5 to 18 s. The station reads it from the card's 48 kHz audio instead.
/// </summary>
public class CarrierSenseTests
{
    private const double IdleSeconds = 4;
    private const double KeyedSeconds = 3;

    [Fact]
    public async Task On_an_open_squelch_receiver_the_channel_is_busy_for_an_over_and_clear_soon_after_it()
    {
        var receiver = new OpenSquelchReceiver(IdleSeconds, KeyedSeconds);
        await using var station = new SoundModemStation(receiver, new SoundModemStationOptions { Mode = FmModes.Default });
        var seen = new List<(double At, bool Busy)>();
        double unkey = IdleSeconds + KeyedSeconds;
        while (receiver.Seconds < unkey + 4)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            seen.Add((receiver.Seconds, station.ChannelBusy));
        }

        // After the detector's two seconds learning the idle channel; each span allows a little
        // for the station to catch up with what it has read.
        Busy(seen, 2.5, IdleSeconds).Should().Be(0, "nobody is transmitting");
        Busy(seen, IdleSeconds + 0.5, unkey).Should().BeGreaterThan(0.9, "the far end is keyed");
        Busy(seen, unkey + 1, unkey + 4).Should().Be(0, "the far end unkeyed a second ago");
    }

    private static double Busy(List<(double At, bool Busy)> seen, double from, double to)
    {
        var span = seen.Where(s => s.At >= from && s.At < to).ToList();
        span.Should().NotBeEmpty();
        return span.Count(s => s.Busy) / (double)span.Count;
    }
}
