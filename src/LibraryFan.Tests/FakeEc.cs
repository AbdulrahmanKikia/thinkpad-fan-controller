namespace LibraryFan.Tests;

/// <summary>
/// A small embedded controller behind the two ports: fan select, per-fan command and RPM,
/// temperature bytes, and knobs for the ways the real one misbehaves.
/// </summary>
public sealed class FakeEc : IPortIo
{
    public byte[] Ram { get; } = new byte[256];
    public byte[] FanCommands { get; } = [Registers.BiosControl, Registers.BiosControl];
    public int Fan1Rpm { get; set; } = 1000;
    public int Fan2Rpm { get; set; } = 1000;
    public HashSet<byte> DeadOffsets { get; } = new();
    public int FailNextReads { get; set; }
    public int DropWrites { get; set; }
    public bool Dead { get; set; }
    public List<string> Ops { get; } = new();

    private byte _status;
    private byte _pending;
    private byte _offset;
    private int _phase;

    public FakeEc()
    {
        foreach (var address in Registers.TemperatureAddresses)
            Ram[address] = 45;
    }

    public int Selected => Ram[Registers.FanSelect] == Registers.Fan2 ? 1 : 0;

    public void SetTemperature(byte address, byte raw) => Ram[address] = raw;

    public bool ReadPort(ushort port, out byte value)
    {
        if (port == Registers.ControlPort)
        {
            value = _status;
            return true;
        }

        value = _pending;
        _status &= unchecked((byte)~Registers.StatusOutputFull);
        Ops.Add($"in {value:X2}");
        return true;
    }

    public bool WritePort(ushort port, byte value)
    {
        if (port == Registers.ControlPort)
        {
            Ops.Add($"cmd {value:X2}");
            _phase = value switch
            {
                Registers.CommandRead => 1,
                Registers.CommandWrite => 2,
                _ => 0
            };
            return true;
        }

        Ops.Add($"out {value:X2}");
        switch (_phase)
        {
            case 1:
                _phase = 0;
                if (Dead || DeadOffsets.Contains(value))
                    return true;
                if (FailNextReads > 0)
                {
                    FailNextReads--;
                    return true;
                }

                _pending = ValueAt(value);
                _status |= Registers.StatusOutputFull;
                return true;
            case 2:
                _offset = value;
                _phase = 3;
                return true;
            case 3:
                _phase = 0;
                if (Dead)
                    return true;
                if (DropWrites > 0)
                {
                    DropWrites--;
                    return true;
                }

                Store(_offset, value);
                return true;
            default:
                return true;
        }
    }

    private byte ValueAt(byte offset) => offset switch
    {
        Registers.FanSpeedLow => (byte)(SelectedRpm & 0xFF),
        Registers.FanSpeedHigh => (byte)((SelectedRpm >> 8) & 0xFF),
        Registers.FanCommand => FanCommands[Selected],
        _ => Ram[offset]
    };

    private void Store(byte offset, byte value)
    {
        if (offset == Registers.FanCommand)
            FanCommands[Selected] = value;
        else
            Ram[offset] = value;
    }

    private int SelectedRpm => Selected == 1 ? Fan2Rpm : Fan1Rpm;
}

public static class Samples
{
    public static FanReading Sample(int temperature, bool liveFans = true)
    {
        var sensors = CoolSensors(temperature);
        sensors[0] = new SensorSample { Address = 0x78, Raw = (byte)temperature };
        return Reading(sensors, liveFans);
    }

    public static SensorSample[] CoolSensors(int temperature) =>
        Registers.TemperatureAddresses
            .Select(address => new SensorSample
            {
                Address = address,
                Raw = (byte)Math.Clamp(temperature - 5, 1, 79)
            })
            .ToArray();

    public static FanReading Reading(SensorSample[] sensors, bool liveFans = true) => new()
    {
        Ok = true,
        Fan1Rpm = liveFans ? 1200 : null,
        Fan2Rpm = liveFans ? 1100 : null,
        Fan1Live = liveFans,
        Fan2Live = liveFans,
        Sensors = sensors,
        RegistersRead = Registers.SnapshotRegisters,
        RegistersTotal = Registers.SnapshotRegisters
    };
}
