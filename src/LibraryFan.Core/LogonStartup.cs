using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LibraryFan;

/// <summary>Writes or removes the current-user Run value that starts ThinkPad Fan Controller Utility at logon.</summary>
public interface ILogonStartup
{
    void Enable(string name, string command);
    void Disable(string name);
    string? Read(string name);
}

public static class LogonStartup
{
    public const string ValueName = "LibraryFan";

    public static void Apply(ILogonStartup store, bool enabled, string command)
    {
        if (enabled)
            store.Enable(ValueName, command);
        else
            store.Disable(ValueName);
    }
}

[SupportedOSPlatform("windows")]
public sealed class RegistryLogonStartup : ILogonStartup
{
    public const string DefaultSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;

    public RegistryLogonStartup(string? subKey = null) =>
        _subKey = string.IsNullOrWhiteSpace(subKey) ? DefaultSubKey : subKey;

    public void Enable(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey);
        key?.SetValue(name, command, RegistryValueKind.String);
    }

    public void Disable(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: false);
        return key?.GetValue(name) as string;
    }
}
