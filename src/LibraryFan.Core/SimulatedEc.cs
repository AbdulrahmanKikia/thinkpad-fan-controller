namespace LibraryFan;

/// <summary>A fake controller for <c>--simulate</c>. It never opens a port.</summary>
public sealed class SimulatedEc : IEcClient
{
    private readonly Dictionary<byte, byte> _sensors = new();
    private int _hottest = 48;
    private int _failReads;
    private int _dropRpm;
    private int _reads;

    public SimulatedEc()
    {
        foreach (var address in Registers.TemperatureAddresses)
            _sensors[address] = 40;
        ApplyHottest();
    }

    public bool IsOpen => true;
    public string Status => "Practice mode.";
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

    public void FailReads(int polls) => _failReads = polls;

    public void DropRpm(int polls) => _dropRpm = polls;

    public void SetRawSensor(byte address, byte raw) => _sensors[address] = raw;

    public FanReading Read()
    {
        if (_failReads > 0)
        {
            _failReads--;
            return FanReading.Unreadable;
        }

        var sensors = Registers.TemperatureAddresses
            .Select(address => new SensorSample { Address = address, Raw = _sensors[address] })
            .ToArray();

        if (_dropRpm > 0)
        {
            _dropRpm--;
            return new FanReading
            {
                Ok = true,
                Sensors = sensors,
                RegistersRead = sensors.Length,
                RegistersTotal = Registers.SnapshotRegisters
            };
        }

        var level = LastCommand is null or Registers.BiosControl ? 3 : LastCommand.Value;
        var rpm = level == 0 ? 0 : 800 + (level * 450);
        if (LastCommand == Registers.BiosControl)
            rpm = 2100;
        // A real tachometer wanders by a few tens of RPM between polls; so does the fake one.
        if (rpm > 0)
            rpm += (int)Math.Round(30 * Math.Sin(_reads++ * 0.7));

        return new FanReading
        {
            Ok = true,
            Fan1Rpm = rpm,
            Fan2Rpm = Math.Max(0, rpm - 80),
            Fan1Live = true,
            Fan2Live = true,
            Sensors = sensors,
            RegistersRead = Registers.SnapshotRegisters,
            RegistersTotal = Registers.SnapshotRegisters
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

    public void Dispose()
    {
    }

    private void ApplyHottest()
    {
        foreach (var address in Registers.TemperatureAddresses)
            _sensors[address] = (byte)Math.Clamp(_hottest - 6, 1, 120);
        _sensors[0x78] = (byte)_hottest;
    }
}

/// <summary>Stands in for the driver when it is missing or the process is not elevated.</summary>
public sealed class UnavailableEc : IEcClient
{
    public UnavailableEc(string status, bool needsAdministrator = false)
    {
        Status = status;
        NeedsAdministrator = needsAdministrator;
    }

    public bool IsOpen => false;
    public bool NeedsAdministrator { get; }
    public string Status { get; }

    public FanReading Read() => FanReading.Unreadable;

    public bool TryWrite(byte command) => false;

    public void RestoreBios()
    {
    }

    public void Dispose()
    {
    }
}
