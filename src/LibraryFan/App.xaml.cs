using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace LibraryFan;

/// <summary>
/// Command line. <c>--sim-temp</c> and <c>--mode</c> only act with <c>--simulate</c>; on hardware the
/// app always starts in BIOS and never takes over until a mode is chosen in the window.
/// </summary>
public sealed record LaunchOptions(
    bool Simulate,
    bool SelfTest,
    string? SnapshotPath,
    double SnapshotAfterSeconds = 0.5,
    int? SimTemperature = null,
    ControlMode? SimMode = null)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var simulate = false;
        var selfTest = false;
        string? snapshot = null;
        var after = 0.5;
        int? simTemperature = null;
        ControlMode? simMode = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--simulate":
                    simulate = true;
                    break;
                case "--self-test":
                    selfTest = true;
                    simulate = true;
                    break;
                case "--snapshot" when i + 1 < args.Count:
                    snapshot = args[++i];
                    break;
                case "--snapshot-after" when i + 1 < args.Count && TryNumber(args[i + 1], out var seconds):
                    after = Math.Clamp(seconds, 0, 180);
                    i++;
                    break;
                case "--sim-temp" when i + 1 < args.Count && TryNumber(args[i + 1], out var celsius):
                    simTemperature = (int)Math.Clamp(celsius, 1, 120);
                    i++;
                    break;
                case "--mode" when i + 1 < args.Count && Enum.TryParse<ControlMode>(args[i + 1], ignoreCase: true, out var mode):
                    simMode = mode;
                    i++;
                    break;
            }
        }

        return new LaunchOptions(
            simulate,
            selfTest,
            snapshot,
            after,
            simulate ? simTemperature : null,
            simulate ? simMode : null);
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
}

public partial class App : Application
{
    /// <summary>Set by the window test so OnStartup does not open a second window.</summary>
    public static bool HeadlessHost { get; set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (HeadlessHost)
            return;

        var options = LaunchOptions.Parse(e.Args);
        var window = new MainWindow(CreateClient(options.Simulate), options.Simulate, options.SelfTest);
        MainWindow = window;
        window.Show();

        if (options.SimTemperature is int celsius)
            window.SetSimTemperature(celsius);
        if (options.SimMode is ControlMode mode)
            window.Controller.Request(mode, window.Latest?.ManualLevel ?? 1);

        if (options.SelfTest)
        {
            window.ContentRendered += async (_, _) =>
            {
                var ok = await SelfTest.RunAsync(window);
                Shutdown(ok ? 0 : 1);
            };
        }
        else if (options.SnapshotPath is string path)
        {
            window.ContentRendered += async (_, _) =>
            {
                await window.WaitForUpdateAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(TimeSpan.FromSeconds(options.SnapshotAfterSeconds));
                WindowSnapshot.Save(window, path);
                Shutdown(0);
            };
        }
    }

    private static IEcClient CreateClient(bool simulate)
    {
        if (simulate)
            return new SimulatedEc();
        if (!IsAdministrator())
            return new UnavailableEc("Restart as administrator to read the fans.");
        return PawnIoEcClient.Open();
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunch through the signed dotnet.exe host. The DLL has no apphost of its own, and an
    /// unsigned one would be blocked by Smart App Control on this machine.
    /// </summary>
    internal static void RelaunchElevated()
    {
        var host = Environment.ProcessPath ?? "dotnet";
        var dll = typeof(App).Assembly.Location;
        var info = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            FileName = host,
            Arguments = System.IO.Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? $"exec \"{dll}\""
                : ""
        };

        try
        {
            Process.Start(info);
            Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The UAC prompt was dismissed. Stay open without hardware access.
        }
    }
}
