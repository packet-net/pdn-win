using System.Windows.Controls;
using System.Windows.Input;
using PdnWin.ViewModels;

namespace PdnWin.Views;

/// <summary>The line the operator types into: Enter sends, Up and Down walk the history.</summary>
public partial class InputPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public InputPane()
    {
        InitializeComponent();
        Loaded += (_, _) => Line.Focus();
    }

    /// <summary>Puts the caret in the line.</summary>
    public void FocusLine() => Line.Focus();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                model.Sessions.SendCommand.Execute(null);
                Line.CaretIndex = Line.Text.Length;
                e.Handled = true;
                break;
            case Key.Up:
                model.Sessions.HistoryUp();
                Line.CaretIndex = Line.Text.Length;
                e.Handled = true;
                break;
            case Key.Down:
                model.Sessions.HistoryDown();
                Line.CaretIndex = Line.Text.Length;
                e.Handled = true;
                break;
        }
    }
}
