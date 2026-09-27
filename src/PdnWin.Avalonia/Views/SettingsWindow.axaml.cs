using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PdnWin.Core.Settings;
using PdnWin.ViewModels;

namespace PdnWin.Ava.Views;

/// <summary>The settings dialog, over the same view model as the WPF one.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;

    /// <summary>For the XAML loader.</summary>
    public SettingsWindow()
        : this(new SettingsViewModel(new AppSettings(), null))
    {
    }

    /// <summary>Opens the dialog on <paramref name="model"/>, which it disposes on closing.</summary>
    public SettingsWindow(SettingsViewModel model)
    {
        AvaloniaXamlLoader.Load(this);
        _model = model;
        Closed += (_, _) => model.Dispose();
        DataContext = _model;
        this.FindControl<Button>("Cancel")!.Click += (_, _) => Close(false);
        this.FindControl<Button>("Save")!.Click += (_, _) =>
        {
            Result = _model.Build();
            Close(true);
        };
        Opened += async (_, _) => await _model.ScanAsync();
    }

    /// <summary>The settings to apply, when saved.</summary>
    public AppSettings? Result { get; private set; }
}
