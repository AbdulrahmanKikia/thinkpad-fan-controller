namespace LibraryFan;

public sealed class SimulatedEc : IEcClient
{
    private readonly Dictionary<byte, byte> _sensors = new();
    private int _hottest = 48;
    private int _failReads;

    public SimulatedEc()
    {
        foreach (var address in Registers.TemperatureAddresses)
            _sensors[address] = 40;
        ApplyHottest();
    }

    public bool IsOpen => true;
    public string Status => "Simulator. No embedded-controller ports are open.";
    public byte? LastCommand { get; private set; }
    public int WriteCount { get; private set; }
    public int PortTouches => 0;

    public int HottestCelsius
    {
        get => _hottest;
        set
        {
            _hottest = Math.Clamp(value, 0, 120);
            ApplyHottest();
        }
    }

    public void FailReads(int count) => _failReads = count;

    public FanReading Read()
    {
        if (_failReads > 0)
        {
            _failReads--;
            return FanReading.Unreadable;
        }

        var level = LastCommand is null or Registers.BiosControl
            ? 3
            : LastCommand.Value;
        var rpm = level == 0 ? 0 : 800 + (level * 450);
        if (LastCommand == Registers.BiosControl)
            rpm = 2100;

        return new FanReading
        {
            Ok = true,
            Fan1Rpm = rpm,
            Fan2Rpm = Math.Max(0, rpm - 80),
            Fan1Live = true,
            Fan2Live = true,
            Sensors = Registers.TemperatureAddresses
                .Select(address => new SensorSample { Address = address, Raw = _sensors[address] })
                .ToArray()
        };
    }

    public bool TryWrite(byte command)
    {
        if (!Registers.IsAllowedCommand(command))
            return false;
        LastCommand = command;
        WriteCount++;
        return true;
    }

    public void RestoreBios() => TryWrite(Registers.BiosControl);

    public void SetRawSensor(byte address, byte raw) => _sensors[address] = raw;

    public void Dispose()
    {
    }

    private void ApplyHottest()
    {
        var primary = Registers.TemperatureAddresses[0];
        foreach (var address in Registers.TemperatureAddresses)
            _sensors[address] = (byte)Math.Clamp(_hottest - 6, 1, 120);
        _sensors[primary] = (byte)Math.Clamp(_hottest, 0, 255);
    }
}
