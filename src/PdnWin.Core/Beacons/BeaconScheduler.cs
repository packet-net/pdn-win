using Packet.Core;

namespace PdnWin.Core.Beacons;

/// <summary>A beacon: a UI frame sent on a timer.</summary>
/// <param name="Enabled">Whether it is sent.</param>
/// <param name="Interval">How often. Clamped to at least <see cref="BeaconScheduler.MinimumInterval"/>.</param>
/// <param name="Destination">The UI frame's destination, e.g. BEACON or ID.</param>
/// <param name="Via">Digipeaters, in order.</param>
/// <param name="Text">The text.</param>
public sealed record BeaconSettings(bool Enabled, TimeSpan Interval, Callsign Destination, IReadOnlyList<Callsign> Via, string Text);

/// <summary>
/// Sends a beacon every so often. Never on start-up: the first goes out one interval after it is
/// enabled, so restarting the app does not put a beacon on the air each time.
/// </summary>
public sealed class BeaconScheduler : IDisposable
{
    /// <summary>The shortest interval allowed. A shared channel is not the place for chatter.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(5);

    private readonly Func<BeaconSettings, Task> _send;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private BeaconSettings? _settings;

    /// <summary>Creates a scheduler that sends through <paramref name="send"/>.</summary>
    public BeaconScheduler(Func<BeaconSettings, Task> send, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>When the next beacon is due, or null when none is scheduled.</summary>
    public DateTimeOffset? NextDue { get; private set; }

    /// <summary>Raised when a send fails.</summary>
    public event Action<Exception>? SendFailed;

    /// <summary>Applies settings, restarting the interval.</summary>
    public void Configure(BeaconSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            NextDue = null;
            _settings = settings;
            if (!settings.Enabled)
            {
                return;
            }

            TimeSpan interval = settings.Interval < MinimumInterval ? MinimumInterval : settings.Interval;
            NextDue = _time.GetUtcNow() + interval;
            _timer = _time.CreateTimer(_ => Fire(interval), null, interval, interval);
        }
    }

    /// <summary>Sends the beacon now, whether or not it is enabled.</summary>
    public Task SendNowAsync()
    {
        BeaconSettings? settings;
        lock (_gate)
        {
            settings = _settings;
        }

        return settings is null ? Task.CompletedTask : _send(settings);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Fire(TimeSpan interval)
    {
        BeaconSettings? settings;
        lock (_gate)
        {
            settings = _settings;
            NextDue = _time.GetUtcNow() + interval;
        }

        if (settings is not { Enabled: true })
        {
            return;
        }

        _send(settings).ContinueWith(
            t => SendFailed?.Invoke(t.Exception!.GetBaseException()),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
}
