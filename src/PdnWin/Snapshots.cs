using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PdnWin;

/// <summary>
/// Development aid: with the environment variable <c>PDNWIN_SNAPSHOT</c> set to a directory, every
/// open window renders itself to a PNG there every second (named after its title). This is how the
/// UI is checked from a script without taking focus from whatever the developer is doing.
/// </summary>
internal static class Snapshots
{
    public static void StartIfRequested(Dispatcher dispatcher)
    {
        string? directory = Environment.GetEnvironmentVariable("PDNWIN_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Capture(directory), dispatcher);
        timer.Start();
    }

    private static void Capture(string directory)
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible || window.Content is not FrameworkElement root || root.ActualWidth < 1)
            {
                continue;
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap(
                (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }

            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string name = string.Concat(window.Title.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(directory, (name.Length == 0 ? "window" : name) + ".png");
            try
            {
                using FileStream file = File.Create(path + ".tmp");
                encoder.Save(file);
                file.Close();
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (IOException)
            {
                // The reader has it open; next second will do.
            }
        }
    }
}
