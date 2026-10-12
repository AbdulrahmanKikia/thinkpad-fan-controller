using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Shapes = System.Windows.Shapes;

namespace LibraryFan;

public partial class MainWindow : Window
{
    private const int WarmCelsius = 65;
    private const double TraceMinCelsius = 30;
    private const double TraceMaxCelsius = 90;

    private readonly bool _simulate;
    private TrayIcon? _tray;
    private bool _quit;
    private SensorTile[]? _sensorTiles;
    private double _traceChromeWidth;
    private double _traceChromeHeight;
    private readonly List<UIElement> _traceSeries = new();

    internal FanController Controller { get; }
    internal FanUpdate? Latest { get; private set; }
    internal bool HasTray => _tray is not null;

    public MainWindow(IEcClient client, bool simulate, bool selfTest, string? machineName = null)
    {
        InitializeComponent();
        _simulate = simulate;
        if (!string.IsNullOrWhiteSpace(machineName))
            MachineNameText.Text = machineName;
        Controller = new FanController(client);
        Controller.Updated += OnUpdated;

        SimPanel.Visibility = simulate ? Visibility.Visible : Visibility.Collapsed;
        SimTempLabel.Text = $"{(int)SimTemp.Value}°";
        SimTemp.ValueChanged += (_, _) =>
        {
            SimTempLabel.Text = $"{(int)SimTemp.Value}°";
            Controller.SetSimTemperature((int)SimTemp.Value);
        };

        BuildLevelButtons();
        BuildCurveChips(Controller.Session.Curve);
        if (!selfTest)
            BuildTray();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && Latest is not null)
                Paint(Latest);
        };

        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _tray?.Dispose();
            Controller.Dispose();
        };
        TraceCanvas.SizeChanged += (_, _) =>
        {
            if (Latest is not null)
                PaintTrace(Latest.Trace);
        };

        if (!selfTest && !simulate && client is UnavailableEc unavailable && unavailable.NeedsAdministrator)
            ShowElevateHint(client.Status);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFrame.Apply(this);

        var area = SystemParameters.WorkArea;
        if (Width > area.Width - 24)
            Width = Math.Max(MinWidth, area.Width - 24);
        if (Height > area.Height - 24)
            Height = Math.Max(MinHeight, area.Height - 24);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    internal async Task<bool> WaitForUpdateAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Latest is null && DateTime.UtcNow < deadline)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (Latest is null)
                await Task.Delay(30);
        }

        return Latest is not null;
    }

    internal Button LevelButton(int level) => (Button)LevelHost.Children[level];

    internal void SetSimTemperature(int celsius) => SimTemp.Value = celsius;

    internal void ShowFailure(string message) => ReasonText.Text = message;

    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// The X, Alt+F4, and minimize put the panel away. The controller keeps running.
    /// Only <see cref="Quit"/> closes for real, and that is what restores firmware.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_quit || _tray is null)
            return;
        e.Cancel = true;
        Hide();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized || _quit || _tray is null)
            return;
        Hide();
        WindowState = WindowState.Normal;
    }

    /// <summary>Closes the panel for real and ends the process. This is the tray Exit path.</summary>
    internal void Quit(int code = 0)
    {
        _quit = true;
        Close();
        System.Windows.Application.Current?.Shutdown(code);
    }

    /// <summary>One notice when the app starts with nothing on screen, so the tray icon can be found.</summary>
    internal void AnnounceInTray() =>
        _tray?.ShowBalloon("ThinkPad Fan Controller Utility is in the tray. BIOS is in control.");

    private void BiosClick(object sender, RoutedEventArgs e) => Controller.Request(ControlMode.Bios);

    private void LibraryClick(object sender, RoutedEventArgs e) => Controller.Request(ControlMode.Library);

    private void ManualClick(object sender, RoutedEventArgs e) => Controller.Request(ControlMode.Manual, Latest?.ManualLevel ?? 1);

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Controller.RestoreBios();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            Controller.RestoreBios();
    }

    private void OnUpdated(FanUpdate update)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnUpdated(update));
            return;
        }

        Latest = update;
        _tray?.Update(update);
        if (!IsVisible)
            return;
        Paint(update);
    }

    private void Paint(FanUpdate update)
    {
        ReasonText.Text = update.Reason;
        RefusalText.Text = update.Refusal ?? "";
        RefusalText.Visibility = string.IsNullOrEmpty(update.Refusal) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = update.DriverStatus;
        StatusText.Visibility = string.IsNullOrEmpty(update.DriverStatus) ? Visibility.Collapsed : Visibility.Visible;
        HealthText.Text = update.Health;

        Fan1.Show(update.Fan1Rpm, update.Fan1Held, "Fan 1", update.CommandLabel);
        Fan2.Show(update.Fan2Rpm, update.Fan2Held, "Fan 2", update.CommandLabel);
        var hottest = update.Sensors.Where(sensor => sensor.IsValid).Max(sensor => sensor.Celsius);
        var accent = Brush(update.IsLatched ? "Coral" : hottest is null ? "Teal" : HeatKey(hottest));
        Fan1.SetAccent(accent);
        Fan2.SetAccent(accent);

        PaintLink(update);
        PaintMode(update);
        PaintSensors(update.Sensors);
        PaintTrace(update.Trace);
    }

    /// <summary>Teal below 65°C, amber from 65°C, coral at the 80°C handoff. Shared by the gauges and the sensor bars.</summary>
    private static string HeatKey(int? celsius) =>
        celsius is null ? "Line"
        : celsius >= FanSession.DangerCelsius ? "Coral"
        : celsius >= WarmCelsius ? "Amber"
        : "Teal";

    private void PaintLink(FanUpdate update)
    {
        var (text, key, beat) =
            !update.DriverOpen ? ("OFFLINE", "Muted", false)
            : update.MissedPolls > 0 ? ("NO ANSWER", "Coral", false)
            : update.RegistersRead < update.RegistersTotal ? ("PARTIAL", "Amber", true)
            : ("LIVE", "Teal", true);
        LinkText.Text = text;
        LinkText.Foreground = Brush(key);
        LinkDot.Background = Brush(key);
        LinkDot.BeginAnimation(OpacityProperty, null);
        LinkDot.Opacity = 1;
        if (beat)
        {
            LinkDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, FanController.PollInterval - TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void PaintMode(FanUpdate update)
    {
        ModeBadgeText.Text = update.IsLatched ? "HANDED BACK" : update.Mode switch
        {
            ControlMode.Library => "LIBRARY",
            ControlMode.Manual => "MANUAL",
            _ => "BIOS"
        };
        ModeBadge.Background = update.IsLatched ? Brush("Coral") : Brush("Teal");
        ModeBadgeText.Foreground = update.IsLatched ? Brushes.White : Brush("OnAccent");

        Mark(BiosButton, update.Mode == ControlMode.Bios && !update.IsLatched);
        Mark(LibraryButton, update.Mode == ControlMode.Library);
        Mark(ManualButton, update.Mode == ControlMode.Manual);
        for (var level = 0; level <= Registers.MaxLevel; level++)
            Mark(LevelButton(level), update.Mode == ControlMode.Manual && update.ManualLevel == level);
        RegisterLabel.Text = $"REGISTER 0x2F · {update.CommandLabel.ToUpperInvariant()}";

        // While a sensor is still at 80°C the session refuses to take over; the buttons stay
        // clickable so the refusal sentence can be shown, but they stop looking available.
        var available = update.CanTakeControl ? 1.0 : 0.5;
        LibraryButton.Opacity = available;
        ManualButton.Opacity = available;
        LevelHost.Opacity = available;

        var held = update.Mode == ControlMode.Library ? update.CommandedLevel : null;
        foreach (var child in CurveHost.Children)
        {
            if (child is Border chip && chip.Child is TextBlock text)
            {
                var active = held is not null && (int)chip.Tag == held;
                chip.BorderBrush = Brush(active ? "Teal" : "Line");
                text.Foreground = Brush(active ? "Ink" : "Muted");
            }
        }
    }

    private void PaintSensors(SensorSample[] sensors)
    {
        if (_sensorTiles is null || _sensorTiles.Length != sensors.Length)
        {
            SensorHost.Children.Clear();
            _sensorTiles = new SensorTile[sensors.Length];
            for (var i = 0; i < sensors.Length; i++)
            {
                var tile = SensorTile.Create((Style)FindResource("Surface"), Brush("Raised"), Brush("Line"), Brush("Muted"));
                _sensorTiles[i] = tile;
                SensorHost.Children.Add(tile.Root);
            }
        }

        var hottest = sensors.Where(sensor => sensor.IsValid).MaxBy(sensor => sensor.Celsius);
        for (var i = 0; i < sensors.Length; i++)
            _sensorTiles[i].Update(sensors[i], hottest, Brush, HeatKey);
    }

    /// <summary>
    /// Fixed scales so the picture means the same thing every minute: 30–90°C on the left,
    /// 0–6000 RPM on the right, one grid for both, a dashed line at the 80°C handoff, and the
    /// newest sample marked at the right edge.
    /// </summary>
    private void PaintTrace(IReadOnlyList<TraceSample> trace)
    {
        TracePlaceholder.Visibility = trace.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        var width = TraceCanvas.ActualWidth;
        var height = TraceCanvas.ActualHeight;
        if (width < 120 || height < 60)
            return;

        if (Math.Abs(width - _traceChromeWidth) > 0.5 || Math.Abs(height - _traceChromeHeight) > 0.5)
        {
            TraceCanvas.Children.Clear();
            _traceSeries.Clear();
            DrawTraceChrome(width, height);
            _traceChromeWidth = width;
            _traceChromeHeight = height;
        }
        else
        {
            foreach (var item in _traceSeries)
                TraceCanvas.Children.Remove(item);
            _traceSeries.Clear();
        }

        if (trace.Count < 2)
            return;

        var plot = PlotOf(width, height);
        double YForCelsius(double celsius) =>
            plot.Bottom - (Math.Clamp((celsius - TraceMinCelsius) / (TraceMaxCelsius - TraceMinCelsius), 0, 1) * plot.Height);
        double YForRpm(double rpm) => plot.Bottom - (Math.Clamp(rpm / FanRing.GaugeMaxRpm, 0, 1) * plot.Height);

        var temperature = Polyline(trace, plot, sample => YForCelsius(sample.Celsius), Brush("Teal"));
        var speed = Polyline(trace, plot, sample => YForRpm(sample.Rpm), Brush("Amber"));
        AddSeries(speed);
        AddSeries(temperature);
        AddSeries(EndDot(speed.Points[^1], "Amber"));
        AddSeries(EndDot(temperature.Points[^1], "Teal"));
    }

    private static Rect PlotOf(double width, double height) =>
        new(30, 6, Math.Max(0, width - 62), Math.Max(0, height - 24));

    private void DrawTraceChrome(double width, double height)
    {
        var plot = PlotOf(width, height);
        double YForCelsius(double celsius) =>
            plot.Bottom - (Math.Clamp((celsius - TraceMinCelsius) / (TraceMaxCelsius - TraceMinCelsius), 0, 1) * plot.Height);

        for (var band = 0; band <= 3; band++)
        {
            var celsius = TraceMinCelsius + (band * 20);
            var y = Math.Round(YForCelsius(celsius)) + 0.5;
            TraceCanvas.Children.Add(new Shapes.Line
            {
                X1 = plot.Left, X2 = plot.Right, Y1 = y, Y2 = y,
                Stroke = Brush("Line"), StrokeThickness = 1
            });
            AxisLabel($"{celsius}°", plot.Left - 6, y, right: true, "Muted");
            AxisLabel(band == 0 ? "0" : $"{band * 2}k", plot.Right + 6, y, right: false, "Muted");
        }

        var danger = Math.Round(YForCelsius(FanSession.DangerCelsius)) + 0.5;
        TraceCanvas.Children.Add(new Shapes.Line
        {
            X1 = plot.Left, X2 = plot.Right, Y1 = danger, Y2 = danger,
            Stroke = Brush("Coral"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Opacity = 0.8
        });
        AxisLabel($"{FanSession.DangerCelsius}°", plot.Left - 6, danger, right: true, "Coral");

        string[] marks = { "−3 min", "−2", "−1", "now" };
        for (var i = 0; i < marks.Length; i++)
        {
            var x = Math.Round(plot.Left + (plot.Width * i / 3)) + 0.5;
            TraceCanvas.Children.Add(new Shapes.Line
            {
                X1 = x, X2 = x, Y1 = plot.Bottom, Y2 = plot.Bottom + 4,
                Stroke = Brush("Line"), StrokeThickness = 1
            });
            var label = new TextBlock { Text = marks[i], Foreground = Brush("Muted"), FontSize = 10, FontWeight = FontWeights.SemiBold };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var left = i == 0 ? x : i == marks.Length - 1 ? x - label.DesiredSize.Width : x - (label.DesiredSize.Width / 2);
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, plot.Bottom + 5);
            TraceCanvas.Children.Add(label);
        }
    }

    private void AddSeries(UIElement element)
    {
        TraceCanvas.Children.Add(element);
        _traceSeries.Add(element);
    }

    private void AxisLabel(string text, double x, double y, bool right, string key)
    {
        var label = new TextBlock { Text = text, Foreground = Brush(key), FontSize = 10, FontWeight = FontWeights.SemiBold };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, right ? x - label.DesiredSize.Width : x);
        Canvas.SetTop(label, y - (label.DesiredSize.Height / 2));
        TraceCanvas.Children.Add(label);
    }

    private Border EndDot(Point point, string key)
    {
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brush(key) };
        Canvas.SetLeft(dot, point.X - 4);
        Canvas.SetTop(dot, point.Y - 4);
        return dot;
    }

    private static Shapes.Polyline Polyline(IReadOnlyList<TraceSample> trace, Rect plot, Func<TraceSample, double> y, System.Windows.Media.Brush stroke)
    {
        var line = new Shapes.Polyline
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        var step = plot.Width / Math.Max(1, FanController.TraceLength - 1);
        var offset = plot.Right - (step * (trace.Count - 1));
        for (var i = 0; i < trace.Count; i++)
            line.Points.Add(new Point(offset + (i * step), y(trace[i])));
        return line;
    }

    private void BuildLevelButtons()
    {
        for (var level = 0; level <= Registers.MaxLevel; level++)
        {
            var captured = level;
            var button = new Button
            {
                Content = level.ToString(),
                Width = 40,
                Margin = new Thickness(level == 0 ? 0 : 8, 0, 0, 0),
                Padding = new Thickness(0),
                Style = (Style)FindResource("ModeButton")
            };
            AutomationProperties.SetAutomationId(button, $"ManualLevel{level}");
            button.Click += (_, _) => Controller.Request(ControlMode.Manual, captured);
            LevelHost.Children.Add(button);
        }
    }

    private void BuildCurveChips(LibraryCurve curve)
    {
        CurveHost.Children.Clear();
        CurveHost.Children.Add(CurveChip(0, $"off below {curve.OffBelowCelsius}°", first: true));
        foreach (var step in curve.Steps.OrderBy(step => step.Celsius))
            CurveHost.Children.Add(CurveChip(step.Level, $"{step.Celsius}° → {step.Level}", first: false));
        CurveNote.Text = $"Steps down {curve.HysteresisCelsius}° below each threshold. Level 0 is never held at or above {curve.OffBelowCelsius}°.";
    }

    private Border CurveChip(int level, string text, bool first) => new()
    {
        Style = (Style)FindResource("Surface"),
        Background = Brush("Raised"),
        Height = 28,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(first ? 0 : 6, 0, 0, 0),
        Tag = level,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brush("Muted"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private void BuildTray()
    {
        try
        {
            _tray = new TrayIcon();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        _tray.ShowRequested += () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _tray.ModeRequested += (mode, level) =>
        {
            var update = Controller.Request(mode, mode == ControlMode.Manual ? level : Latest?.ManualLevel ?? 1);
            // A refusal from the menu has no panel to appear on, so it goes to a balloon.
            if (!IsVisible && !string.IsNullOrEmpty(update.Refusal))
                _tray.ShowBalloon(update.Refusal);
        };
        _tray.ExitRequested += () => Quit();
    }

    /// <summary>The tray's startup row. Checked means Windows starts this process, still hidden.</summary>
    internal void AttachLogon(LogonPreference preference)
    {
        if (_tray is null)
            return;
        _tray.SetStartWithWindows(preference.StartWithWindows);
        _tray.StartWithWindowsChanged += enabled =>
        {
            preference.Set(enabled);
            _tray.SetStartWithWindows(preference.StartWithWindows);
        };
    }

    private void ShowElevateHint(string status)
    {
        StatusText.Text = status;
        var restart = new Button
        {
            Content = "Restart as administrator",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Style = (Style)FindResource("ModeButton")
        };
        restart.Click += (_, _) => App.RelaunchElevated();
        restart.Background = Brush("Teal");
        restart.Foreground = Brush("OnAccent");
        restart.BorderBrush = Brush("Teal");
        StatusHost.Children.Add(restart);
    }

    private void Mark(Button button, bool active)
    {
        button.Background = Brush(active ? "Teal" : "Raised");
        button.Foreground = Brush(active ? "OnAccent" : "Ink");
        button.BorderBrush = Brush(active ? "Teal" : "Line");
    }

    private System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);
}
