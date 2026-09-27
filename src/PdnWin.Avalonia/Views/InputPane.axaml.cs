using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PdnWin.ViewModels;

namespace PdnWin.Ava.Views;

/// <summary>The line the operator types into: Enter sends, Up and Down walk the history.</summary>
public partial class InputPane : UserControl
{
    private readonly TextBox _line;

    /// <summary>Creates the pane.</summary>
    public InputPane()
    {
        AvaloniaXamlLoader.Load(this);
        _line = this.FindControl<TextBox>("Line")!;
        // Tunnelling, so the arrows are ours before the text box moves the caret with them.
        _line.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => _line.Focus();
    }

    /// <summary>Puts the caret in the line.</summary>
    public void FocusLine() => _line.Focus();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                model.Sessions.SendCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                model.Sessions.HistoryUp();
                _line.CaretIndex = _line.Text?.Length ?? 0;
                e.Handled = true;
                break;
            case Key.Down:
                model.Sessions.HistoryDown();
                _line.CaretIndex = _line.Text?.Length ?? 0;
                e.Handled = true;
                break;
        }
    }
}
