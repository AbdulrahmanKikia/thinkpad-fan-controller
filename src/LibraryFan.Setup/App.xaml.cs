using System.Windows;

namespace LibraryFan.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (HeadlessInstall.Try(e.Args, out var code))
            Environment.Exit(code);

        var window = new InstallWindow();
        MainWindow = window;
        window.Show();
    }
}
