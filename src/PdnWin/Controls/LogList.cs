using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace PdnWin.App.Controls;

/// <summary>
/// A list that behaves like a terminal: follows new lines while the operator is at the bottom,
/// stays put while they have scrolled back, opens at the newest line, and copies the selection
/// (Ctrl+C, or the right-click menu) in the order the lines appear.
/// </summary>
public sealed class LogList : ListBox
{
    private ScrollViewer? _scroller;
    private INotifyCollectionChanged? _watched;
    private bool _following = true;
    private bool _scrollPending;

    /// <summary>Creates the list.</summary>
    public LogList()
    {
        SelectionMode = SelectionMode.Multiple;
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await CopyAsync();
            }
        };

        var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += async (_, _) => await CopyAsync();
        var all = new MenuItem { Header = "Select all", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
        all.Click += (_, _) => SelectAll();
        ContextMenu = new ContextMenu { Items = { copy, all } };
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
        }

        _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            if (_watched is not null)
            {
                _watched.CollectionChanged -= OnItemsChanged;
            }

            _watched = ItemsSource as INotifyCollectionChanged;
            if (_watched is not null)
            {
                _watched.CollectionChanged += OnItemsChanged;
            }

            // Another session's transcript: start from its newest line.
            _following = true;
            ScrollToEndSoon();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Shown again (a pane moved by the dock, a tab brought forward): at the newest line.
        _following = true;
        ScrollToEndSoon();
    }

    private async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || ItemsSource is not System.Collections.IList items)
        {
            return;
        }

        // In the order the lines are in, not the order they were clicked.
        string text = string.Join(Environment.NewLine, Selection.SelectedIndexes
            .Where(i => i >= 0 && i < items.Count)
            .Order()
            .Select(i => items[i]?.ToString()));
        if (text.Length > 0)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_following)
        {
            return;
        }

        ScrollToEndSoon();
    }

    private void ScrollToEndSoon()
    {
        if (_scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            if (_following)
            {
                _scroller?.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller is null)
        {
            return;
        }

        // Only an operator's scroll changes whether we follow; content growing does not.
        if (e.ExtentDelta.Y == 0)
        {
            _following = _scroller.Offset.Y >= _scroller.Extent.Height - _scroller.Viewport.Height - 2;
        }
    }
}
