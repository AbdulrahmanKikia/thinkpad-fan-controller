using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryFan;

/// <summary>
/// The only Windows Forms in the app: a notification-area icon whose menu is the whole panel in
/// small. The tooltip is the reason sentence. The glyph takes the panel's heat colour.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private static readonly Color Teal = Color.FromArgb(46, 230, 182);
    private static readonly Color Amber = Color.FromArgb(240, 163, 94);
    private static readonly Color Coral = Color.FromArgb(255, 107, 90);
    private static readonly Color Muted = Color.FromArgb(139, 155, 176);
    private const int WarmCelsius = 65;
    private const int SentenceWidth = 380;
    private const int RowHeight = 22;

    private readonly NotifyIcon _icon;
    private readonly Dictionary<Color, (Icon Icon, IntPtr Handle)> _glyphs = new();
    private readonly ToolStripMenuItem _sentence;
    private readonly ToolStripMenuItem _sentenceTail = Label("");
    private readonly ToolStripMenuItem _fans;
    private readonly ToolStripMenuItem _hottest;
    private readonly ToolStripMenuItem _link;
    private readonly ToolStripMenuItem _bios;
    private readonly ToolStripMenuItem _library;
    private readonly ToolStripMenuItem _manual;
    private readonly ToolStripMenuItem[] _levels;
    private readonly ToolStripMenuItem _startup;

    public TrayIcon()
    {
        _sentence = Label("BIOS is in control.");
        _fans = Label("Fans not read yet");
        _hottest = Label("No temperature yet");
        _link = Label("Starting");

        _bios = Choice("BIOS", () => ModeRequested?.Invoke(ControlMode.Bios, 0));
        _library = Choice("Library", () => ModeRequested?.Invoke(ControlMode.Library, 0));
        _manual = new ToolStripMenuItem("Manual");
        _levels = new ToolStripMenuItem[Registers.MaxLevel + 1];
        for (var level = 0; level <= Registers.MaxLevel; level++)
        {
            var captured = level;
            _levels[level] = Choice($"Level {level}", () => ModeRequested?.Invoke(ControlMode.Manual, captured));
            _manual.DropDownItems.Add(_levels[level]);
        }

        // The first sentence row has a fixed width, so the menu is always wide enough for a
        // wrapped sentence and never shrinks to whatever text was there when it first opened.
        // A long sentence continues on a second ordinary row; a taller single row would make
        // the menu stretch every other row to match.
        _sentence.AutoSize = false;
        _sentence.Width = SentenceWidth;
        _sentence.Height = RowHeight;
        _sentence.TextAlign = ContentAlignment.MiddleLeft;
        _sentenceTail.Visible = false;
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = true };
        menu.Items.Add(_sentence);
        menu.Items.Add(_sentenceTail);
        menu.Items.Add(_fans);
        menu.Items.Add(_hottest);
        menu.Items.Add(_link);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_bios);
        menu.Items.Add(_library);
        menu.Items.Add(_manual);
        menu.Items.Add(new ToolStripSeparator());
        _startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startup.Click += (_, _) => StartWithWindowsChanged?.Invoke(_startup.Checked);
        menu.Items.Add(_startup);
        menu.Items.Add("Open panel", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Exit and hand fans to BIOS", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = Glyph(Teal),
            Visible = true,
            Text = "ThinkPad Fan Controller Utility",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public event Action? ShowRequested;
    public event Action<ControlMode, int>? ModeRequested;
    public event Action? ExitRequested;
    public event Action<bool>? StartWithWindowsChanged;

    public void SetStartWithWindows(bool enabled) => _startup.Checked = enabled;

    /// <summary>Repaints the menu, tooltip, and glyph from one controller update.</summary>
    public void Update(FanUpdate update)
    {
        SetText(update.Reason);
        var (head, tail) = Split(update.Reason, 46);
        _sentence.Text = head;
        _sentenceTail.Text = tail;
        _sentenceTail.Visible = tail.Length > 0;

        _fans.Text = $"Fan 1  {Speed(update.Fan1Rpm, update.Fan1Held)}   ·   Fan 2  {Speed(update.Fan2Rpm, update.Fan2Held)}";
        var hottest = update.Sensors.Where(sensor => sensor.IsValid).MaxBy(sensor => sensor.Celsius);
        _hottest.Text = hottest is null
            ? "No valid temperature"
            : $"Hottest  {hottest.Label}  {hottest.Celsius}°C";
        _link.Text = !update.DriverOpen ? "Offline: " + update.DriverStatus
            : update.MissedPolls > 0 ? "No answer from the controller"
            : update.RegistersRead < update.RegistersTotal ? $"Partial: {update.RegistersRead} of {update.RegistersTotal} registers"
            : "Live: all registers read";

        _bios.Checked = update.Mode == ControlMode.Bios;
        _bios.Text = update.IsLatched ? "BIOS (handed back)" : "BIOS";
        _library.Checked = update.Mode == ControlMode.Library;
        _library.Text = update.Mode == ControlMode.Library && update.CommandedLevel is int held
            ? $"Library  (level {held})"
            : "Library";
        _manual.Checked = update.Mode == ControlMode.Manual;
        _manual.Text = update.Mode == ControlMode.Manual ? $"Manual  (level {update.ManualLevel})" : "Manual";
        for (var level = 0; level <= Registers.MaxLevel; level++)
            _levels[level].Checked = update.Mode == ControlMode.Manual && update.ManualLevel == level;

        // The session refuses these while a sensor is at 80°C; the menu says so instead of
        // offering a click that would only produce a sentence nobody can see.
        _library.Enabled = update.CanTakeControl;
        _manual.Enabled = update.CanTakeControl;

        var colour = update.IsLatched ? Coral
            : hottest is null ? Teal
            : hottest.Celsius >= FanSession.DangerCelsius ? Coral
            : hottest.Celsius >= WarmCelsius ? Amber
            : Teal;
        _icon.Icon = Glyph(update.DriverOpen ? colour : Muted);
    }

    public void SetText(string text) =>
        _icon.Text = text.Length <= 63 ? text : text[..60] + "...";

    public void ShowBalloon(string text) =>
        _icon.ShowBalloonTip(4000, "ThinkPad Fan Controller Utility", text, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        foreach (var (icon, handle) in _glyphs.Values)
        {
            icon.Dispose();
            if (handle != IntPtr.Zero)
                DestroyIcon(handle);
        }
    }

    /// <summary>Breaks one sentence at a space so it fits the fixed menu width on at most two rows.</summary>
    private static (string Head, string Tail) Split(string text, int columns)
    {
        if (text.Length <= columns)
            return (text, "");
        var cut = text.LastIndexOf(' ', columns);
        return cut <= 0 ? (text, "") : (text[..cut], text[(cut + 1)..]);
    }

    private static string Speed(int? rpm, bool held) =>
        rpm is null ? "—" : rpm == 0 ? "stopped" : held ? $"{rpm} (last read)" : $"{rpm} RPM";

    private static ToolStripMenuItem Label(string text) => new(text) { Enabled = false };

    private static ToolStripMenuItem Choice(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private Icon Glyph(Color accent)
    {
        if (_glyphs.TryGetValue(accent, out var cached))
            return cached.Icon;

        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var ring = new Pen(Color.FromArgb(44, 56, 72), 5f);
            graphics.DrawEllipse(ring, 4, 4, 24, 24);
            using var marker = new Pen(accent, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(marker, 4, 4, 24, 24, -90, 70);
            graphics.DrawArc(marker, 4, 4, 24, 24, 30, 70);
            graphics.DrawArc(marker, 4, 4, 24, 24, 150, 70);
        }

        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        _glyphs[accent] = (icon, handle);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>The panel's palette on the WinForms menu. Disabled rows are the read-only status lines.</summary>
    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Card = Color.FromArgb(20, 26, 34);
        private static readonly Color Raised = Color.FromArgb(28, 36, 48);
        private static readonly Color Line = Color.FromArgb(44, 56, 72);
        private static readonly Color Ink = Color.FromArgb(244, 247, 251);

        public DarkRenderer() : base(new DarkColours()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Card);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            var bounds = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            using var brush = new SolidBrush(Raised);
            e.Graphics.FillRectangle(brush, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ink : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using var brush = new SolidBrush(Teal);
            var r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(brush, r.X + (r.Width / 2) - 4, r.Y + (r.Height / 2) - 4, 8, 8);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ink;
            base.OnRenderArrow(e);
        }

        private sealed class DarkColours : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Raised;
            public override Color MenuItemBorder => Raised;
            public override Color MenuBorder => Line;
            public override Color ToolStripDropDownBackground => Card;
            public override Color ImageMarginGradientBegin => Card;
            public override Color ImageMarginGradientMiddle => Card;
            public override Color ImageMarginGradientEnd => Card;
        }
    }
}
