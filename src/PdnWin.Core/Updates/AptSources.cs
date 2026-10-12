namespace PdnWin.Core.Updates;

/// <summary>Whether this machine installs from the packet-net apt repository.</summary>
public static class AptSources
{
    /// <summary>The repository's host, as a sources line names it.</summary>
    public const string RepoHost = "packet-net.github.io";

    /// <summary>Whether any of these sources texts (one-line or deb822) names the repository.</summary>
    public static bool NamesRepo(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return texts.SelectMany(t => t.Split('\n'))
            .Select(l => l.Trim())
            .Any(l => !l.StartsWith('#') && l.Contains(RepoHost, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The check against this machine's <c>/etc/apt/sources.list</c> and
    /// <c>/etc/apt/sources.list.d</c>. False anywhere without them. Never throws.</summary>
    public static bool ThisMachine() => ThisMachine("/etc/apt/sources.list", "/etc/apt/sources.list.d");

    /// <summary>The same check against the given paths. Never throws.</summary>
    public static bool ThisMachine(string sourcesFile, string sourcesDirectory)
    {
        try
        {
            var texts = new List<string> { ReadIfExists(sourcesFile) };
            if (Directory.Exists(sourcesDirectory))
            {
                texts.AddRange(Directory.EnumerateFiles(sourcesDirectory)
                    .Where(f => f.EndsWith(".list", StringComparison.Ordinal) || f.EndsWith(".sources", StringComparison.Ordinal))
                    .Select(ReadIfExists));
            }

            return NamesRepo(texts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ReadIfExists(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
