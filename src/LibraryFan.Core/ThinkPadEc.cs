namespace LibraryFan;

/// <summary>The dual-fan protocol from TPFanControl-dual-fan, on top of <see cref="EcBus"/>.</summary>
public static class ThinkPadEc
{
    /// <summary>After this many registers in a row fail to answer, the rest of the snapshot is skipped.</summary>
    public const int DeadAfter = 3;

    public static FanReading ReadSnapshot(IPortIo ports)
    {
        var read = 0;
        var failures = 0;

        var fan2 = ReadRpm(ports, Registers.Fan2, ref read, ref failures);
        var fan1 = ReadRpm(ports, Registers.Fan1, ref read, ref failures);

        var sensors = new SensorSample[Registers.TemperatureAddresses.Length];
        for (var i = 0; i < sensors.Length; i++)
        {
            var address = Registers.TemperatureAddresses[i];
            byte? raw = null;
            if (failures < DeadAfter && EcBus.TryRead(ports, address, out var value))
            {
                raw = value;
                read++;
                failures = 0;
            }
            else
            {
                failures++;
            }

            sensors[i] = new SensorSample { Address = address, Raw = raw };
        }

        return new FanReading
        {
            Ok = read > 0,
            Fan1Rpm = fan1.Rpm,
            Fan2Rpm = fan2.Rpm,
            Fan1Live = fan1.Live,
            Fan2Live = fan2.Live,
            Sensors = sensors,
            RegistersRead = read,
            RegistersTotal = Registers.SnapshotRegisters
        };
    }

    /// <summary>
    /// Write one command to both fans and confirm both read-backs. Only 0x80 and levels 0–7
    /// are accepted. Bit 6, disengaged full speed, is never written.
    /// </summary>
    public static bool TryCommandBoth(IPortIo ports, byte command)
    {
        if (!Registers.IsAllowedCommand(command))
            return false;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (WriteFan(ports, Registers.Fan1, command)
                && WriteFan(ports, Registers.Fan2, command)
                && ReadFanCommand(ports, Registers.Fan1) == command
                && ReadFanCommand(ports, Registers.Fan2) == command)
            {
                return true;
            }

            Thread.Sleep(30);
        }

        return false;
    }

    private static (int? Rpm, bool Live) ReadRpm(IPortIo ports, byte fan, ref int read, ref int failures)
    {
        if (failures >= DeadAfter)
        {
            failures++;
            return (null, false);
        }

        if (!EcBus.TryWrite(ports, Registers.FanSelect, fan)
            || !EcBus.TryRead(ports, Registers.FanSpeedLow, out var low)
            || !EcBus.TryRead(ports, Registers.FanSpeedHigh, out var high))
        {
            failures++;
            return (null, false);
        }

        read++;
        failures = 0;
        var word = (high << 8) | low;
        if (word > Registers.StaleRpmAbove)
            return (null, false);
        return (word, true);
    }

    private static bool WriteFan(IPortIo ports, byte fan, byte command) =>
        EcBus.TryWrite(ports, Registers.FanSelect, fan)
        && EcBus.TryWrite(ports, Registers.FanCommand, command);

    private static byte? ReadFanCommand(IPortIo ports, byte fan)
    {
        if (!EcBus.TryWrite(ports, Registers.FanSelect, fan))
            return null;
        return EcBus.TryRead(ports, Registers.FanCommand, out var value) ? value : null;
    }
}
