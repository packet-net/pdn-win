using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PdnWin.App.Views;

/// <summary>The console and a tab per connection.</summary>
public partial class SessionsPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public SessionsPane() => AvaloniaXamlLoader.Load(this);
}
