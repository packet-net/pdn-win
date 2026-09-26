using PdnWin.Core.Levels;
using PdnWin.Core.Stations;

namespace PdnWin.Core.Tests;

public class RxLevelAdvisorTests
{
    private static RxLevelAdvice Feed(RxLevelAdvisor advisor, LevelReading reading, int times)
    {
        RxLevelAdvice advice = null!;
        for (int i = 0; i < times; i++)
        {
            advice = advisor.Add(reading);
        }

        return advice;
    }

    [Fact]
    public void Open_squelch_noise_peaking_in_the_band_is_good_and_settles_after_three_seconds()
    {
        var advisor = new RxLevelAdvisor();
        RxLevelAdvice early = Feed(advisor, new LevelReading(-12, -22, false), 5);
        early.Verdict.Should().Be(RxLevelVerdict.Good);
        early.Settled.Should().BeFalse();

        Feed(advisor, new LevelReading(-12, -22, false), 12).Settled.Should().BeTrue();
    }

    [Fact]
    public void Any_clipping_in_the_window_says_turn_it_down()
    {
        var advisor = new RxLevelAdvisor();
        Feed(advisor, new LevelReading(-12, -22, false), 5);
        advisor.Add(new LevelReading(0, -8, true)).Verdict.Should().Be(RxLevelVerdict.Clipping);
    }

    [Fact]
    public void The_handhelds_volume_turned_right_down_reads_as_no_audio()
    {
        var advisor = new RxLevelAdvisor();
        Feed(advisor, new LevelReading(-60, -70, false), 5).Verdict.Should().Be(RxLevelVerdict.NoAudio);
    }

    [Fact]
    public void Quiet_and_hot_are_told_apart_from_good()
    {
        Feed(new RxLevelAdvisor(), new LevelReading(-25, -35, false), 5).Verdict.Should().Be(RxLevelVerdict.TooQuiet);
        Feed(new RxLevelAdvisor(), new LevelReading(-4, -14, false), 5).Verdict.Should().Be(RxLevelVerdict.TooLoud);
    }

    [Fact]
    public void Audio_that_keeps_dropping_away_is_a_squelch_closing()
    {
        var advisor = new RxLevelAdvisor();
        RxLevelAdvice advice = null!;
        for (int i = 0; i < 10; i++)
        {
            advice = advisor.Add(i % 3 == 0 ? new LevelReading(-12, -20, false) : new LevelReading(-70, -80, false));
        }

        advice.Verdict.Should().Be(RxLevelVerdict.SquelchClosing);
    }
}
