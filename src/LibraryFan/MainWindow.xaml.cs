using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Media = System.Windows.Media;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LibraryFan;

public partial class MainWindow : Window
{
    private readonly FanController _controller;
    private readonly bool _simulate;
    private readonly List<string> _log = new();
    private FanUpdate? _latest;
    private Forms.NotifyIcon? _tray;

    public MainWindow(IEcClient client, bool simulate, bool selfTest)
    {
        InitializeComponent();
        _simulate = simulate;
        _controller = new FanController(client);
        _controller.Updated += OnUpdated;
        SimPanel.Visibility = simulate ? Visibility.Visible : Visibility.Collapsed;
        SimTemp.ValueChanged += (_, _) =>
        {
            SimTempLabel.Text = $"{(int)SimTemp.Value}°";
            _controller.SetSimTemperature((int)SimTemp.Value);
        };
        SimTempLabel.Text = $"{(int)SimTemp.Value}°";
        BuildLevelButtons();
        try
        {
            BuildTray();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        SystemEvents.SessionEnding += (_, _) => _controller.RestoreBios();
        SystemEvents.PowerModeChanged += (_, args) =>
        {
            if (args.Mode == PowerModes.Suspend)
                _controller.RestoreBios();
        };
        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _controller.Dispose();
        };
        if (!selfTest && !simulate && client is UnavailableEc)
            ShowElevateHint(client.Status);
        TraceCanvas.SizeChanged += (_, _) =>
        {
            if (_latest is not null)
                PaintTrace(_latest.Trace);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var area = SystemParameters.WorkArea;
        if (Width > area.Width - 24)
            Width = Math.Max(MinWidth, area.Width - 24);
        if (Height > area.Height - 24)
            Height = Math.Max(MinHeight, area.Height - 24);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public async Task<bool> RunSelfTestAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_latest is null && DateTime.UtcNow < deadline)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (_latest is null)
                await Task.Delay(30);
        }

        if (_latest is null)
            return Fail("The window never received a fan update.");

        Click(LibraryButton);
        SetTemperature(64);
        if (!AwaitReason("Library is holding level 2 because 0x78 is 64°C."))
            return Fail($"Library at 64°C said '{_latest?.Reason}'.");

        SetTemperature(80);
        if (!AwaitReason("Handed control back to the machine because a sensor reached 80°C."))
            return Fail($"Library handoff said '{_latest?.Reason}'.");

        Click(LibraryButton);
        if (_latest?.Reason != "Handed control back to the machine because a sensor reached 80°C.")
            return Fail("Library resumed while the sensor was still at 80°C.");
        if (!(_latest?.Refusal?.Contains("stays in control", StringComparison.Ordinal) ?? false))
            return Fail("The refused handoff did not explain itself.");

        SetTemperature(60);
        Click(LibraryButton);
        if (!(_latest?.Reason.Contains("Library is holding level", StringComparison.Ordinal) ?? false))
            return Fail($"Library did not resume after cooling: '{_latest?.Reason}'.");

        Click(LevelButton(1));
        if (_latest?.Reason != "Manual is holding level 1.")
            return Fail($"Manual said '{_latest?.Reason}'.");

        SetTemperature(80);
        if (!AwaitReason("Handed control back to the machine because a sensor reached 80°C."))
            return Fail($"Manual handoff said '{_latest?.Reason}'.");

        SetTemperature(50);
        Click(BiosButton);
        if (_latest?.Reason != "BIOS is in control.")
            return Fail($"BIOS said '{_latest?.Reason}'.");

