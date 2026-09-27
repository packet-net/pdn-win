using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Stations;

namespace PdnWin.ViewModels;

/// <summary>One monitor row.</summary>
public sealed class MonitorItem(MonitorEntry entry)
{
    /// <summary>The formatted frame.</summary>
    public MonitorEntry Entry { get; } = entry;

    /// <summary>HH:mm:ss local.</summary>
    public string Time { get; } = entry.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Sent by us.</summary>
    public bool IsTx => Entry.Direction == FrameDirection.Transmitted;

    /// <summary>"TX" or "RX".</summary>
    public string Direction => IsTx ? "TX" : "RX";

    /// <summary>The frame kind, for colour.</summary>
    public FrameKind Kind => Entry.Kind;

    /// <summary>The header line.</summary>
    public string Header => Entry.Header;

    /// <summary>The info field, one line per line.</summary>
    public string? Info { get; } = entry.Info.Count == 0 ? null : string.Join("\n", entry.Info);

    /// <summary>Receive diagnostics.</summary>
    public string? Detail => Entry.Detail.Length == 0 ? null : Entry.Detail;

    /// <summary>A level verdict badge.</summary>
    public string? Badge => Entry.Badge;

    /// <summary>The row as text, for copying.</summary>
    public override string ToString() =>
        $"{Time} {Direction} {Header}{(Info is null ? string.Empty : "\n    " + Info.Replace("\n", "\n    ", StringComparison.Ordinal))}";
}

/// <summary>The monitor pane: every frame heard and sent, BPQ style.</summary>
public sealed partial class MonitorViewModel : ObservableObject
{
    private const int Capacity = 4000;

    [ObservableProperty]
    private bool _showSupervisory = true;

    [ObservableProperty]
    private bool _paused;

    /// <summary>The rows, oldest first.</summary>
    public ObservableCollection<MonitorItem> Items { get; } = [];

    /// <summary>Raised after a row is added, for auto-scrolling.</summary>
    public event Action<MonitorItem>? Added;

    /// <summary>Adds a frame. UI thread.</summary>
    public void Add(MonitorEntry entry)
    {
        if (Paused || (!ShowSupervisory && entry.Kind == FrameKind.Supervisory))
        {
            return;
        }

        var item = new MonitorItem(entry);
        Items.Add(item);
        if (Items.Count > Capacity)
        {
            Items.RemoveAt(0);
        }

        Added?.Invoke(item);
    }

    /// <summary>Empties the pane.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Clear() => Items.Clear();
}
