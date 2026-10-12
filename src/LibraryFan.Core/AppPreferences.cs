using System.Text.Json;

namespace LibraryFan;

/// <summary>
/// The first-run choice: start with Windows, or only when opened. Either way the panel stays hidden.
/// </summary>
public sealed class AppPreferences
{
    public bool SetupComplete { get; init; }
    public bool StartWithWindows { get; init; }
    public string? ProfileId { get; init; }

    public AppPreferences Complete(bool startWithWindows, string? profileId = null) => new()
    {
        SetupComplete = true,
        StartWithWindows = startWithWindows,
        ProfileId = profileId ?? ProfileId
    };

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryFan", "settings.json");

    public static AppPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppPreferences();
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path), JsonOptions) ?? new AppPreferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppPreferences();
        }
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Command for the logon Run key. <c>dotnet.exe</c> hosts the DLL, because an unsigned
    /// apphost is blocked on this machine. Any other host is started on its own.
    /// </summary>
    public static string StartupCommand(string hostPath, string assemblyPath) =>
        Path.GetFileName(hostPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? $"\"{hostPath}\" exec \"{assemblyPath}\""
            : $"\"{hostPath}\"";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
