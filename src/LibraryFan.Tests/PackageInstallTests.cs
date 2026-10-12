using System.IO.Compression;
using System.Net;
using LibraryFan.Setup;
using Xunit;

namespace LibraryFan.Tests;

public class PackageInstallTests
{
    [Fact]
    public void The_installer_downloads_the_latest_release_asset_from_github()
    {
        Assert.Equal(
            "https://github.com/AbdulrahmanKikia/thinkpad-fan-controller/releases/latest/download/LibraryFan-try.zip",
            GitHubRelease.LatestAsset.AbsoluteUri);
        Assert.True(GitHubRelease.IsAllowed(new Uri("https://objects.githubusercontent.com/package")));
        Assert.True(GitHubRelease.IsAllowed(new Uri("https://release-assets.githubusercontent.com/package")));
        Assert.False(GitHubRelease.IsAllowed(new Uri("http://github.com/AbdulrahmanKikia/library-fan/releases/latest/download/LibraryFan-try.zip")));
        Assert.False(GitHubRelease.IsAllowed(new Uri("https://example.com/LibraryFan-try.zip")));
    }

    [Fact]
    public async Task A_redirect_off_github_is_refused()
    {
        var handler = new ScriptedHandler(_ => Redirect("https://example.com/LibraryFan-try.zip"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubRelease.DownloadAsync(handler));
        Assert.Contains("GitHub", error.Message);
    }

    [Fact]
    public void The_zip_installs_the_dll_package_and_refuses_an_exe()
    {
        var destination = TempDir();
        var refused = Zip(("LibraryFan/LibraryFan.dll", [1]), ("LibraryFan/LibraryFan.exe", [2]));
        var error = Assert.Throws<InvalidOperationException>(() => PackageInstall.Extract(refused, destination));
        Assert.Contains("LibraryFan.exe", error.Message);
        Assert.False(File.Exists(Path.Combine(destination, "LibraryFan.dll")));
        Assert.False(File.Exists(Path.Combine(destination, "LibraryFan.exe")));

        var good = Zip(PackageInstall.Required.Select(name => ($"LibraryFan/{name}", new byte[] { 1 })).ToArray());
        PackageInstall.Extract(good, destination);
        Assert.True(File.Exists(Path.Combine(destination, "LibraryFan.dll")));
        Assert.False(File.Exists(Path.Combine(destination, "LibraryFan.exe")));
        Assert.False(File.Exists(Path.Combine(destination, "hardware-profile.json")));
    }

    [Fact]
    public void A_profile_in_the_zip_is_not_installed_over_the_built_in_map()
    {
        var destination = TempDir();
        var zip = Zip(
            PackageInstall.Required.Select(name => ($"LibraryFan/{name}", new byte[] { 1 }))
                .Append(("LibraryFan/hardware-profile.json", [9]))
                .ToArray());
        PackageInstall.Extract(zip, destination);
        Assert.False(File.Exists(Path.Combine(destination, "hardware-profile.json")));
    }

    [Fact]
    public void The_start_menu_launcher_calls_the_installed_cmd()
    {
        var app = TempDir();
        var menu = Path.Combine(TempDir(), "ThinkPad Fan Controller Utility.cmd");
        PackageInstall.WriteStartMenuLauncher(menu, app);
        var text = File.ReadAllText(menu);
        Assert.Contains(Path.Combine(app, "Open ThinkPad Fan Controller Utility.cmd"), text);
        Assert.DoesNotContain("LibraryFan.exe", text);
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "library-fan-setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = entry.Open();
                writer.Write(bytes);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Redirect)
    {
        Headers = { Location = new Uri(location) }
    };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _next;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> next) => _next = next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_next(request));
    }
}
