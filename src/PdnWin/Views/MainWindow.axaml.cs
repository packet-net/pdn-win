using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PdnWin.App.Controls;
using PdnWin.App.Docking;
using PdnWin.Core.Settings;
using PdnWin.Docking;
using PdnWin.ViewModels;

namespace PdnWin.App.Views;

/// <summary>The main window: title bar, control strip, docked panes, status line.</summary>
public partial class MainWindow : Window
{
    private static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);

    private readonly MainViewModel _model;
    private readonly DockHost _dock;
    private readonly InputPane _input = new();
    private bool _closing;

    // The window's bounds when it was last neither maximised nor minimised: what is saved, since a
    // maximised window's own position and size are the screen's. Position in pixels, size in DIPs.
    private PixelPoint _normalPosition;
    private Size _normalSize;

    /// <summary>For the XAML loader; the app uses the other constructor.</summary>
    public MainWindow()
        : this(null!)
    {
    }

    /// <summary>Creates the window over <paramref name="model"/>.</summary>
    public MainWindow(MainViewModel model)
    {
        _model = model;
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
        _dock = this.FindControl<DockHost>("Dock")!;
        if (model is null)
        {
            return;
        }

        SetUpChrome();

        _dock.Register(new DockPane("spectrum", "Spectrum", Bound(new SpectrumView { Margin = new Thickness(0, 4, 0, 0) },
            (SpectrumView.FeedProperty, "Band"), (SpectrumView.PassbandProperty, "Passband"), (SpectrumView.SpanHzProperty, "Settings.Ui.SpanHz"))));
        _dock.Register(new DockPane("waterfall", "Waterfall", Bound(new WaterfallView(),
            (WaterfallView.FeedProperty, "Band"), (WaterfallView.SpanHzProperty, "Settings.Ui.SpanHz"),
            (WaterfallView.FloorDbProperty, "Settings.Ui.WaterfallFloorDb"), (WaterfallView.TopDbProperty, "Settings.Ui.WaterfallTopDb"))));
        _dock.Register(new DockPane("heard", "Heard", WithEmptyHint(Bound(new ListBox(), (ItemsControl.ItemsSourceProperty, "Heard.Stations")),
            "Heard.Stations.Count", "Stations appear here as they are heard, most recent first.")));
        _dock.Register(new DockPane("monitor", "Monitor", WithEmptyHint(Bound(new LogList { Padding = new Thickness(0, 4) }, (ItemsControl.ItemsSourceProperty, "Monitor.Items")),
            "Monitor.Items.Count", "Every frame heard or sent appears here, BPQ style."), MonitorTools()));
        _dock.Register(new DockPane("sessions", "Sessions", new SessionsPane(), SessionsTools()));
        _dock.Register(new DockPane("input", "Input", _input));
        _dock.Register(new DockPane("levels", "Levels", new LevelHelperPane()));

        _dock.Load((model.Settings.Ui.Layout is { } saved ? DockLayout.FromJson(saved) : null) ?? DefaultLayout());
        _dock.LayoutChanged += SaveUi;
        RestorePlacement(model.Settings.Ui);
        PositionChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();

        model.PropertyChanged += OnModelChanged;
        model.SettingsRequested += OpenSettings;
        model.LevelHelperRequested += () => _dock.Show("levels", near: "monitor");
        model.Sessions.PropertyChanged += (_, e) =>
        {
            // A session chosen or opened is a session to type into.
            if (e.PropertyName == nameof(SessionsViewModel.Selected))
            {
                _input.FocusLine();
            }
        };
        this.FindControl<Button>("ViewButton")!.Click += (s, _) => ShowViewMenu((Control)s!);

        Opened += async (_, _) =>
        {
            UpdateStatus();
            if (!model.Settings.IsComplete && !model.Simulate)
            {
                OpenSettings();
                _dock.Show("levels", near: "monitor");
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
        var band = new DockSplit(DockOrientation.Vertical)
            .Add(new DockGroup(["spectrum"]), 0.6)
            .Add(new DockGroup(["waterfall"]), 1);
        var top = new DockSplit(DockOrientation.Horizontal).Add(band, 3.2).Add(new DockGroup(["heard"]), 1);
        var talk = new DockSplit(DockOrientation.Vertical).Add(new DockGroup(["sessions"]), 1).Add(new DockGroup(["input"]), 0.22);
        var bottom = new DockSplit(DockOrientation.Horizontal).Add(new DockGroup(["monitor"]), 1).Add(talk, 1.2);
        return new DockLayout(new DockSplit(DockOrientation.Vertical).Add(top, 1.15).Add(bottom, 1.4));
    }

    private static T Bound<T>(T control, params (AvaloniaProperty Property, string Path)[] bindings) where T : Control
    {
        foreach ((AvaloniaProperty property, string path) in bindings)
        {
            control.Bind(property, new Binding(path));
        }

        return control;
    }

    private static Grid WithEmptyHint(Control content, string countPath, string text)
    {
        var hint = new TextBlock { Text = text, Classes = { "empty" } };
        hint.Bind(IsVisibleProperty, new Binding(countPath) { Converter = IsZero });
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(hint);
        return grid;
    }

    private static StackPanel MonitorTools()
    {
        var supervisory = Bound(new ToggleButton { Content = "RR/RNR", Margin = new Thickness(0, 0, 4, 0) }, (ToggleButton.IsCheckedProperty, "Monitor.ShowSupervisory"));
        var pause = Bound(new ToggleButton { Content = "Pause", Margin = new Thickness(0, 0, 4, 0) }, (ToggleButton.IsCheckedProperty, "Monitor.Paused"));
        var clear = Bound(new Button { Content = "Clear", Classes = { "ghost" }, FontSize = 11 }, (Button.CommandProperty, "Monitor.ClearCommand"));
        ToolTip.SetTip(supervisory, "Show supervisory frames (acknowledgements)");
        ToolTip.SetTip(pause, "Stop adding frames");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { supervisory, pause, clear } };
    }

    private static StackPanel SessionsTools()
    {
        // The callsign box offers the stations connected to recently, drops them down when it is
        // clicked into, and connects on Enter.
        var call = Bound(
            new AutoCompleteBox
            {
                Width = 130,
                MinHeight = 24,
                FontFamily = Palette.Mono,
                PlaceholderText = "callsign",
                MinimumPrefixLength = 0,
                FilterMode = AutoCompleteFilterMode.StartsWith,
            },
            (AutoCompleteBox.TextProperty, "Sessions.ConnectTo"),
            (AutoCompleteBox.ItemsSourceProperty, "Sessions.RecentCalls"));
        ToolTip.SetTip(call, "Callsign to connect to");
        AutomationProperties.SetName(call, "Connect to");
        call.AddHandler(TextInputEvent, (_, e) => e.Text = e.Text?.ToUpperInvariant(), RoutingStrategies.Tunnel);
        call.GotFocus += (_, _) => call.IsDropDownOpen = call.ItemsSource is System.Collections.ICollection { Count: > 0 } && string.IsNullOrEmpty(call.Text);
        call.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !call.IsDropDownOpen && call.DataContext is MainViewModel model && model.Sessions.ConnectCommand.CanExecute(null))
            {
                model.Sessions.ConnectCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        var connect = Bound(new Button { Content = "Connect", Classes = { "accent" }, Padding = new Thickness(10, 2), FontSize = 11, Margin = new Thickness(5, 0, 0, 0) },
            (Button.CommandProperty, "Sessions.ConnectCommand"));
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { call, connect } };
    }

    private void SetUpChrome()
    {
        var captions = this.FindControl<StackPanel>("CaptionButtons")!;
        var titleBar = this.FindControl<Grid>("TitleBar")!;
        if (OperatingSystem.IsWindows())
        {
            // Our own title bar, with the window's buttons drawn by us: the
            // system keeps only the resize border. (Extending the client area instead would
            // bring Avalonia's own drawn title bar, over ours.)
            WindowDecorations = WindowDecorations.BorderOnly;
            this.FindControl<Button>("MinButton")!.Click += (_, _) => WindowState = WindowState.Minimized;
            this.FindControl<Button>("MaxButton")!.Click += (_, _) =>
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
            titleBar.PointerPressed += (_, e) =>
            {
                PointerPointProperties button = e.GetCurrentPoint(this).Properties;
                if (button.IsRightButtonPressed)
                {
                    WindowMenu().ShowAt(titleBar, showAtPointer: true);
                    return;
                }

                if (!button.IsLeftButtonPressed)
                {
                    return;
                }

                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
                else
                {
                    BeginMoveDrag(e);
                }
            };

            // The maximise button says what it will do: restore, when maximised.
            var glyph = this.FindControl<Avalonia.Controls.Shapes.Path>("MaxGlyph")!;
            var maxButton = this.FindControl<Button>("MaxButton")!;
            this.GetObservable(WindowStateProperty).Subscribe(new Observer<WindowState>(state =>
            {
                bool maximised = state == WindowState.Maximized;
                glyph.Data = Geometry.Parse(maximised ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
                ToolTip.SetTip(maxButton, maximised ? "Restore" : "Maximise");
            }));
        }
        else
        {
            // Elsewhere the desktop's own decorations: a drawn title bar is fragile under the
            // variety of Linux window managers and Wayland compositors.
            captions.IsVisible = false;
        }
    }

    /// <summary>What right-clicking a title bar offers: our title bar is drawn by us, so this is too.</summary>
    private MenuFlyout WindowMenu()
    {
        MenuItem Item(string header, bool enabled, Action act)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => act();
            return item;
        }

        bool maximised = WindowState == WindowState.Maximized;
        return new MenuFlyout
        {
            Items =
            {
                Item("Restore", maximised, () => WindowState = WindowState.Normal),
                Item("Minimise", true, () => WindowState = WindowState.Minimized),
                Item("Maximise", !maximised, () => WindowState = WindowState.Maximized),
                new Separator(),
                Item("Close", true, Close),
            },
        };
    }

    /// <summary>An observer from a callback, for GetObservable without Rx.</summary>
    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => next(value);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.Transmitting))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        (Color color, string word) = _model.Status switch
        {
            StationStatus.Live when _model.Transmitting => (Color.Parse("#FF5A42"), "transmitting"),
            StationStatus.Live => (Color.Parse("#7BD88F"), "live"),
            StationStatus.Starting => (Color.Parse("#F5A83C"), "starting"),
            StationStatus.Fault => (Color.Parse("#F27E7E"), "fault"),
            _ => (Color.Parse("#7C8894"), "offline"),
        };
        this.FindControl<Lamp>("StatusLamp")!.Color = color;
        this.FindControl<TextBlock>("StateWord")!.Text = word;
        this.FindControl<Border>("Pill")!.Background = new SolidColorBrush(_model.Status == StationStatus.Fault
            ? Color.Parse("#29FF785A")
            : Color.Parse("#0FFFFFFF"));
    }

    private async void OpenSettings()
    {
        var dialog = new SettingsWindow(_model.CreateSettings());
        if (await dialog.ShowDialog<bool>(this) && dialog.Result is { } settings)
        {
            await _model.ApplySettingsAsync(settings);
        }
    }

    private void ShowViewMenu(Control anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (DockPane pane in _dock.Panes.OrderBy(p => p.Title))
        {
            var item = new MenuItem
            {
                Header = pane.Title,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _dock.IsShown(pane.Id),
            };
            item.Click += (_, _) =>
            {
                if (_dock.IsShown(pane.Id))
                {
                    _dock.Close(pane.Id);
                }
                else
                {
                    _dock.Show(pane.Id);
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "Reset layout" };
        reset.Click += (_, _) =>
        {
            _dock.Load(DefaultLayout());
            SaveUi();
        };
        menu.Items.Add(reset);
        menu.ShowAt(anchor);
    }

    private void SaveUi() => _model.SaveUi(_model.Settings.Ui with
    {
        Layout = _dock.Layout.ToJson(),
        WindowBounds = _normalSize.Width > 0 ? [_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height] : _model.Settings.Ui.WindowBounds,
        Maximized = WindowState == WindowState.Maximized,
    });

    private void RememberNormalBounds()
    {
        if (WindowState == WindowState.Normal && IsVisible)
        {
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }
    }

    /// <summary>
    /// Puts the window where it was, if that is still on a screen (a monitor unplugged since), and
    /// maximises it if it was.
    /// </summary>
    private void RestorePlacement(UiSettings ui)
    {
        if (ui.WindowBounds is [double left, double top, double width, double height] && width > 200 && height > 200)
        {
            var position = new PixelPoint((int)left, (int)top);
            var visible = new PixelRect(position, new PixelSize((int)Math.Min(width, 400), (int)Math.Min(height, 200)));
            if (Screens.All.Any(screen => screen.WorkingArea.Intersects(visible)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = position;
                Width = width;
                Height = height;
                _normalPosition = position;
                _normalSize = new Size(width, height);
            }
        }

        if (ui.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
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
}
