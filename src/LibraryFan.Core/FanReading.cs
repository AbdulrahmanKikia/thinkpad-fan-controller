namespace LibraryFan;

public sealed class SensorSample
{
    public required byte Address { get; init; }
    public required byte Raw { get; init; }

    public bool IsValid => !Registers.IsMissingTemperature(Raw);

    public int? Celsius => IsValid ? Raw : null;

    public string Label => $"0x{Address:X2}";
}

public sealed class FanReading
{
    public static FanReading Unreadable { get; } = new()
    {
        Ok = false,
        Sensors = []
    };

    public required bool Ok { get; init; }
    public int? Fan1Rpm { get; init; }
    public int? Fan2Rpm { get; init; }
    public bool Fan1Live { get; init; }
    public bool Fan2Live { get; init; }
    public SensorSample[] Sensors { get; init; } = [];

    public IEnumerable<SensorSample> ValidSensors => Sensors.Where(sensor => sensor.IsValid);

    public SensorSample? Hottest =>
        ValidSensors.OrderByDescending(sensor => sensor.Celsius).FirstOrDefault();

    public bool IsDangerous =>
        ValidSensors.Any(sensor => sensor.Celsius >= FanSession.DangerCelsius);
}
