using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Stations;

namespace PdnWin.ViewModels;

/// <summary>A station we have heard.</summary>
public sealed partial class HeardStation : ObservableObject
{
    [ObservableProperty]
    private DateTimeOffset _lastHeard;

    [ObservableProperty]
    private int _frames;

    [ObservableProperty]
    private string _lastTo = string.Empty;

    [ObservableProperty]
    private string? _quality;

    [ObservableProperty]
    private string _ago = "now";

    /// <summary>The callsign.</summary>
    public required string Call { get; init; }

    /// <summary>Refreshes <see cref="Ago"/>.</summary>
    public void Tick(DateTimeOffset now)
    {
        TimeSpan age = now - LastHeard;
        Ago = age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds}s"
            : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours}h"
            : LastHeard.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);
    }
}

/// <summary>The heard list: who is on the channel, most recent first.</summary>
public sealed class HeardViewModel
{
    private readonly Dictionary<string, HeardStation> _byCall = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stations, most recently heard first.</summary>
    public ObservableCollection<HeardStation> Stations { get; } = [];

    /// <summary>Notes a received frame. UI thread.</summary>
    public void Add(MonitorEntry entry)
    {
        if (entry.Direction != FrameDirection.Received || entry.Source is not { } source)
        {
            return;
        }

        string call = MonitorFormatter.Callsign(source);
        if (!_byCall.TryGetValue(call, out HeardStation? station))
        {
            station = new HeardStation { Call = call };
            _byCall[call] = station;
        }
        else
        {
            Stations.Remove(station);
        }

        station.LastHeard = entry.Time;
        station.Frames++;
        station.LastTo = entry.Destination is { } to ? MonitorFormatter.Callsign(to) : string.Empty;
        station.Quality = entry.Detail.Length > 0 ? entry.Detail : station.Quality;
        station.Tick(entry.Time);
        Stations.Insert(0, station);
    }

    /// <summary>Refreshes every "ago". UI thread.</summary>
    public void Tick()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (HeardStation station in Stations)
        {
            station.Tick(now);
        }
    }
}
