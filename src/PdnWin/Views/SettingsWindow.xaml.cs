using System.ComponentModel;
using System.Windows;
using PdnWin.Core.Settings;
using PdnWin.ViewModels;

namespace PdnWin.Views;

/// <summary>The settings dialog.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;

    /// <summary>Opens the dialog on <paramref name="model"/>, which it disposes on closing.</summary>
    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        _model = model;
        Closed += (_, _) => model.Dispose();
        DataContext = _model;
        _model.PropertyChanged += OnChanged;
        Loaded += async (_, _) =>
        {
            ShowPttFields();
            Call.Focus();
            await _model.ScanAsync();
        };
    }

    /// <summary>The settings to apply, when saved.</summary>
    public AppSettings? Result { get; private set; }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Ptt))
        {
            ShowPttFields();
        }
    }

    private void ShowPttFields()
    {
        bool hid = _model.Ptt == PttKind.Cm108Hid;
        GpioLabel.Visibility = GpioBox.Visibility = hid ? Visibility.Visible : Visibility.Collapsed;
        SerialRow.Visibility = _model.Ptt == PttKind.Serial ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = _model.Build();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
