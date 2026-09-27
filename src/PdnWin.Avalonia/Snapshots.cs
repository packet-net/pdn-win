using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PdnWin.Ava;

/// <summary>
/// Development aid, as in the WPF app: with <c>PDNWIN_SNAPSHOT</c> set to a directory, every open
/// window renders itself to a PNG there each second, named after its title.
/// </summary>
internal static class Snapshots
{
    public static void StartIfRequested()
    {
        string? directory = Environment.GetEnvironmentVariable("PDNWIN_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Capture(directory));
        timer.Start();
    }

    private static void Capture(string directory)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (Window window in desktop.Windows)
        {
            if (!window.IsVisible || window.Bounds.Width < 1)
            {
                continue;
            }

            double scale = window.RenderScaling;
            using var bitmap = new RenderTargetBitmap(
                new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(window);
            string name = string.Concat((window.Title ?? "window").Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(directory, name + ".png");
            try
            {
#pragma warning disable CS0618 // the replacement overload needs encoder options this does not care about
                bitmap.Save(path + ".tmp");
#pragma warning restore CS0618
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
