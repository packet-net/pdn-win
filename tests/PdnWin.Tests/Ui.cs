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

    /// <summary>Runs <paramref name="test"/> on the UI thread, to the end.</summary>
    /// <remarks>Through the overload for a task with a result, which pumps the UI thread until
    /// the task is done. Given the <see cref="Func{Task}"/> itself, <c>Dispatch</c> takes it as a
    /// function returning a value: what it returns is done at the test's first await, and an
    /// assertion after that fails unseen.</remarks>
    public Task Run(Func<Task> test) => _session.Dispatch(
        async () =>
        {
            await test();
            return true;
        },
        CancellationToken.None);

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
