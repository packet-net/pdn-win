using System.Windows;
using System.Windows.Threading;
using PdnWin.Core.Settings;
using PdnWin.ViewModels;
using PdnWin.Views;

namespace PdnWin;

/// <summary>The application: one main window over one station.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);
        Snapshots.StartIfRequested(Dispatcher);
        bool simulate = e.Args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
        // PDNWIN_SETTINGS points a development run at a settings file of its own.
        string? settingsPath = Environment.GetEnvironmentVariable("PDNWIN_SETTINGS");
        SettingsStore store = string.IsNullOrWhiteSpace(settingsPath) ? SettingsStore.Default : new SettingsStore(settingsPath);
        var model = new MainViewModel(store, Dispatcher) { Simulate = simulate };
        var window = new MainWindow(model);
        MainWindow = window;
        window.Show();
    }

    /// <summary>Appends an exception to %LOCALAPPDATA%\pdn-win\errors.log.</summary>
    internal static void Log(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pdn-win");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch (System.IO.IOException)
        {
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the station up rather than lose the operator's sessions to one bad handler; say what
        // happened where the operator will see it.
        Log(e.Exception);
        MessageBox.Show(e.Exception.Message, "pdn-win", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
