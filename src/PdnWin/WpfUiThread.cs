using System.Windows.Threading;
using PdnWin.Hosting;

namespace PdnWin;

/// <summary>The WPF dispatcher as the view models' <see cref="IUiThread"/>.</summary>
internal sealed class WpfUiThread(Dispatcher dispatcher) : IUiThread
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(action);

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => tick(), dispatcher);
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
