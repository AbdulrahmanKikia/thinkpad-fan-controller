namespace LibraryFan;

public sealed class SensorSample
{
    public required byte Address { get; init; }

    /// <summary>The byte the register returned, or null when the register did not answer this poll.</summary>
    public byte? Raw { get; init; }

    public bool WasRead => Raw is not null;

    public bool IsValid => Raw is byte raw && !Registers.IsMissingTemperature(raw);

    public int? Celsius => IsValid ? Raw : null;

    public string Label => Registers.Label(Address);
}

public sealed class FanReading
{
    public static FanReading Unreadable { get; } = new()
    {
        Ok = false,
        RegistersTotal = Registers.SnapshotRegisters
    };

    /// <summary>True when at least one register answered.</summary>
    public required bool Ok { get; init; }

    public int? Fan1Rpm { get; init; }
    public int? Fan2Rpm { get; init; }
    public bool Fan1Live { get; init; }
    public bool Fan2Live { get; init; }
    public SensorSample[] Sensors { get; init; } = [];
    public int RegistersRead { get; init; }
    public int RegistersTotal { get; init; } = Registers.SnapshotRegisters;

    public IEnumerable<SensorSample> ValidSensors => Sensors.Where(sensor => sensor.IsValid);

    public SensorSample? Hottest =>
        ValidSensors.OrderByDescending(sensor => sensor.Celsius).FirstOrDefault();

    public bool IsDangerous =>
        ValidSensors.Any(sensor => sensor.Celsius >= FanSession.DangerCelsius);
}
