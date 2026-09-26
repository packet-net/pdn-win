using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PdnWin.Controls;

/// <summary>
/// A list that behaves like a terminal: it follows new lines while the operator is at the bottom,
/// stays put while they have scrolled back to read, and copies the selected lines with Ctrl+C.
/// </summary>
public sealed class LogList : ListBox
{
    private ScrollViewer? _scroller;
    private bool _following = true;
    private bool _scrollPending;

    /// <summary>Creates the list.</summary>
    public LogList()
    {
        SelectionMode = SelectionMode.Extended;
        // A subclass does not pick up the implicit ListBox style; ask for it by key.
        SetResourceReference(StyleProperty, typeof(ListBox));
        Loaded += (_, _) =>
        {
            _scroller ??= Find<ScrollViewer>(this);
            if (_scroller is not null)
            {
                _scroller.ScrollChanged += OnScrollChanged;
            }

            ScrollToEnd();
        };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => Copy()));
        ContextMenu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy };
        var all = new MenuItem { Header = "Select all" };
        all.Click += (_, _) => SelectAll();
        ContextMenu.Items.Add(copy);
        ContextMenu.Items.Add(all);
    }

    /// <summary>Scrolls to the newest line and follows from there.</summary>
    public void ScrollToEnd()
    {
        _following = true;
        _scroller?.ScrollToEnd();
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (_following && !_scrollPending)
        {
            // Once per batch of lines, after layout, rather than per line.
            _scrollPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _scrollPending = false;
                if (_following)
                {
                    _scroller?.ScrollToEnd();
                }
            });
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scroller is null)
        {
            return;
        }

        // Only an operator's scroll changes whether we follow; content growing does not.
        if (e.ExtentHeightChange == 0)
        {
            _following = _scroller.VerticalOffset >= _scroller.ScrollableHeight - 2;
        }
    }

    private void Copy()
    {
        IEnumerable<object> selected = SelectedItems.Cast<object>().OrderBy(i => Items.IndexOf(i));
        string text = string.Join(Environment.NewLine, selected.Select(i => i.ToString()));
        if (text.Length > 0)
        {
            Clipboard.SetText(text);
        }
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                return found;
            }

            if (Find<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
