namespace LibraryFan;

public interface IPortIo
{
    bool ReadPort(ushort port, out byte value);
    bool WritePort(ushort port, byte value);
}

public interface IEcClient : IDisposable
{
    bool IsOpen { get; }
    string Status { get; }
    FanReading Read();
    bool TryWrite(byte command);
    void RestoreBios();
}

public static class ThinkPadEc
{
    public static bool ReadByte(IPortIo ports, byte offset, out byte value)
    {
        value = 0;
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.ControlPort, Registers.CommandRead))
            return false;
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.DataPort, offset))
            return false;
        if (!WaitOutputFull(ports))
            return false;
        return ports.ReadPort(Registers.DataPort, out value);
    }

    public static bool WriteByte(IPortIo ports, byte offset, byte value)
    {
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.ControlPort, Registers.CommandWrite))
            return false;
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.DataPort, offset))
            return false;
        if (!WaitInputClear(ports))
            return false;
        return ports.WritePort(Registers.DataPort, value);
    }

    public static bool TryReadSnapshot(IPortIo ports, out FanReading reading)
    {
        reading = FanReading.Unreadable;
        if (!WriteByte(ports, Registers.FanSelect, Registers.Fan2))
            return false;
        if (!TryReadRpm(ports, out var fan2, out var fan2Live))
            return false;
        if (!WriteByte(ports, Registers.FanSelect, Registers.Fan1))
            return false;
        if (!TryReadRpm(ports, out var fan1, out var fan1Live))
            return false;

        var sensors = new SensorSample[Registers.TemperatureAddresses.Length];
        for (var i = 0; i < Registers.TemperatureAddresses.Length; i++)
        {
            if (!ReadByte(ports, Registers.TemperatureAddresses[i], out var raw))
                return false;
            sensors[i] = new SensorSample
            {
                Address = Registers.TemperatureAddresses[i],
                Raw = raw
            };
        }

        reading = new FanReading
        {
            Ok = true,
            Fan1Rpm = fan1,
            Fan2Rpm = fan2,
            Fan1Live = fan1Live,
            Fan2Live = fan2Live,
            Sensors = sensors
        };
        return true;
    }

    public static bool TryCommandBoth(IPortIo ports, byte command)
    {
        if (!Registers.IsAllowedCommand(command))
            return false;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!WriteFan(ports, Registers.Fan1, command))
                continue;
            if (!WriteFan(ports, Registers.Fan2, command))
                continue;
            if (ReadFanCommand(ports, Registers.Fan1) == command &&
                ReadFanCommand(ports, Registers.Fan2) == command)
                return true;
            Thread.Sleep(30);
        }

        return false;
    }

    private static bool WriteFan(IPortIo ports, byte fan, byte command)
    {
        return WriteByte(ports, Registers.FanSelect, fan) &&
               WriteByte(ports, Registers.FanCommand, command);
    }

    private static byte? ReadFanCommand(IPortIo ports, byte fan)
    {
        if (!WriteByte(ports, Registers.FanSelect, fan))
            return null;
        return ReadByte(ports, Registers.FanCommand, out var value) ? value : null;
    }

    private static bool TryReadRpm(IPortIo ports, out int? rpm, out bool live)
    {
        rpm = null;
        live = false;
        if (!ReadByte(ports, Registers.FanSpeedLow, out var low))
            return false;
        if (!ReadByte(ports, Registers.FanSpeedHigh, out var high))
            return false;
        var combined = (high << 8) | low;
        if (combined > Registers.StaleRpmAbove)
            return true;
        rpm = combined;
        live = true;
        return true;
    }

    private static bool WaitInputClear(IPortIo ports)
    {
        for (var i = 0; i < 40; i++)
        {
            if (!ports.ReadPort(Registers.ControlPort, out var status))
                return false;
            if ((status & Registers.StatusInputFull) == 0)
                return true;
            Thread.Sleep(1);
        }

        return false;
    }

    private static bool WaitOutputFull(IPortIo ports)
    {
        for (var i = 0; i < 40; i++)
        {
            if (!ports.ReadPort(Registers.ControlPort, out var status))
                return false;
            if ((status & Registers.StatusOutputFull) != 0)
                return true;
            Thread.Sleep(1);
        }

        return false;
    }
}
