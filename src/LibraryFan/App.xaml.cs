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
        var interactive = !options.Simulate && !options.SelfTest && options.SnapshotPath is null;
        LogonPreference? preference = null;
        string? profileId = null;
        if (interactive)
            preference = PrepareLaunch(out profileId);

        var client = CreateClient(options.Simulate, profileId, out var machineName);
        var window = new MainWindow(client, options.Simulate, options.SelfTest, machineName);
        MainWindow = window;
        // The window is hidden on a normal launch, so the last window closing is not
        // what ends the process. Tray Exit calls Quit, which shuts down explicitly.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (preference is not null)
            window.AttachLogon(preference);

        if (options.SimTemperature is int celsius)
            window.SetSimTemperature(celsius);
        if (options.SimMode is ControlMode mode)
            window.Controller.Request(mode, window.Latest?.ManualLevel ?? 1);

        var show = LaunchPresentation.ShowPanel(
            options.SelfTest,
            options.SnapshotPath is not null,
            client.IsOpen,
            window.HasTray);
        if (show)
            window.Show();
        else
            window.AnnounceInTray();

        if (options.SelfTest)
        {
            window.ContentRendered += async (_, _) =>
            {
                var ok = await SelfTest.RunAsync(window);
                window.Quit(ok ? 0 : 1);
            };
        }
        else if (options.SnapshotPath is string path)
        {
            window.ContentRendered += async (_, _) =>
            {
                await window.WaitForUpdateAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(TimeSpan.FromSeconds(options.SnapshotAfterSeconds));
                WindowSnapshot.Save(window, path);
                window.Quit();
            };
        }
    }

    /// <summary>
    /// First hardware launch asks which computer to command and whether to start with Windows.
    /// Later launches only refresh the Run key so a moved DLL is still what logon starts.
    /// The panel is not shown here.
    /// </summary>
    private static LogonPreference PrepareLaunch(out string? profileId)
    {
        var path = AppPreferences.DefaultPath();
        var preferences = AppPreferences.Load(path);
        var preference = new LogonPreference(
            preferences,
            path,
            new RegistryLogonStartup(),
            AppPreferences.StartupCommand(Environment.ProcessPath ?? "dotnet", typeof(App).Assembly.Location));

        if (!preferences.SetupComplete || string.IsNullOrWhiteSpace(preferences.ProfileId))
        {
            var setup = new SetupWindow(preferences.StartWithWindows, preferences.ProfileId, HardwareProfile.ReadMachine());
            setup.ShowDialog();
            preference.Set(setup.StartWithWindows, setup.ProfileId);
        }
        else
        {
            preference.Apply();
        }

        profileId = preference.ProfileId;
        return preference;
    }

    private static IEcClient CreateClient(bool simulate, string? profileId, out string machineName)
    {
        machineName = EcMap.BuiltIn.Name;
        if (simulate)
            return new SimulatedEc();

        var choice = HardwareProfile.Choose(
            HardwareProfile.ReadMachine(),
            HardwareProfile.BesideApp(),
            HardwareProfile.DefaultPath(),
            profileId);
        if (choice.Map is null)
            return new UnavailableEc(choice.Problem ?? "This machine has no hardware profile, so the fans stay with firmware.");

        machineName = choice.Map.Name;
        if (!IsAdministrator())
            return new UnavailableEc("Restart as administrator to read the fans.", needsAdministrator: true);
        return PawnIoEcClient.Open(choice.Map);
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
