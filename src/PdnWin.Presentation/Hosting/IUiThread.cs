namespace PdnWin.Hosting;

/// <summary>
/// The UI thread, as a view model needs it: to get back onto it from a station thread, and to
/// tick on it. Each front-end supplies one over its own dispatcher.
/// </summary>
public interface IUiThread
{
    /// <summary>Whether the caller is on the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Runs <paramref name="action"/> on the UI thread, later.</summary>
    void Post(Action action);

    /// <summary>Calls <paramref name="tick"/> on the UI thread every <paramref name="interval"/>
    /// until the returned handle is disposed.</summary>
    IDisposable StartTimer(TimeSpan interval, Action tick);
}

/// <summary>Helpers over <see cref="IUiThread"/>.</summary>
public static class UiThreadExtensions
{
    /// <summary>Runs <paramref name="action"/> now if already on the UI thread, otherwise posts it.</summary>
    public static void Run(this IUiThread ui, Action action)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(action);
        if (ui.CheckAccess())
        {
            action();
        }
        else
        {
            ui.Post(action);
        }
    }
}
