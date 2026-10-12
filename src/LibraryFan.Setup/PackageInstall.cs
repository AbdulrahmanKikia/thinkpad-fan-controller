using System.IO.Compression;

namespace LibraryFan.Setup;

/// <summary>
/// Installs the downloaded zip beside a Start menu launcher. It never writes LibraryFan.exe
/// or a hardware-profile.json that would replace the built-in map.
/// </summary>
public static class PackageInstall
{
    public static readonly string[] Required =
    [
        "LibraryFan.dll",
        "LibraryFan.Core.dll",
        "LibraryFan.deps.json",
        "LibraryFan.runtimeconfig.json",
        "library-curve.json",
        "LpcACPIEC.bin",
        "COPYING",
        "hardware-profile.example.json",
        "Open ThinkPad Fan Controller Utility.cmd",
        "Read this first.txt"
    ];

    public static string DefaultAppDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryFan", "App");

    public static string DefaultStartMenuCommand() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "ThinkPad Fan Controller Utility.cmd");

    public static void Extract(Stream zip, string destination)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = FileName(entry.FullName);
            if (name is null)
                continue;
            if (name.Equals("LibraryFan.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The package contains LibraryFan.exe. The app was not installed.");
            if (name.Equals("hardware-profile.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Required.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            using var source = entry.Open();
            using var copy = new MemoryStream();
            source.CopyTo(copy);
            if (copy.Length > GitHubRelease.MaxBytes)
                throw new InvalidOperationException("The GitHub package is the wrong size. The app was not installed.");
            files[name] = copy.ToArray();
        }

        var missing = Required.FirstOrDefault(name => !files.ContainsKey(name));
        if (missing is not null)
            throw new InvalidOperationException($"The GitHub package is missing {missing}. The app was not installed.");

        Directory.CreateDirectory(destination);
        foreach (var (name, bytes) in files)
            File.WriteAllBytes(Path.Combine(destination, name), bytes);
    }

    public static void WriteStartMenuLauncher(string commandPath, string appDirectory)
    {
        var directory = Path.GetDirectoryName(commandPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var target = Path.Combine(appDirectory, "Open ThinkPad Fan Controller Utility.cmd");
        File.WriteAllText(commandPath, $"@echo off\r\ncall \"{target}\" %*\r\n");
    }

    public static bool HasDesktopRuntime()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(root) && Directory.GetDirectories(root).Any(path => Path.GetFileName(path).StartsWith("8.", StringComparison.Ordinal));
    }

    private static string? FileName(string fullName)
    {
        var parts = fullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || fullName.EndsWith('/') || fullName.EndsWith('\\'))
            return null;
        if (parts.Any(part => part is ".." or "."))
            throw new InvalidOperationException("The package contains an unsafe path. The app was not installed.");
        if (parts.Length > 2 || (parts.Length == 2 && !parts[0].Equals("LibraryFan", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The package contains an unsafe path. The app was not installed.");
        return parts[^1];
    }
}
