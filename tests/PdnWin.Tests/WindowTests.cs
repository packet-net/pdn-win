using Avalonia.Controls;
using Avalonia.VisualTree;
using PdnWin.App;
using PdnWin.App.Controls;
using PdnWin.App.Views;
using PdnWin.Core;
using PdnWin.Core.Settings;
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

    private static bool SerialRowVisible(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Serial port")
            .IsEffectivelyVisible;

    private SettingsStore Store(AppSettings settings)
    {
        var store = new SettingsStore(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"));
        store.Save(settings);
        return store;
    }
}
