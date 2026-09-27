namespace PdnWin.Core;

/// <summary>
/// What the app is called on this machine: pdn-win on Windows and pdn-lin on Linux. One app, named
/// for its platform, which is also where its settings and logs live.
/// </summary>
public static class AppIdentity
{
    /// <summary>"pdn-lin" on Linux, "pdn-win" everywhere else.</summary>
    public static string Name { get; } = OperatingSystem.IsLinux() ? "pdn-lin" : "pdn-win";

    /// <summary>The part of the name after "pdn": "-lin" or "-win", for the wordmark.</summary>
    public static string Suffix => Name[3..];

    /// <summary>
    /// Where the app keeps its files: <c>%APPDATA%\pdn-win</c> on Windows,
    /// <c>~/.config/pdn-lin</c> on Linux.
    /// </summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
}
