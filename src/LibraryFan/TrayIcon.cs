using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryFan;

/// <summary>The only Windows Forms in the app: a notification-area icon whose tooltip is the reason sentence.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _glyph;
    private readonly IntPtr _glyphHandle;

    public TrayIcon()
    {
        (_glyph, _glyphHandle) = DrawGlyph();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("BIOS", null, (_, _) => BiosRequested?.Invoke());
        menu.Items.Add("Library", null, (_, _) => LibraryRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        _icon = new NotifyIcon
        {
            Icon = _glyph,
            Visible = true,
            Text = "Library Fan",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public event Action? ShowRequested;
    public event Action? BiosRequested;
    public event Action? LibraryRequested;
    public event Action? ExitRequested;

    public void SetText(string text) =>
        _icon.Text = text.Length <= 63 ? text : text[..60] + "...";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _glyph.Dispose();
        if (_glyphHandle != IntPtr.Zero)
            DestroyIcon(_glyphHandle);
    }

    private static (Icon, IntPtr) DrawGlyph()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var ring = new Pen(Color.FromArgb(44, 56, 72), 5f);
            graphics.DrawEllipse(ring, 4, 4, 24, 24);
            using var marker = new Pen(Color.FromArgb(46, 230, 182), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(marker, 4, 4, 24, 24, -90, 70);
            graphics.DrawArc(marker, 4, 4, 24, 24, 30, 70);
            graphics.DrawArc(marker, 4, 4, 24, 24, 150, 70);
        }

        var handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
