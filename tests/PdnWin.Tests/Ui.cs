using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace PdnWin.Tests;

/// <summary>How the headless app is built: the real one, with its theme, on the headless platform.</summary>
public static class TestAppBuilder
{
    /// <summary>What <see cref="HeadlessUnitTestSession"/> calls.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::PdnWin.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .WithInterFont();
}

/// <summary>
/// One headless Avalonia session for every UI test: a process can host one application, so the
/// tests share it, one at a time, through the <c>ui</c> collection.
/// </summary>
public sealed class Ui : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    /// <summary>Runs <paramref name="test"/> on the UI thread.</summary>
    public Task Run(Func<Task> test) => _session.Dispatch(test, CancellationToken.None);

    /// <summary>Lets the UI thread catch up: bindings, layout, posted work.</summary>
    public static void Settle() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Leaves the session running. Disposing it waits for its UI thread to stop, which on
    /// Avalonia 12.1.3 never happens and hangs the test process after the last test has passed;
    /// the thread is a background one, so the process ending is what stops it.
    /// </summary>
    public void Dispose()
    {
    }
}

/// <summary>The collection every UI test belongs to, so they share one session and never overlap.</summary>
[CollectionDefinition("ui")]
public sealed class UiCollection : ICollectionFixture<Ui>
{
}
