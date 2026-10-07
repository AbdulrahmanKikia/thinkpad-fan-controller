using System.Diagnostics;
using System.Security.Principal;

namespace LibraryFan;

public partial class App : System.Windows.Application
{
    public static bool HeadlessHost { get; set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (HeadlessHost)
            return;
        var selfTest = e.Args.Contains("--self-test");
        var simulate = selfTest || e.Args.Contains("--simulate");
        var window = new MainWindow(CreateClient(simulate), simulate, selfTest);
        MainWindow = window;
        window.Show();
        if (selfTest)
        {
            window.ContentRendered += async (_, _) =>
            {
                var ok = await window.RunSelfTestAsync();
                Shutdown(ok ? 0 : 1);
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

    internal static void RelaunchElevated()
    {
        var host = Environment.ProcessPath ?? "";
        var info = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            FileName = host
        };
        if (System.IO.Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            info.Arguments = $"exec \"{dll}\"";
        }

        Process.Start(info);
        Current.Shutdown();
    }
}
