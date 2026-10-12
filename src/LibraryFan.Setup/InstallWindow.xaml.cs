using System.Net.Http;
using System.Windows;

namespace LibraryFan.Setup;

public partial class InstallWindow : Window
{
    private readonly HttpClientHandler _handler = new() { AllowAutoRedirect = false };

    public InstallWindow() => InitializeComponent();

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Downloading LibraryFan-try.zip from GitHub...";
            var bytes = await GitHubRelease.DownloadAsync(_handler);
            var directory = PackageInstall.DefaultAppDirectory();
            PackageInstall.Extract(new MemoryStream(bytes), directory);
            PackageInstall.WriteStartMenuLauncher(PackageInstall.DefaultStartMenuCommand(), directory);
            StatusText.Text = PackageInstall.HasDesktopRuntime()
                ? "Installed. Open ThinkPad Fan Controller Utility from the Start menu. PawnIO from https://pawnio.eu is still required to read the fans."
                : "Installed. Install the .NET 8 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8, then open ThinkPad Fan Controller Utility from the Start menu.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            InstallButton.IsEnabled = true;
        }
    }
}
