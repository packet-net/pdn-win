using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PdnWin.Core.Settings;
using PdnWin.Hardware;
using PdnWin.Hosting;
using PdnWin.ViewModels;

namespace PdnWin.Ava;

/// <summary>The Avalonia application: one main window over one station, as the WPF one.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? [];
            bool simulate = args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
            string? settingsPath = Environment.GetEnvironmentVariable("PDNWIN_SETTINGS");
            SettingsStore store = string.IsNullOrWhiteSpace(settingsPath) ? SettingsStore.Default : new SettingsStore(settingsPath);

            // The Windows hardware layer on Windows; nothing yet elsewhere, where only the
            // simulator runs (the Linux layer is the next piece of the port).
            IStationHardware? hardware = OperatingSystem.IsWindows() ? new WindowsStationHardware() : null;
            var model = new MainViewModel(store, new AvaloniaUiThread(), hardware) { Simulate = simulate, DemoScript = args.Contains("--demo", StringComparer.OrdinalIgnoreCase) ? MainViewModel.DefaultDemo : null };
            desktop.MainWindow = new Views.MainWindow(model);
            Snapshots.StartIfRequested();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
