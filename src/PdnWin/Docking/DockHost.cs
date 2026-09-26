using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PdnWin.Docking;

/// <summary>A pane the dock host can place.</summary>
/// <param name="Id">Stable ID, stored in the saved layout.</param>
/// <param name="Title">The tab text.</param>
/// <param name="Content">What the pane shows. Kept alive and re-parented as the layout changes.</param>
/// <param name="Tools">Optional controls shown at the right of the tab strip while the pane is selected.</param>
public sealed record DockPane(string Id, string Title, FrameworkElement Content, FrameworkElement? Tools = null);

/// <summary>
/// A small docking host: panes in tabbed groups, groups in resizable rows and columns, panes
/// dragged by their tab onto any group's centre (as a tab) or edge (as a new group), closed from
/// the tab and brought back from the View menu. The layout round-trips through JSON.
/// </summary>
/// <remarks>
/// Written for this app rather than taken from AvalonDock: AvalonDock is MS-PL, which the FSF
/// counts as incompatible with the GPL family this app is licensed under. It does less (no
/// floating windows yet) and is a few hundred lines.
/// </remarks>
public sealed class DockHost : Border
{
    internal const string DragFormat = "pdn-win/dock-pane";

    private readonly Dictionary<string, DockPane> _panes = [];
    private readonly Dictionary<string, ContentPresenter> _hosts = [];
    private DockLayout _layout = new(new DockGroup([]));

    /// <summary>Raised whenever the layout changes: a move, a close, a resize.</summary>
    public event Action? LayoutChanged;

    /// <summary>The current layout.</summary>
    public DockLayout Layout => _layout;

    /// <summary>Every registered pane.</summary>
    public IReadOnlyCollection<DockPane> Panes => _panes.Values;

    /// <summary>Makes a pane available for placing.</summary>
    public void Register(DockPane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        _panes[pane.Id] = pane;
    }

    /// <summary>Shows <paramref name="layout"/>, dropping any pane in it that is not registered.</summary>
    public void Load(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (string unknown in layout.PlacedPanes.Where(p => !_panes.ContainsKey(p)).ToList())
        {
            layout.Close(unknown);
        }

        _layout = layout;
        Rebuild();
    }

    /// <summary>Whether a pane is placed.</summary>
    public bool IsShown(string id) => _layout.GroupOf(id) is not null;

    /// <summary>Places a pane (beside <paramref name="near"/> if given) and selects it.</summary>
    public void Show(string id, string? near = null)
    {
        _layout.Show(id, near);
        Changed();
    }

    /// <summary>Takes a pane out of the layout.</summary>
    public void Close(string id)
    {
        _layout.Close(id);
        Changed();
    }

    /// <summary>Selects a placed pane's tab.</summary>
    public void Select(string id)
    {
        if (_layout.GroupOf(id) is { } group && group.Selected != id)
        {
            group.Selected = id;
            Changed();
        }
    }

    internal void Move(string id, DockGroup target, DockZone zone)
    {
        _layout.Move(id, target, zone);
        Changed();
    }

    private void Changed()
    {
        Rebuild();
        LayoutChanged?.Invoke();
    }

    private void Rebuild()
    {
        // Every pane's content is re-parented, so first take it out of wherever it was.
        foreach (ContentPresenter host in _hosts.Values)
        {
            host.Content = null;
        }

        _hosts.Clear();
        Child = Build(_layout.Root);
    }

    private UIElement Build(DockNode node) => node switch
    {
        DockGroup group => new GroupView(this, group, group.Panes.Where(_panes.ContainsKey).Select(p => _panes[p]).ToList(), _hosts),
        DockSplit split => BuildSplit(split),
        _ => throw new InvalidOperationException(),
    };

