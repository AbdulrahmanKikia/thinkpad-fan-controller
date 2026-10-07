using Media = System.Windows.Media;
using System.Windows.Threading;

namespace LibraryFan;

public partial class FanRing : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _timer;
    private int _rpm;
    private DateTime _last = DateTime.UtcNow;

    public FanRing()
    {
        InitializeComponent();
        Marker.Stroke = (Media.Brush)FindResource("Teal");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _timer.Tick += Advance;
        _timer.Start();
        Show(null, "Fan", "Firmware");
    }

    public void Show(int? rpm, string caption, string command)
    {
        _rpm = rpm ?? 0;
        CaptionText.Text = caption;
        CommandTextBlock.Text = command;
        if (rpm is null)
        {
            RpmText.Text = "—";
            RpmText.Foreground = (Media.Brush)FindResource("Muted");
            UnitText.Text = "NO READING";
            Marker.Opacity = 0.2;
            return;
        }

        RpmText.Foreground = (Media.Brush)FindResource("Ink");
        if (rpm == 0)
        {
            RpmText.Text = "0";
            UnitText.Text = "STOPPED";
            Marker.Opacity = 0.28;
            return;
        }

        RpmText.Text = rpm.Value.ToString();
        UnitText.Text = "RPM";
        Marker.Opacity = 1;
    }

    public void SetAccent(Media.Brush brush) => Marker.Stroke = brush;

    private void Advance(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = (now - _last).TotalSeconds;
        _last = now;
        if (_rpm <= 0)
            return;
        Spin.Angle = (Spin.Angle + (_rpm * 0.45 * seconds)) % 360;
    }
}
