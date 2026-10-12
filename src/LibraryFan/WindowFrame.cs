using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LibraryFan;

/// <summary>The fan mark used as the window icon, and the dark caption the system buttons sit on.</summary>
internal static class WindowFrame
{
    public static void Apply(Window window)
    {
        window.Icon = Icon();
        var hwnd = new WindowInteropHelper(window).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var round = DwmRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        var caption = CaptionColor;
        DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int));
        var text = TextColor;
        DwmSetWindowAttribute(hwnd, DwmTextColor, ref text, sizeof(int));
    }

    private static ImageSource Icon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(44, 56, 72), 3f);
            graphics.DrawEllipse(ring, 3, 3, 26, 26);
            using var marker = new System.Drawing.Pen(System.Drawing.Color.FromArgb(46, 230, 182), 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(marker, 3, 3, 26, 26, -90, 70);
            graphics.DrawArc(marker, 3, 3, 26, 26, 30, 70);
            graphics.DrawArc(marker, 3, 3, 26, 26, 150, 70);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int DwmRound = 2;
    private const int CaptionColor = 0x00130E0B;
    private const int TextColor = 0x00FBF7F4;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
