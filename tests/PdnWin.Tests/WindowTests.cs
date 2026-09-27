using Avalonia.Controls;
using Avalonia.VisualTree;
using PdnWin.App;
using PdnWin.App.Controls;
using PdnWin.App.Views;
using PdnWin.Core;
using PdnWin.Core.Settings;
using PdnWin.Hosting;
using PdnWin.ViewModels;

namespace PdnWin.Tests;

[Collection("ui")]
public sealed class WindowTests(Ui ui) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pdnwin-ui-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public Task The_main_window_on_the_simulator_names_itself_and_shows_every_pane() => ui.Run(async () =>
    {
        var model = new MainViewModel(Store(new AppSettings()), new AvaloniaUiThread(), hardware: null) { Simulate = true };
        var window = new MainWindow(model);
        window.Show();
        Ui.Settle();

        try
        {
            window.Title.Should().Be(AppIdentity.Name);
            window.GetVisualDescendants().OfType<SpectrumView>().Should().ContainSingle();
            window.GetVisualDescendants().OfType<WaterfallView>().Should().ContainSingle();
            window.GetVisualDescendants().OfType<LogList>().Should().NotBeEmpty("the monitor and the console transcript");
            window.GetVisualDescendants().OfType<TextBox>().Should().Contain(t => t.Name == "Line", "the input line");
        }
        finally
        {
            await model.DisposeAsync();
            window.Close();
        }
    });

    [Fact]
    public Task A_saved_window_size_comes_back() => ui.Run(async () =>
    {
        var settings = new AppSettings { Ui = new UiSettings { WindowBounds = [40, 30, 1234, 777] } };
        var model = new MainViewModel(Store(settings), new AvaloniaUiThread(), hardware: null);
        var window = new MainWindow(model);

        try
        {
            window.Width.Should().Be(1234);
            window.Height.Should().Be(777);
        }
        finally
        {
            await model.DisposeAsync();
            window.Close();
        }
    });

    [Fact]
    public Task The_settings_show_the_ptt_fields_for_the_kind_chosen() => ui.Run(() =>
    {
        var model = new SettingsViewModel(new AppSettings { Interface = new InterfaceSettings { Ptt = PttKind.Cm108Hid } }, null);
        var window = new SettingsWindow(model);
        window.Show();
        Ui.Settle();

        try
        {
            Visible(window, "GpioBox").Should().BeTrue("HID keys on a GPIO pin");
            SerialRowVisible(window).Should().BeFalse();

            model.Ptt = PttKind.Serial;
            Ui.Settle();
            Visible(window, "GpioBox").Should().BeFalse();
            SerialRowVisible(window).Should().BeTrue("serial keys on the port's lines");

            model.Ptt = PttKind.None;
            Ui.Settle();
            Visible(window, "GpioBox").Should().BeFalse();
            SerialRowVisible(window).Should().BeFalse();
        }
        finally
        {
            window.Close();
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task The_settings_say_what_saving_will_do_to_the_station() => ui.Run(() =>
    {
        var stored = new AppSettings { MyCall = "M0LTE", Interface = new InterfaceSettings { ContainerId = "aioc", CaptureEndpointId = "in", RenderEndpointId = "out" } };
        var idle = new SettingsViewModel(stored, null);
        var running = new SettingsViewModel(stored, null) { StationRunning = true };
        var window = new SettingsWindow(running);
        window.Show();
        Ui.Settle();

        try
        {
            idle.SaveEffect.Should().Be("Saving starts the station.");
            SaveLine(window).Should().Be("Saving applies these to the running station.");

            running.BeaconText = "{MYCALL} on the air";
            running.TxDelayMs = 500;
            running.Mode = "qpsk3600";
            Ui.Settle();
            SaveLine(window).Should().Be("Saving applies these to the running station.");

            running.MyCall = "M0LTE-1";
            Ui.Settle();
            SaveLine(window).Should().Be("Saving restarts the station, which drops any connected sessions.");
        }
        finally
        {
            window.Close();
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task Saving_settings_changes_a_running_station_and_restarts_it_only_for_a_new_callsign() => ui.Run(async () =>
    {
        var model = new MainViewModel(Store(new AppSettings()), new AvaloniaUiThread(), hardware: null) { Simulate = true };
        await model.StartAsync();

        try
        {
            model.Status.Should().Be(StationStatus.Live);
            await model.ApplySettingsAsync(model.Settings with
            {
                Beacon = model.Settings.Beacon with { Text = "changed" },
                Sessions = model.Settings.Sessions with { WelcomeText = "Hello" },
            });
            Ui.Settle();
            OnTheAir(model).Should().Be(1, "a beacon or welcome text change is taken as the station runs");

            await model.ApplySettingsAsync(model.Settings with { MyCall = "M0LTE-5" });
            Ui.Settle();
            OnTheAir(model).Should().Be(2, "the station is opened with its callsign");
            model.Status.Should().Be(StationStatus.Live);
        }
        finally
        {
            await model.DisposeAsync();
        }
    });

    [Fact]
    public Task A_start_asked_for_while_one_is_under_way_happens_after_it_rather_than_being_lost() => ui.Run(async () =>
    {
        var stored = new AppSettings { MyCall = "M0LTE", Interface = new InterfaceSettings { CaptureEndpointId = "in", RenderEndpointId = "out" } };
        var hardware = new SlowHardware();
        var model = new MainViewModel(Store(stored), new AvaloniaUiThread(), hardware);

        try
        {
            Task first = model.StartAsync();
            hardware.Opens.Should().Be(1, "the first start is waiting for the interface");

            await model.StartAsync();
            hardware.Opens.Should().Be(1, "the second waits for the first");

            hardware.Gate.SetResult();
            await first;
            hardware.Opens.Should().Be(2, "the first start read the settings before the second was asked for");
        }
        finally
        {
            await model.DisposeAsync();
        }
    });

    [Fact]
    public Task The_tone_test_says_how_long_it_sends_for() => ui.Run(() =>
    {
        var pane = new LevelHelperPane();
        var window = new Window { Content = pane };
        window.Show();
        Ui.Settle();

        try
        {
            window.GetVisualDescendants().OfType<TextBox>()
                .Should().Contain(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Tone seconds");
        }
        finally
        {
            window.Close();
        }

        return Task.CompletedTask;
    });

    private static bool Visible(Window window, string automationId) =>
        window.GetVisualDescendants().OfType<Control>()
            .Single(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == automationId)
            .IsEffectivelyVisible;

    private static string? SaveLine(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "SaveEffect").Text;

    private static int OnTheAir(MainViewModel model) =>
        model.Sessions.Console.Lines.Count(l => l.Text.StartsWith("*** station on the air", StringComparison.Ordinal));

    private static bool SerialRowVisible(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Serial port")
            .IsEffectivelyVisible;

    // An interface that takes as long to open as the test says, and then is not there.
    private sealed class SlowHardware : IStationHardware
    {
        public event Action? DevicesChanged { add { } remove { } }

        public TaskCompletionSource Gate { get; } = new();

        public int Opens { get; private set; }

        public Task<DiscoveryResult> DiscoverAsync(InterfaceSettings current, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiscoveryResult([], null, string.Empty, []));

        public InterfaceSettings Suggest(InterfaceOption option, InterfaceSettings previous) => previous;

        public async Task<OpenedInterface?> OpenAsync(
            InterfaceSettings settings, Action<string> note, Action<Exception> faulted, CancellationToken cancellationToken = default)
        {
            Opens++;
            await Gate.Task;
            return null;
        }
    }

    private SettingsStore Store(AppSettings settings)
    {
        var store = new SettingsStore(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"));
        store.Save(settings);
        return store;
    }
}
