using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LibraryFan;

/// <summary>
/// Renders the window to a PNG from inside the process. Screen capture from another process
/// fails once the app is elevated, so this is how the window is checked.
/// </summary>
internal static class WindowSnapshot
{
    public static void Save(Window window, string path)
    {
        // Window.ActualHeight includes the title bar and frame, which the render does not draw,
        // so size the bitmap to the client area: the content element plus its margin.
        var width = window.ActualWidth;
        var height = window.ActualHeight;
        if (window.Content is FrameworkElement content)
        {
            width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
            height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
