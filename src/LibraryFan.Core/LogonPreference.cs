namespace LibraryFan;

/// <summary>
/// Saves the startup choice and keeps the logon Run value in step with it.
/// Nothing is registered until setup has been completed.
/// </summary>
public sealed class LogonPreference
{
    private readonly string _path;
    private readonly ILogonStartup _startup;
    private readonly string _command;
    private AppPreferences _preferences;

    public LogonPreference(AppPreferences preferences, string path, ILogonStartup startup, string command)
    {
        _preferences = preferences;
        _path = path;
        _startup = startup;
        _command = command;
    }

    public bool StartWithWindows => _preferences.StartWithWindows;
    public string? ProfileId => _preferences.ProfileId;

    public void Apply()
    {
        if (!_preferences.SetupComplete)
            return;
        LogonStartup.Apply(_startup, _preferences.StartWithWindows, _command);
    }

    public void Set(bool startWithWindows, string? profileId = null)
    {
        var next = _preferences.Complete(startWithWindows, profileId ?? _preferences.ProfileId);
        next.Save(_path);
        _preferences = next;
        LogonStartup.Apply(_startup, startWithWindows, _command);
    }
}