    private Grid BuildSplit(DockSplit split)
    {
        var grid = new Grid();
        bool horizontal = split.Orientation == Orientation.Horizontal;
        var cells = new List<UIElement>();
        for (int i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                var gap = new GridLength(5);
                if (horizontal)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = gap });
                }
                else
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = gap });
                }

                var splitter = new GridSplitter
                {
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                };
                splitter.DragCompleted += (_, _) => CaptureWeights(split, cells);
                Place(grid, splitter, horizontal, (i * 2) - 1);
            }

            var size = new GridLength(split.Weights[i], GridUnitType.Star);
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size, MinWidth = 60 });
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = size, MinHeight = 44 });
            }

            UIElement cell = Build(split.Children[i]);
            cells.Add(cell);
            Place(grid, cell, horizontal, i * 2);
        }

        return grid;
    }

    private void CaptureWeights(DockSplit split, List<UIElement> cells)
    {
        for (int i = 0; i < cells.Count && i < split.Weights.Count; i++)
        {
            var element = (FrameworkElement)cells[i];
            double size = split.Orientation == Orientation.Horizontal ? element.ActualWidth : element.ActualHeight;
            split.Weights[i] = Math.Max(1, size);
        }

        LayoutChanged?.Invoke();
    }

    private static void Place(Grid grid, UIElement element, bool horizontal, int index)
    {
        if (horizontal)
        {
            Grid.SetColumn(element, index);
        }
        else
        {
            Grid.SetRow(element, index);
        }

        grid.Children.Add(element);
    }

    /// <summary>One group: its tab strip, the selected pane, and the drop targets.</summary>
    private sealed class GroupView : Border
    {
        private readonly DockHost _host;
        private readonly DockGroup _group;
        private readonly Rectangle _dropHint;
        private readonly Canvas _overlay;
        private DockZone? _zone;

        public GroupView(DockHost host, DockGroup group, IReadOnlyList<DockPane> panes, Dictionary<string, ContentPresenter> hosts)
        {
            _host = host;
            _group = group;
            Background = (Brush)Application.Current.FindResource("Panel");
            BorderBrush = (Brush)Application.Current.FindResource("Edge");
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(7);
            ClipToBounds = true;
            AllowDrop = true;
            SnapsToDevicePixels = true;

            DockPane? selected = panes.FirstOrDefault(p => p.Id == group.Selected) ?? panes.FirstOrDefault();
            group.Selected = selected?.Id;

            var strip = new DockPanel { LastChildFill = false, Height = 30 };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
            foreach (DockPane pane in panes)
            {
                tabs.Children.Add(new TabHeader(host, group, pane, ReferenceEquals(pane, selected)));
            }

            DockPanel.SetDock(tabs, Dock.Left);
            strip.Children.Add(tabs);
            if (selected?.Tools is { } tools)
            {
                if (tools.Parent is Panel old)
                {
                    old.Children.Remove(tools);
                }

                DockPanel.SetDock(tools, Dock.Right);
                tools.Margin = new Thickness(0, 0, 6, 0);
                tools.VerticalAlignment = VerticalAlignment.Center;
                strip.Children.Add(tools);
            }

            var header = new Border
            {
                Child = strip,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = (Brush)Application.Current.FindResource("Panel"),
            };

            var body = new ContentPresenter();
            if (selected is not null)
            {
                body.Content = selected.Content;
                hosts[selected.Id] = body;
            }

            var layout = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);
            layout.Children.Add(new Border { Child = body, Background = (Brush)Application.Current.FindResource("Panel2") });

            _dropHint = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x38, 0xF5, 0xA8, 0x3C)),
                Stroke = (Brush)Application.Current.FindResource("Amber"),
                StrokeThickness = 1.5,
                RadiusX = 5,
                RadiusY = 5,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _overlay = new Canvas { IsHitTestVisible = false };
            _overlay.Children.Add(_dropHint);

            var root = new Grid();
            root.Children.Add(layout);
            root.Children.Add(_overlay);
            Child = root;

            DragOver += OnDragOver;
            DragLeave += (_, _) => Hint(null);
            Drop += OnDrop;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DragFormat))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            Point p = e.GetPosition(this);
            double w = ActualWidth, h = ActualHeight;
            double dx = p.X / w, dy = p.Y / h;
            DockZone zone = (dx, dy) switch
            {
                _ when dx is > 0.3 and < 0.7 && dy is > 0.3 and < 0.7 => DockZone.Center,
                _ when Math.Min(dx, 1 - dx) < Math.Min(dy, 1 - dy) => dx < 0.5 ? DockZone.Left : DockZone.Right,
                _ => dy < 0.5 ? DockZone.Top : DockZone.Bottom,
            };

            Hint(zone);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            DockZone? zone = _zone;
            Hint(null);
            if (zone is { } z && e.Data.GetData(DragFormat) is string id)
            {
                // After the drag-drop loop unwinds: rebuilding the tree mid-drop tears down the
                // element the drop is being delivered to.
                Dispatcher.BeginInvoke(() => _host.Move(id, _group, z));
            }
        }

        private void Hint(DockZone? zone)
        {
            _zone = zone;
            if (zone is null)
            {
                _dropHint.Visibility = Visibility.Collapsed;
                return;
            }

            double w = ActualWidth, h = ActualHeight;
            Rect r = zone switch
            {
                DockZone.Left => new Rect(4, 4, w / 2 - 6, h - 8),
                DockZone.Right => new Rect(w / 2 + 2, 4, w / 2 - 6, h - 8),
                DockZone.Top => new Rect(4, 4, w - 8, h / 2 - 6),
                DockZone.Bottom => new Rect(4, h / 2 + 2, w - 8, h / 2 - 6),
                _ => new Rect(4, 34, w - 8, h - 38),
            };
            Canvas.SetLeft(_dropHint, r.X);
            Canvas.SetTop(_dropHint, r.Y);
            _dropHint.Width = Math.Max(0, r.Width);
            _dropHint.Height = Math.Max(0, r.Height);
            _dropHint.Visibility = Visibility.Visible;
        }
    }

    /// <summary>A tab: click to select, drag to move, x to close.</summary>
    private sealed class TabHeader : Border
    {
        private readonly DockHost _host;
        private readonly DockPane _pane;
        private Point? _pressedAt;

        public TabHeader(DockHost host, DockGroup group, DockPane pane, bool selected)
        {
            _host = host;
            _pane = pane;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            Padding = new Thickness(9, 0, 4, 0);
            ToolTip = "Drag to move; drop on another pane's edge to split it";

            var title = new TextBlock
            {
                Text = pane.Title.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.FindResource(selected ? "Ink" : "Muted"),
            };
            TextOptions.SetTextFormattingMode(title, TextFormattingMode.Display);
            title.SetValue(TextBlock.LineHeightProperty, 12.0);
            title.Margin = new Thickness(0, 1, 0, 0);
            title.Inlines.Clear();
            title.Text = Spaced(pane.Title.ToUpperInvariant());

            var close = new Button
            {
                Content = new Path
                {
                    Data = Geometry.Parse("M 0,0 L 6,6 M 6,0 L 0,6"),
                    Stroke = (Brush)Application.Current.FindResource("Muted"),
                    StrokeThickness = 1.3,
                },
                Style = (Style)Application.Current.FindResource("GhostButton"),
                Padding = new Thickness(5),
                Margin = new Thickness(4, 0, 0, 0),
                Opacity = 0,
                ToolTip = "Close (bring it back from the View menu)",
                VerticalAlignment = VerticalAlignment.Center,
            };
            close.Click += (_, _) => _host.Close(_pane.Id);

            var underline = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = selected ? (Brush)Application.Current.FindResource("Amber") : Brushes.Transparent,
                Margin = new Thickness(0, 0, 18, 0),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(title);
            row.Children.Add(close);
            var grid = new Grid();
            grid.Children.Add(row);
            grid.Children.Add(underline);
            Child = grid;

            MouseEnter += (_, _) => close.Opacity = 1;
            MouseLeave += (_, _) => close.Opacity = 0;
            PreviewMouseLeftButtonDown += (_, e) => _pressedAt = e.GetPosition(this);
            PreviewMouseLeftButtonUp += (_, _) =>
            {
                if (_pressedAt is not null && group.Selected != pane.Id)
                {
                    _host.Select(pane.Id);
                }

                _pressedAt = null;
            };
            MouseMove += OnMouseMove;
        }

        private static string Spaced(string text) => string.Join(' ', text.ToCharArray());

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector moved = e.GetPosition(this) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2 &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2)
            {
                return;
            }

            _pressedAt = null;
            DragDrop.DoDragDrop(this, new DataObject(DragFormat, _pane.Id), DragDropEffects.Move);
        }
    }
}
