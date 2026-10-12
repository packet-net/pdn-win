using System.Net;

namespace PdnWin.Core.Updates;

/// <summary>A newer release than the one running.</summary>
/// <param name="Installed">The version running.</param>
/// <param name="Available">The newer version.</param>
/// <param name="ReleaseUrl">That release's page: its notes, and the Windows downloads.</param>
/// <param name="AptCommand">The command that upgrades a copy installed from the packet-net apt
/// repository; null anywhere else (Windows, or a <c>.deb</c> or tarball installed by hand).</param>
public sealed record AvailableUpdate(string Installed, string Available, string ReleaseUrl, string? AptCommand);

/// <summary>
/// Looks for a newer release: once soon after <see cref="Start"/>, then once a day. Always on,
/// never installs anything, and quiet when it cannot find out (offline, a bad gateway, a page that
/// is not the index): it tries again at the next look.
/// </summary>
/// <remarks>
/// It reads the packet-net apt repository's index, as pdn-soundmodem's own check does, on
/// Windows too: one release carries both platforms, the apt index is updated by that release
/// after the GitHub release (with the MSI) is published, and a failed update fails the release
/// (docs/releasing.md), so the <c>pdn-lin</c> version there is the version for both.
/// </remarks>
public sealed class UpdateCheck : IDisposable
{
    /// <summary>The packet-net apt repository's index.</summary>
    public const string PackagesUrl = "https://packet-net.github.io/apt/Packages";

    /// <summary>The package the index lists this app under.</summary>
    public const string Package = "pdn-lin";

    /// <summary>Where the releases are.</summary>
    public const string ReleasesUrl = "https://github.com/packet-net/pdn-win/releases";

    /// <summary>What upgrades a copy installed from the apt repository.</summary>
    public const string AptCommand = "sudo apt update && sudo apt install --only-upgrade " + Package;

    /// <summary>How often it looks after the first.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    // The index is small (tens of packages) and this GETs it once a day.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };

    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task<string>> _fetch;
    private readonly Func<bool> _fromAptRepo;
    private readonly CancellationTokenSource _stop = new();
    private ITimer? _timer;

    /// <summary>
    /// A check for <paramref name="current"/>. <paramref name="fetch"/> (the index's text) and
    /// <paramref name="fromAptRepo"/> (whether this machine installs from the apt repository) are
    /// for tests; left out, they are the real repository and the real machine.
    /// </summary>
    public UpdateCheck(string current, TimeProvider? time = null, Func<CancellationToken, Task<string>>? fetch = null, Func<bool>? fromAptRepo = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        Current = current;
        _time = time ?? TimeProvider.System;
        _fetch = fetch ?? FetchAsync;
        _fromAptRepo = fromAptRepo ?? (() => OperatingSystem.IsLinux() && AptSources.ThisMachine());
    }

    /// <summary>The version running.</summary>
    public string Current { get; }

    /// <summary>How long after <see cref="Start"/> the first look is: long enough to stay out of
    /// the way of start-up.</summary>
    public TimeSpan FirstDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Raised by every look that finds a newer release, on the timer's thread.</summary>
    public event Action<AvailableUpdate>? Found;

    /// <summary>Starts looking. A build that is not a release (<c>0.0.0-dev</c>) never looks: it
    /// would always be behind.</summary>
    public void Start()
    {
        if (!System.Version.TryParse(Current, out _))
        {
            return;
        }

        // Taken once here: a look that fires as the app closes must not read the token from a
        // source Dispose has already disposed.
        CancellationToken stop = _stop.Token;
        _timer = _time.CreateTimer(_ => _ = CheckOnceAsync(stop), null, FirstDelay, Every);
    }

    /// <summary>One look. Never throws, but for <paramref name="cancellation"/>.</summary>
    public async Task CheckOnceAsync(CancellationToken cancellation)
    {
        if (!System.Version.TryParse(Current, out Version? current))
        {
            return;
        }

        try
        {
            Version? newest = Newest(await _fetch(cancellation).ConfigureAwait(false));
            if (newest is not null && newest > current)
            {
                string available = newest.ToString();
                Found?.Invoke(new AvailableUpdate(Current, available, $"{ReleasesUrl}/tag/{available}",
                    _fromAptRepo() ? AptCommand : null));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Not knowing is never an error: it looks again tomorrow.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    /// The newest <see cref="Package"/> in an apt <c>Packages</c> index (stanzas of
    /// <c>Field: value</c> lines, a blank line between them), or null when it lists none. Versions
    /// that are not plain numbers are passed over.
    /// </summary>
    public static Version? Newest(string index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Version? newest = null;
        string? package = null;
        string? version = null;

        void Finish()
        {
            if (package == Package && System.Version.TryParse(version, out Version? v) && (newest is null || v > newest))
            {
                newest = v;
            }

            package = version = null;
        }

        foreach (string raw in index.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Finish();
            }
            else if (line.StartsWith("Package:", StringComparison.OrdinalIgnoreCase))
            {
                package = line["Package:".Length..].Trim();
            }
            else if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                version = line["Version:".Length..].Trim();
            }
        }

        Finish();
        return newest;
    }

    /// <summary>Stops looking.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task<string> FetchAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PackagesUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", $"{AppIdentity.Name}/{Current} (+https://github.com/packet-net/pdn-win)");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"the apt repository answered {(int)response.StatusCode}");
        }

        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }
}
