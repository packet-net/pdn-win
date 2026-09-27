using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PdnWin.Ava.Views;

/// <summary>The guided level setup: receive by the handheld's volume, transmit by Bessel null.</summary>
public partial class LevelHelperPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public LevelHelperPane() => AvaloniaXamlLoader.Load(this);
}
