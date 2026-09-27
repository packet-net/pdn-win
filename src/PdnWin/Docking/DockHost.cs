using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PdnWin.Docking;

namespace PdnWin.App.Docking;

/// <summary>A pane the dock host can place.</summary>
/// <param name="Id">Stable ID, stored in the saved layout.</param>
/// <param name="Title">The tab text.</param>
/// <param name="Content">What the pane shows. Kept alive and re-parented as the layout changes.</param>
/// <param name="Tools">Optional controls shown at the right of the tab strip while the pane is selected.</param>
public sealed record DockPane(string Id, string Title, Control Content, Control? Tools = null);

/// <summary>
/// The dock host: tabbed groups in resizable rows and columns over the shared
/// <see cref="DockLayout"/>, panes dragged by their tab onto another group's centre or edge.
/// Dragging is done with pointer capture rather than the OS drag-and-drop, which keeps it the same
/// on every platform Avalonia runs on.
/// </summary>
public sealed class DockHost : Border
{
    private readonly Dictionary<string, DockPane> _panes = [];
    private readonly Dictionary<string, ContentControl> _hosts = [];
    private readonly List<GroupView> _groups = [];
    private DockLayout _layout = new(new DockGroup([]));
    private string? _dragging;
    private GroupView? _hover;
    private Action? _resetDraggedTab;

    /// <summary>Raised whenever the layout changes.</summary>
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

    /// <summary>Places a pane and selects it.</summary>
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

    internal void Select(string id)
    {
        if (_layout.GroupOf(id) is { } group && group.Selected != id)
        {
            group.Selected = id;
            Changed();
        }
    }