        _log.Add("self-test passed");
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), string.Join(Environment.NewLine, _log));
        }
        catch (IOException)
        {
        }

        return true;
    }

    private void BiosClick(object sender, RoutedEventArgs e) => _controller.Request(ControlMode.Bios);

    private void LibraryClick(object sender, RoutedEventArgs e) => _controller.Request(ControlMode.Library);

    private void ManualClick(object sender, RoutedEventArgs e) => _controller.Request(ControlMode.Manual, _latest?.ManualLevel ?? 1);

    private void OnUpdated(FanUpdate update)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnUpdated(update));
            return;
        }

        _latest = update;
        ReasonText.Text = update.Reason;
        RefusalText.Text = update.Refusal ?? "";
        RefusalText.Visibility = string.IsNullOrEmpty(update.Refusal) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = update.DriverStatus;
        Fan1.Show(update.Fan1Rpm, "Fan 1", update.CommandLabel);
        Fan2.Show(update.Fan2Rpm, "Fan 2", update.CommandLabel);
        var accent = update.IsLatched
            ? (Media.Brush)FindResource("Coral")
            : update.Mode == ControlMode.Bios || (update.Fan1Rpm ?? 0) < 1600
                ? (Media.Brush)FindResource("Teal")
                : (Media.Brush)FindResource("Amber");
        Fan1.SetAccent(accent);
        Fan2.SetAccent(accent);
        PaintMode(update);
        PaintSensors(update.Sensors);
        PaintTrace(update.Trace);
        if (_tray is not null)
            _tray.Text = TrimTooltip(update.Reason);
    }

    private void PaintMode(FanUpdate update)
    {
        var label = update.IsLatched ? "HANDED BACK" : update.Mode switch
        {
            ControlMode.Library => "LIBRARY",
            ControlMode.Manual => "MANUAL",
            _ => "BIOS"
        };
        ModePillText.Text = label;
        var hot = update.IsLatched;
        ModePill.Background = hot ? (Media.Brush)FindResource("Coral") : (Media.Brush)FindResource("Teal");
        ModePillText.Foreground = hot ? Media.Brushes.White : (Media.Brush)FindResource("OnAccent");
        Mark(BiosButton, update.Mode == ControlMode.Bios && !update.IsLatched);
        Mark(LibraryButton, update.Mode == ControlMode.Library);
        Mark(ManualButton, update.Mode == ControlMode.Manual);
        for (var level = 0; level <= 7; level++)
            Mark(LevelButton(level), update.Mode == ControlMode.Manual && update.ManualLevel == level);
    }

    private void PaintSensors(SensorSample[] sensors)
    {
        SensorHost.Children.Clear();
        var hottest = sensors.Where(sensor => sensor.IsValid).MaxBy(sensor => sensor.Celsius);
        foreach (var sensor in sensors)
        {
            var danger = sensor.IsValid && sensor.Celsius >= FanSession.DangerCelsius;
            var chip = new Border
            {
                Style = (Style)FindResource("Surface"),
                Background = (Media.Brush)FindResource("Raised"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(4),
                MinHeight = 56
            };
            if (danger)
                chip.BorderBrush = (Media.Brush)FindResource("Coral");
            else if (hottest is not null && sensor.Address == hottest.Address)
                chip.BorderBrush = (Media.Brush)FindResource("Teal");

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = sensor.Label,
                Foreground = (Media.Brush)FindResource("Muted"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = sensor.IsValid ? $"{sensor.Celsius}°" : "—",
                Foreground = (Media.Brush)FindResource("Ink"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            chip.Child = stack;
            SensorHost.Children.Add(chip);
        }
    }

    private void PaintTrace(IReadOnlyList<TraceSample> trace)
    {
        TraceCanvas.Children.Clear();
        TracePlaceholder.Visibility = trace.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (trace.Count < 2)
            return;
        var width = TraceCanvas.ActualWidth;
        var height = TraceCanvas.ActualHeight;
        if (width < 20 || height < 20)
            return;

        var grid = (Media.Brush)FindResource("Line");
        for (var band = 1; band <= 3; band++)
        {
            var y = height * band / 4;
            TraceCanvas.Children.Add(new Shapes.Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = grid,
                StrokeThickness = 1,
                Opacity = 0.85
            });
        }

        var maxRpm = Math.Max(1000, trace.Max(sample => sample.Rpm));
        var maxCelsius = Math.Max(80, trace.Max(sample => sample.Celsius));
        TraceCanvas.Children.Add(Line(trace, width, height, sample => sample.Celsius / maxCelsius, (Media.Brush)FindResource("Teal")));
        TraceCanvas.Children.Add(Line(trace, width, height, sample => sample.Rpm / maxRpm, (Media.Brush)FindResource("Amber")));
    }

    private static Shapes.Polyline Line(IReadOnlyList<TraceSample> trace, double width, double height, Func<TraceSample, double> pick, Media.Brush stroke)
    {
        var line = new Shapes.Polyline
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = Media.PenLineJoin.Round,
            StrokeStartLineCap = Media.PenLineCap.Round,
            StrokeEndLineCap = Media.PenLineCap.Round
        };
        var step = width / Math.Max(1, trace.Count - 1);
        for (var i = 0; i < trace.Count; i++)
        {
            var y = height - 8 - (pick(trace[i]) * (height - 16));
            line.Points.Add(new System.Windows.Point(i * step, y));
        }

        return line;
    }

    private void BuildLevelButtons()
    {
        for (var level = 0; level <= 7; level++)
        {
            var captured = level;
            var button = new System.Windows.Controls.Button
            {
                Content = level.ToString(),
                Width = 40,
                Margin = new Thickness(level == 0 ? 0 : 8, 0, 0, 0),
                Style = (Style)FindResource("ModeButton"),
                Padding = new Thickness(0)
            };
            button.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, $"ManualLevel{level}");
            button.Click += (_, _) => _controller.Request(ControlMode.Manual, captured);
            LevelHost.Children.Add(button);
        }
    }

    private System.Windows.Controls.Button LevelButton(int level) => (System.Windows.Controls.Button)LevelHost.Children[level];

    private void BuildTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Library Fan"
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
        menu.Items.Add("BIOS", null, (_, _) => _controller.Request(ControlMode.Bios));
        menu.Items.Add("Library", null, (_, _) => _controller.Request(ControlMode.Library));
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
    }

    private void ShowElevateHint(string status)
    {
        StatusText.Text = status;
        var restart = new System.Windows.Controls.Button
        {
            Content = "Restart as administrator",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Style = (Style)FindResource("ModeButton")
        };
        restart.Click += (_, _) => App.RelaunchElevated();
        StatusHost.Children.Add(restart);
    }

    private void Mark(System.Windows.Controls.Button button, bool active)
    {
        button.Background = (Media.Brush)FindResource(active ? "Teal" : "Raised");
        button.Foreground = (Media.Brush)FindResource(active ? "OnAccent" : "Ink");
        button.BorderBrush = (Media.Brush)FindResource(active ? "Teal" : "Line");
    }

    private static void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

    private void SetTemperature(int celsius) => SimTemp.Value = celsius;

    private bool AwaitReason(string expected) => _latest?.Reason == expected;

    private bool Fail(string message)
    {
        _log.Add(message);
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), string.Join(Environment.NewLine, _log));
        }
        catch (IOException)
        {
        }

        ReasonText.Text = message;
        return false;
    }

    private static string TrimTooltip(string reason) =>
        reason.Length <= 63 ? reason : reason[..60] + "...";
}
