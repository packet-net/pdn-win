using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace PdnWin.Ava.Controls;

/// <summary>
/// A list that behaves like a terminal (as the WPF one): follows new lines while the operator is at
/// the bottom, stays put while they have scrolled back, and copies the selection with Ctrl+C.
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
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                string text = string.Join(Environment.NewLine, SelectedItems?.Cast<object>().Select(i => i.ToString()) ?? []);
                if (text.Length > 0)
                {
                    await clipboard.SetTextAsync(text);
                }

                e.Handled = true;
            }
        };
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
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_following || _scrollPending)
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
