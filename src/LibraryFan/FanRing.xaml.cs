using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LibraryFan;

/// <summary>
/// An instrument arc for one fan. The arc fills with measured RPM on a fixed 0–6000 scale and the
/// rotor inside turns at the measured speed. Zero is stopped. A held value is marked, not hidden.
/// </summary>
public partial class FanRing : UserControl
{
    public const int GaugeMaxRpm = 6000;

    private const double DegreesPerRpmSecond = 0.45;
    private const double Center = 86;
    private const double Radius = 74;
    private const double StartAngle = -135;
    private const double Sweep = 270;

    private readonly DispatcherTimer _timer;
    private int _rpm;
    private DateTime _last = DateTime.UtcNow;

    public FanRing()
    {
        InitializeComponent();
        Track.Data = Arc(StartAngle, Sweep);
        SetAccent((Brush)FindResource("Teal"));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _timer.Tick += Advance;
        _timer.Start();
        Show(null, false, "Fan", "Firmware");
    }

    public void Show(int? rpm, bool held, string caption, string command)
    {
        _rpm = rpm ?? 0;
        CaptionText.Text = caption;
        CommandText.Text = command;

        if (rpm is null)
        {
            RpmText.Text = "—";
            RpmText.Foreground = (Brush)FindResource("Muted");
            UnitText.Text = "NO READING";
            Fill.Data = Geometry.Empty;
            Rotor.Opacity = 0.18;
            return;
        }

        RpmText.Foreground = (Brush)FindResource("Ink");
        var share = Math.Clamp(rpm.Value / (double)GaugeMaxRpm, 0, 1);
        Fill.Data = Arc(StartAngle, Sweep * share);
        Fill.Opacity = held ? 0.55 : 1;

        if (rpm == 0)
        {
            RpmText.Text = "0";
            UnitText.Text = "STOPPED";
            Rotor.Opacity = 0.28;
            return;
        }

        RpmText.Text = rpm.Value.ToString();
        UnitText.Text = held ? "LAST READ" : "RPM";
        Rotor.Opacity = held ? 0.4 : 0.75;
    }

    public void SetAccent(Brush brush)
    {
        Fill.Stroke = brush;
        Rotor.Stroke = brush;
    }

    private static Geometry Arc(double startDegrees, double sweepDegrees)
    {
        if (sweepDegrees < 0.5)
            return Geometry.Empty;
        var figure = new PathFigure { StartPoint = PointAt(startDegrees), IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            PointAt(startDegrees + sweepDegrees),
            new Size(Radius, Radius),
            0,
            sweepDegrees > 180,
            SweepDirection.Clockwise,
            true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Angles are clockwise from twelve o'clock, so −135° is bottom-left and +135° is bottom-right.</summary>
    private static Point PointAt(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(Center + (Radius * Math.Sin(radians)), Center - (Radius * Math.Cos(radians)));
    }

    private void Advance(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = (now - _last).TotalSeconds;
        _last = now;
        if (_rpm <= 0)
            return;
        Spin.Angle = (Spin.Angle + (_rpm * DegreesPerRpmSecond * seconds)) % 360;
    }
}
