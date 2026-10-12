using Microsoft.Win32;
using Xunit;

namespace LibraryFan.Tests;

public class LaunchPreferencesTests
{
    [Fact]
    public void A_missing_settings_file_still_needs_setup()
    {
        var preferences = AppPreferences.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        Assert.False(preferences.SetupComplete);
        Assert.False(preferences.StartWithWindows);
    }

    [Fact]
    public void Completing_setup_remembers_the_startup_choice()
    {
        var path = TempSettings();
        try
        {
            AppPreferences.Load(path).Complete(startWithWindows: true).Save(path);
            var loaded = AppPreferences.Load(path);
            Assert.True(loaded.SetupComplete);
            Assert.True(loaded.StartWithWindows);

            loaded.Complete(startWithWindows: false, "lenovo-p1-gen3").Save(path);
            loaded = AppPreferences.Load(path);
            Assert.True(loaded.SetupComplete);
            Assert.False(loaded.StartWithWindows);
            Assert.Equal("lenovo-p1-gen3", loaded.ProfileId);

            loaded.Complete(startWithWindows: true).Save(path);
            loaded = AppPreferences.Load(path);
            Assert.Equal("lenovo-p1-gen3", loaded.ProfileId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unreadable_settings_ask_for_setup_again()
    {
        var path = TempSettings();
        try
        {
            File.WriteAllText(path, "{ not json");
            var preferences = AppPreferences.Load(path);
            Assert.False(preferences.SetupComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Startup_command_uses_dotnet_exec_for_the_dll()
    {
        var command = AppPreferences.StartupCommand(@"C:\Program Files\dotnet\dotnet.exe", @"C:\Apps\ThinkPadFan\LibraryFan.dll");
        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" exec \"C:\\Apps\\ThinkPadFan\\LibraryFan.dll\"",
            command);
    }

    [Fact]
    public void Startup_command_is_the_host_when_it_is_not_dotnet()
    {
        var command = AppPreferences.StartupCommand(@"C:\Apps\ThinkPadFan\LibraryFan.exe", @"C:\Apps\ThinkPadFan\LibraryFan.dll");
        Assert.Equal("\"C:\\Apps\\ThinkPadFan\\LibraryFan.exe\"", command);
    }

    [Fact]
    public void Startup_is_not_registered_until_setup_is_finished()
    {
        var path = TempSettings();
        var subKey = $@"Software\LibraryFan\TestRun-{Guid.NewGuid():N}";
        var store = new RegistryLogonStartup(subKey);
        try
        {
            var preference = new LogonPreference(new AppPreferences(), path, store, "cmd");
            preference.Apply();
            Assert.Null(store.Read(LogonStartup.ValueName));

            preference.Set(startWithWindows: true);
            Assert.Equal("cmd", store.Read(LogonStartup.ValueName));
            var saved = AppPreferences.Load(path);
            Assert.True(saved.SetupComplete);
            Assert.True(saved.StartWithWindows);

            preference.Set(startWithWindows: false);
            Assert.Null(store.Read(LogonStartup.ValueName));
            Assert.False(AppPreferences.Load(path).StartWithWindows);
        }
        finally
        {
            File.Delete(path);
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Logon_registration_follows_the_checkbox()
    {
        var subKey = $@"Software\LibraryFan\TestRun-{Guid.NewGuid():N}";
        var store = new RegistryLogonStartup(subKey);
        try
        {
            LogonStartup.Apply(store, enabled: true, "\"dotnet.exe\" exec \"LibraryFan.dll\"");
            Assert.Equal("\"dotnet.exe\" exec \"LibraryFan.dll\"", store.Read(LogonStartup.ValueName));

            LogonStartup.Apply(store, enabled: false, "\"dotnet.exe\" exec \"LibraryFan.dll\"");
            Assert.Null(store.Read(LogonStartup.ValueName));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    [Theory]
    [InlineData(false, false, false, true, true, false)]
    [InlineData(true, false, false, true, true, true)]
    [InlineData(false, true, false, true, true, true)]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(false, false, false, true, false, true)]
    public void The_panel_stays_hidden_unless_something_must_be_seen(
        bool selfTest, bool snapshot, bool simulate, bool driverOpen, bool hasTray, bool showPanel)
    {
        Assert.Equal(showPanel, LaunchPresentation.ShowPanel(selfTest, snapshot, driverOpen, hasTray));
        Assert.Equal(!simulate && !selfTest && !snapshot, LaunchPresentation.ShowSetup(simulate, selfTest, snapshot, setupComplete: false));
        Assert.False(LaunchPresentation.ShowSetup(simulate, selfTest, snapshot, setupComplete: true));
    }

    private static string TempSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "library-fan-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
