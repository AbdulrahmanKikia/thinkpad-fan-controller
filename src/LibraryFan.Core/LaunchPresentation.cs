namespace LibraryFan;

/// <summary>Decides which window, if any, a launch is allowed to show.</summary>
public static class LaunchPresentation
{
    /// <summary>
    /// A normal launch stays in the tray. The panel opens for the self-test, a snapshot,
    /// a driver that did not open, or a machine with no tray icon.
    /// </summary>
    public static bool ShowPanel(bool selfTest, bool snapshot, bool driverOpen, bool hasTray) =>
        selfTest || snapshot || !driverOpen || !hasTray;

    /// <summary>
    /// Hardware's first launch asks whether to start with Windows. The simulator, the self-test,
    /// and snapshots skip that question, and it is not asked again once it has been answered.
    /// </summary>
    public static bool ShowSetup(bool simulate, bool selfTest, bool snapshot, bool setupComplete) =>
        !simulate && !selfTest && !snapshot && !setupComplete;
}
