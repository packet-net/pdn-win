using PdnWin.Core.Settings;
using PdnWin.Core.Stations;

namespace PdnWin.Core.Tests;

public class AppSettingsTests
{
    private static readonly AppSettings OnAir = new()
    {
        MyCall = "M0LTE",
        Interface = new InterfaceSettings
        {
            Name = "AIOC Audio",
            ContainerId = "b59cde31",
            CaptureEndpointId = "capture",
            RenderEndpointId = "render",
            HidPath = "/dev/hidraw0",
        },
    };

    [Fact]
    public void A_new_callsign_or_another_interface_needs_the_station_restarting()
    {
        OnAir.NeedsRestartFor(OnAir with { MyCall = "M0LTE-1" }).Should().BeTrue();
        OnAir.NeedsRestartFor(OnAir with { Interface = OnAir.Interface with { ContainerId = "other", CaptureEndpointId = "c2" } }).Should().BeTrue();
        OnAir.NeedsRestartFor(OnAir with { Interface = OnAir.Interface with { Ptt = PttKind.Serial, SerialPort = "COM8" } }).Should().BeTrue();
    }

    [Fact]
    public void The_mode_channel_access_sessions_beacon_and_levels_change_on_a_running_station()
    {
        AppSettings next = OnAir with
        {
            MyCall = "m0lte",
            Interface = OnAir.Interface with { Name = "AIOC", CaptureLevelDb = -6, RenderLevelDb = -3 },
            Modem = OnAir.Modem with { Mode = "qpsk3600", ChannelAccess = new ChannelAccess(TxDelayMs: 500, TxTailMs: 50) },
            Sessions = OnAir.Sessions with { AcceptIncoming = false, WelcomeText = "Hello", Paclen = 64, RecentCalls = ["GB7RDG"] },
            Beacon = new BeaconSettingsModel { Enabled = true, Text = "hello" },
            Ui = OnAir.Ui with { SpanHz = 6000 },
        };

        OnAir.NeedsRestartFor(next).Should().BeFalse();
    }

    [Fact]
    public void Persistence_and_slot_time_are_always_the_channel_defaults_whatever_the_file_says()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            { "MyCall": "M0LTE", "Modem": { "Mode": "afsk1200",
              "ChannelAccess": { "TxDelayMs": 450, "Persistence": 255, "SlotTimeMs": 10, "TxTailMs": 40 } } }
            """);
        try
        {
            AppSettings loaded = new SettingsStore(path).Load();

            loaded.Modem.ChannelAccess.Should().Be(new ChannelAccess(TxDelayMs: 450, Persistence: 63, SlotTimeMs: 100, TxTailMs: 40));
            (loaded.Modem with { ChannelAccess = new ChannelAccess(Persistence: 200, SlotTimeMs: 20) }).ChannelAccess
                .Should().Be(new ChannelAccess());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
