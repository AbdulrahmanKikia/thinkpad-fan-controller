namespace LibraryFan;

/// <summary>
/// Register offsets copied from nikolasgd/TPFanControl-dual-fan, fancontrol/fanstuff.cpp.
/// Ports come from this machine's ECDT (LENOVO TP-N2V): data 0x62, control 0x66.
/// Nothing in here is invented. Confirmed live values are in .cursor/skills/thinkpad-ec/reference.md.
/// </summary>
public static class Registers
{
    public const ushort DataPort = 0x62;
    public const ushort ControlPort = 0x66;

    public const byte StatusOutputFull = 0x01;
    public const byte StatusInputFull = 0x02;

    public const byte CommandRead = 0x80;
    public const byte CommandWrite = 0x81;

    public const byte FanSelect = 0x31;
    public const byte Fan1 = 0x00;
    public const byte Fan2 = 0x01;

    public const byte FanCommand = 0x2F;
    public const byte BiosControl = 0x80;
    public const byte DisengagedFullSpeed = 0x40;
    public const byte MaxLevel = 7;

    public const byte FanSpeedLow = 0x84;
    public const byte FanSpeedHigh = 0x85;
    public const int StaleRpmAbove = 0x1FFF;

    public static readonly byte[] TemperatureAddresses =
    [
        0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
        0xC0, 0xC1, 0xC2, 0xC3
    ];

    /// <summary>Two fan speed words plus every temperature byte.</summary>
    public static int SnapshotRegisters => 2 + TemperatureAddresses.Length;

    public static bool IsMissingTemperature(byte raw) => raw is 0x00 or 0x80;

    public static bool IsLevelCommand(byte command) => command <= MaxLevel;

    public static bool IsAllowedCommand(byte command) =>
        command == BiosControl || IsLevelCommand(command);

    public static string Label(byte address) => $"0x{address:X2}";
}
