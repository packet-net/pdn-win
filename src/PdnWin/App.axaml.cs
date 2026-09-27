using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using PdnWin.Core;
using PdnWin.Core.Settings;
using PdnWin.Hardware;
using PdnWin.Hosting;
using PdnWin.ViewModels;

namespace PdnWin.App;

/// <summary>The application: one main window over one station.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
            string[] args = desktop.Args ?? [];
            bool simulate = args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
            string? settingsPath = Environment.GetEnvironmentVariable("PDNWIN_SETTINGS");
            SettingsStore store = string.IsNullOrWhiteSpace(settingsPath) ? SettingsStore.Default : new SettingsStore(settingsPath);

            // The hardware layer for the machine: WASAPI and HID on Windows, ALSA and hidraw on
            // Linux. Anywhere else (macOS builds, but has no layer yet) only the simulator runs.
            IStationHardware? hardware = OperatingSystem.IsWindows() ? new WindowsStationHardware()
                : OperatingSystem.IsLinux() ? new LinuxStationHardware()
                : null;
            var model = new MainViewModel(store, new AvaloniaUiThread(), hardware) { Simulate = simulate, DemoScript = args.Contains("--demo", StringComparer.OrdinalIgnoreCase) ? MainViewModel.DefaultDemo : null };
            desktop.MainWindow = new Views.MainWindow(model);
            Snapshots.StartIfRequested();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Appends an exception to errors.log in the app's local data folder
    /// (<c>%LOCALAPPDATA%\pdn-win</c>, <c>~/.local/share/pdn-lin</c>).</summary>
    internal static void Log(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Name);
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the station up rather than lose the operator's sessions to one bad handler; say what
        // happened where the operator will see it.
        Log(e.Exception);
        e.Handled = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } owner })
        {
            _ = Warning(e.Exception.Message).ShowDialog(owner);
        }
    }

    private static Window Warning(string message)
    {
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 4), IsDefault = true, IsCancel = true };
        var window = new Window
        {
            Title = AppIdentity.Name,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Something went wrong, and the station carried on.", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => window.Close();
        return window;
    }
}
