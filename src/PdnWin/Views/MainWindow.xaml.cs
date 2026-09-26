using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdnWin.Controls;
using PdnWin.Core.Settings;
using PdnWin.Docking;
using PdnWin.ViewModels;

namespace PdnWin.Views;

/// <summary>The main window: title bar, control strip, the docked panes and a status line.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private readonly InputPane _input = new();
    private bool _closing;

    /// <summary>Creates the window over <paramref name="model"/>.</summary>
    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Views/Panes.xaml", UriKind.Relative) });

        Register("spectrum", "Spectrum", "SpectrumPane");
        Register("waterfall", "Waterfall", "WaterfallPane");
        Register("heard", "Heard", "HeardPane");
        Register("monitor", "Monitor", "MonitorPane", "MonitorTools");
        Register("sessions", "Sessions", "SessionsPane", "SessionsTools");
        Dock.Register(new DockPane("input", "Input", _input));
        Dock.Register(new DockPane("levels", "Levels", new LevelHelperPane()));

        Dock.Load((model.Settings.Ui.Layout is { } saved ? DockLayout.FromJson(saved) : null) ?? DefaultLayout());
        Dock.LayoutChanged += SaveUi;
        RestorePlacement(model.Settings.Ui);

        model.PropertyChanged += OnModelChanged;
        model.SettingsRequested += OpenSettings;
        model.LevelHelperRequested += () => Dock.Show("levels", near: "monitor");
        model.Sessions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionsViewModel.Selected))
            {
                _input.FocusLine();
            }
        };

        StateChanged += (_, _) =>
        {
            // A maximised chromeless window overhangs the screen by its resize border.
            Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxGlyph.Data = Geometry.Parse(WindowState == WindowState.Maximized
                ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z"
                : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        };

        Loaded += async (_, _) =>
        {
            UpdateStatus();
            if (!model.Settings.IsComplete && !model.Simulate)
            {
                OpenSettings();
                Dock.Show("levels", near: "monitor");
            }
            else
            {
                await model.StartAsync();
            }
        };
        Closing += OnClosing;
    }

    private static DockLayout DefaultLayout()
    {
        var band = new DockSplit(Orientation.Vertical)
            .Add(new DockGroup(["spectrum"]), 0.6)
            .Add(new DockGroup(["waterfall"]), 1);
        var top = new DockSplit(Orientation.Horizontal)
            .Add(band, 3.2)
            .Add(new DockGroup(["heard"]), 1);
        var talk = new DockSplit(Orientation.Vertical)
            .Add(new DockGroup(["sessions"]), 1)
            .Add(new DockGroup(["input"]), 0.22);
        var bottom = new DockSplit(Orientation.Horizontal)
            .Add(new DockGroup(["monitor"]), 1)
            .Add(talk, 1.2);
        return new DockLayout(new DockSplit(Orientation.Vertical).Add(top, 1.15).Add(bottom, 1.4));
    }

    private void Register(string id, string title, string template, string? tools = null)
    {
        var content = new ContentControl { Content = _model, ContentTemplate = (DataTemplate)Resources[template], Focusable = false };
        ContentControl? toolbar = tools is null
            ? null
            : new ContentControl { Content = _model, ContentTemplate = (DataTemplate)Resources[tools], Focusable = false };
        Dock.Register(new DockPane(id, title, content, toolbar));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.Transmitting) or nameof(MainViewModel.MyCall))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        CallBadge.Visibility = string.IsNullOrEmpty(_model.MyCall) ? Visibility.Collapsed : Visibility.Visible;
        (Color color, string word) = _model.Status switch
        {
            StationStatus.Live when _model.Transmitting => (Color.FromRgb(0xFF, 0x5A, 0x42), "transmitting"),
            StationStatus.Live => (Color.FromRgb(0x7B, 0xD8, 0x8F), "live"),
            StationStatus.Starting => (Color.FromRgb(0xF5, 0xA8, 0x3C), "starting"),
            StationStatus.Fault => (Color.FromRgb(0xF2, 0x7E, 0x7E), "fault"),
            _ => (Color.FromRgb(0x7C, 0x88, 0x94), "offline"),
        };
        StatusLamp.Color = color;
        StateWord.Text = word;
        Pill.Background = new SolidColorBrush(_model.Status == StationStatus.Fault
            ? Color.FromArgb(0x29, 0xFF, 0x78, 0x5A)
            : Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_model.Settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } settings)
        {
            _ = _model.ApplySettingsAsync(settings);
        }
    }

    private void OnViewMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ViewButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (DockPane pane in Dock.Panes.OrderBy(p => p.Title))
        {
            var item = new MenuItem { Header = pane.Title, IsCheckable = true, IsChecked = Dock.IsShown(pane.Id) };
            item.Click += (_, _) =>
            {
                if (Dock.IsShown(pane.Id))
                {
                    Dock.Close(pane.Id);
                }
                else
                {
                    Dock.Show(pane.Id);
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator { Margin = new Thickness(4), Background = (Brush)FindResource("Edge") });
        var reset = new MenuItem { Header = "Reset layout" };
        reset.Click += (_, _) =>
        {
            Dock.Load(DefaultLayout());
            SaveUi();
        };
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    private void SaveUi()
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _model.SaveUi(_model.Settings.Ui with
        {
            Layout = Dock.Layout.ToJson(),
            WindowBounds = [bounds.Left, bounds.Top, bounds.Width, bounds.Height],
            Maximized = WindowState == WindowState.Maximized,
        });
    }

    private void RestorePlacement(UiSettings ui)
    {
        if (ui.WindowBounds is [double left, double top, double width, double height] && width > 200 && height > 200)
        {
            var bounds = new Rect(left, top, width, height);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (screen.IntersectsWith(bounds))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (ui.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        // Release the radio (PTT first) before the process goes.
        e.Cancel = true;
        _closing = true;
        SaveUi();
        await _model.DisposeAsync();
        Close();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