    /// <summary>A tab is being dragged; <paramref name="resetTab"/> puts it back if the drag is
    /// abandoned (Escape, or the pointer taken away).</summary>
    internal void BeginDrag(string id, Action resetTab)
    {
        _dragging = id;
        _resetDraggedTab = resetTab;
        TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, OnDragKey, RoutingStrategies.Tunnel);
    }

    /// <summary>Abandons a drag: nothing moves.</summary>
    internal void CancelDrag()
    {
        Action? reset = _resetDraggedTab;
        FinishDrag();
        reset?.Invoke();
    }

    internal void DragOver(PointerEventArgs e)
    {
        GroupView? over = null;
        foreach (GroupView group in _groups)
        {
            Point p = e.GetPosition(group);
            if (new Rect(group.Bounds.Size).Contains(p))
            {
                over = group;
                group.Hint(p);
            }
            else
            {
                group.Hint(null);
            }
        }

        _hover = over;
        Cursor = over is null ? new Cursor(StandardCursorType.No) : new Cursor(StandardCursorType.DragMove);
    }

    internal void EndDrag()
    {
        string? id = _dragging;
        GroupView? target = _hover;
        DockZone? zone = target?.Zone;
        FinishDrag();
        if (id is not null && target is not null && zone is { } z)
        {
            _layout.Move(id, target.Group, z);
            Changed();
        }
    }

    private void FinishDrag()
    {
        foreach (GroupView group in _groups)
        {
            group.Hint(null);
        }

        _dragging = null;
        _hover = null;
        _resetDraggedTab = null;
        Cursor = Cursor.Default;
        TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnDragKey);
    }

    private void OnDragKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragging is not null)
        {
            e.Handled = true;
            CancelDrag();
        }
    }

    private void Changed()
    {
        Rebuild();
        LayoutChanged?.Invoke();
    }

    private void Rebuild()
    {
        foreach (ContentControl host in _hosts.Values)
        {
            host.Content = null;
        }

        _hosts.Clear();
        _groups.Clear();
        Child = Build(_layout.Root);
    }

    private Control Build(DockNode node) => node switch
    {
        DockGroup group => BuildGroup(group),
        DockSplit split => BuildSplit(split),
        _ => throw new InvalidOperationException(),
    };

    private GroupView BuildGroup(DockGroup group)
    {
        var view = new GroupView(this, group, group.Panes.Where(_panes.ContainsKey).Select(p => _panes[p]).ToList(), _hosts);
        _groups.Add(view);
        return view;
    }

    private Grid BuildSplit(DockSplit split)
    {
        var grid = new Grid();
        bool horizontal = split.Orientation == DockOrientation.Horizontal;
        var cells = new List<Control>();
        for (int i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                if (horizontal)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(5)));
                }
                else
                {
                    grid.RowDefinitions.Add(new RowDefinition(new GridLength(5)));
                }

                // Its colour comes from the theme, which also lights it on hover.
                var splitter = new GridSplitter
                {
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                };
                splitter.DragCompleted += (_, _) => CaptureWeights(split, cells);
                Place(grid, splitter, horizontal, (i * 2) - 1);
            }

            var size = new GridLength(split.Weights[i], GridUnitType.Star);
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(size) { MinWidth = 60 });
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(size) { MinHeight = 44 });
            }

            Control cell = Build(split.Children[i]);
            cells.Add(cell);
            Place(grid, cell, horizontal, i * 2);
        }

        return grid;
    }

    private void CaptureWeights(DockSplit split, List<Control> cells)
    {
        for (int i = 0; i < cells.Count && i < split.Weights.Count; i++)
        {
            double size = split.Orientation == DockOrientation.Horizontal ? cells[i].Bounds.Width : cells[i].Bounds.Height;
            split.Weights[i] = Math.Max(1, size);
        }

        LayoutChanged?.Invoke();
    }

    private static void Place(Grid grid, Control element, bool horizontal, int index)
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

    /// <summary>One group: its tab strip, the selected pane, and the drop hint.</summary>
    private sealed class GroupView : Border
    {
        private static readonly IBrush HintFill = new ImmutableSolidColorBrush(Color.Parse("#38F5A83C"));
        private readonly Rectangle _hint;

        public GroupView(DockHost host, DockGroup group, IReadOnlyList<DockPane> panes, Dictionary<string, ContentControl> hosts)
        {
            Group = group;
            Background = Palette.Panel;
            BorderBrush = Palette.Edge;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(7);
            ClipToBounds = true;

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

            var header = new Border { Child = strip, BorderBrush = Palette.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Background = Palette.Panel };
            var body = new ContentControl();
            if (selected is not null)
            {
                body.Content = selected.Content;
                hosts[selected.Id] = body;
            }

            var layout = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);
            layout.Children.Add(new Border { Child = body, Background = Palette.Panel2 });

            _hint = new Rectangle
            {
                Fill = HintFill,
                Stroke = Palette.Amber,
                StrokeThickness = 1.5,
                RadiusX = 5,
                RadiusY = 5,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            var overlay = new Canvas { IsHitTestVisible = false };
            overlay.Children.Add(_hint);

            var root = new Grid();
            root.Children.Add(layout);
            root.Children.Add(overlay);
            Child = root;
        }

        public DockGroup Group { get; }

        public DockZone? Zone { get; private set; }

        public void Hint(Point? at)
        {
            if (at is not { } p)
            {
                Zone = null;
                _hint.IsVisible = false;
                return;
            }

            double w = Bounds.Width, h = Bounds.Height;
            double dx = p.X / w, dy = p.Y / h;
            DockZone zone = (dx, dy) switch
            {
                _ when dx is > 0.3 and < 0.7 && dy is > 0.3 and < 0.7 => DockZone.Center,
                _ when Math.Min(dx, 1 - dx) < Math.Min(dy, 1 - dy) => dx < 0.5 ? DockZone.Left : DockZone.Right,
                _ => dy < 0.5 ? DockZone.Top : DockZone.Bottom,
            };
            Zone = zone;
            Rect r = zone switch
            {
                DockZone.Left => new Rect(4, 4, w / 2 - 6, h - 8),
                DockZone.Right => new Rect(w / 2 + 2, 4, w / 2 - 6, h - 8),
                DockZone.Top => new Rect(4, 4, w - 8, h / 2 - 6),
                DockZone.Bottom => new Rect(4, h / 2 + 2, w - 8, h / 2 - 6),
                _ => new Rect(4, 34, w - 8, h - 38),
            };
            Canvas.SetLeft(_hint, r.X);
            Canvas.SetTop(_hint, r.Y);
            _hint.Width = Math.Max(0, r.Width);
            _hint.Height = Math.Max(0, r.Height);
            _hint.IsVisible = true;
        }
    }

    /// <summary>A tab: click to select, drag to move, x to close.</summary>
    private sealed class TabHeader : Border
    {
        private readonly DockHost _host;
        private readonly DockPane _pane;
        private Point? _pressedAt;
        private bool _dragging;
        private IPointer? _pointer;

        public TabHeader(DockHost host, DockGroup group, DockPane pane, bool selected)
        {
            _host = host;
            _pane = pane;
            Cursor = new Cursor(StandardCursorType.Hand);
            Background = Brushes.Transparent;
            Padding = new Thickness(9, 0, 4, 0);
            ToolTip.SetTip(this, "Drag to move; drop on another pane's edge to split it");

            var title = new TextBlock
            {
                Text = pane.Title.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 1.2,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = selected ? Palette.Ink : Palette.Muted,
                Margin = new Thickness(0, 1, 0, 0),
            };

            var close = new Button
            {
                Content = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M 0,0 L 6,6 M 6,0 L 0,6"),
                    Stroke = Palette.Muted,
                    StrokeThickness = 1.3,
                },
                Classes = { "ghost" },
                Padding = new Thickness(5),
                Margin = new Thickness(4, 0, 0, 0),
                Opacity = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Close (bring it back from the View menu)");
            close.Click += (_, _) => _host.Close(_pane.Id);

            var underline = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = selected ? Palette.Amber : Brushes.Transparent,
                Margin = new Thickness(0, 0, 18, 0),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(title);
            row.Children.Add(close);
            var grid = new Grid();
            grid.Children.Add(row);
            grid.Children.Add(underline);
            Child = grid;

            PointerEntered += (_, _) => close.Opacity = 1;
            PointerExited += (_, _) => close.Opacity = 0;
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _pressedAt = e.GetPosition(this);
                    _pointer = e.Pointer;
                    e.Pointer.Capture(this);
                }
            };
            PointerMoved += (_, e) =>
            {
                if (_pressedAt is not { } start)
                {
                    return;
                }

                if (!_dragging)
                {
                    Vector moved = e.GetPosition(this) - start;
                    if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
                    {
                        return;
                    }

                    _dragging = true;
                    _host.BeginDrag(_pane.Id, Reset);
                }

                _host.DragOver(e);
            };
            PointerReleased += (_, e) =>
            {
                // State first: releasing the capture raises CaptureLost, which must not read this as
                // an abandoned drag.
                bool wasDragging = _dragging;
                _pressedAt = null;
                _dragging = false;
                _pointer = null;
                e.Pointer.Capture(null);
                if (wasDragging)
                {
                    _host.EndDrag();
                }
                else if (group.Selected != pane.Id)
                {
                    _host.Select(pane.Id);
                }
            };
            PointerCaptureLost += (_, _) =>
            {
                // Capture taken away mid-drag (another window, a menu): nothing moves.
                if (_dragging)
                {
                    _host.CancelDrag();
                }

                _pressedAt = null;
            };
        }

        private void Reset()
        {
            _dragging = false;
            _pressedAt = null;
            IPointer? pointer = _pointer;
            _pointer = null;
            pointer?.Capture(null);
        }
    }
}
