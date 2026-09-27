using Avalonia.Threading;
using PdnWin.Hosting;

namespace PdnWin.Ava;

/// <summary>Avalonia's UI thread as the view models' <see cref="IUiThread"/>.</summary>
internal sealed class AvaloniaUiThread : IUiThread
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => tick());
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
