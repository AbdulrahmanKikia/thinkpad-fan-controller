namespace LibraryFan;

/// <summary>The dual-fan protocol from TPFanControl-dual-fan, on top of <see cref="EcBus"/>.</summary>
public static class ThinkPadEc
{
    /// <summary>After this many registers in a row fail to answer, the rest of the snapshot is skipped.</summary>
    public const int DeadAfter = 3;

    public static FanReading ReadSnapshot(IPortIo ports) => ReadSnapshot(ports, EcMap.BuiltIn);

    public static FanReading ReadSnapshot(IPortIo ports, EcMap map)
    {
        var read = 0;
        var failures = 0;
        int? fan1Rpm = null;
        int? fan2Rpm = null;
        var fan1Live = false;
        var fan2Live = false;

        for (var i = map.Fans.Length - 1; i >= 0; i--)
        {
            var sample = ReadRpm(ports, map, map.Fans[i], ref read, ref failures);
            if (i == 0)
            {
                fan1Rpm = sample.Rpm;
                fan1Live = sample.Live;
            }
            else
            {
                fan2Rpm = sample.Rpm;
                fan2Live = sample.Live;
            }
        }

        var sensors = new SensorSample[map.Temperatures.Length];
        for (var i = 0; i < sensors.Length; i++)
        {
            var address = map.Temperatures[i];
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
            Fan1Rpm = fan1Rpm,
            Fan2Rpm = fan2Rpm,
            Fan1Live = fan1Live,
            Fan2Live = fan2Live,
            Sensors = sensors,
            RegistersRead = read,
            RegistersTotal = map.SnapshotRegisters
        };
    }

    /// <summary>
    /// Write one command to every fan in the map and confirm the read-backs. Only 0x80 and
    /// levels 0–7 are accepted. Bit 6, disengaged full speed, is never written.
    /// </summary>
    public static bool TryCommandBoth(IPortIo ports, byte command) =>
        TryCommandBoth(ports, command, EcMap.BuiltIn);

    public static bool TryCommandBoth(IPortIo ports, byte command, EcMap map)
    {
        if (!Registers.IsAllowedCommand(command))
            return false;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrote = true;
            foreach (var fan in map.Fans)
                wrote = wrote && WriteFan(ports, map, fan, command);

            var confirmed = wrote;
            foreach (var fan in map.Fans)
                confirmed = confirmed && ReadFanCommand(ports, map, fan) == command;

            if (confirmed)
                return true;

            Thread.Sleep(30);
        }

        return false;
    }

    private static (int? Rpm, bool Live) ReadRpm(IPortIo ports, EcMap map, byte fan, ref int read, ref int failures)
    {
        if (failures >= DeadAfter)
        {
            failures++;
            return (null, false);
        }

        if (!EcBus.TryWrite(ports, map.FanSelect, fan)
            || !EcBus.TryRead(ports, map.SpeedLow, out var low)
            || !EcBus.TryRead(ports, map.SpeedHigh, out var high))
        {
            failures++;
            return (null, false);
        }

        read++;
        failures = 0;
        var word = (high << 8) | low;
        if (word > map.StaleRpmAbove)
            return (null, false);
        return (word, true);
    }

    private static bool WriteFan(IPortIo ports, EcMap map, byte fan, byte command) =>
        EcBus.TryWrite(ports, map.FanSelect, fan)
        && EcBus.TryWrite(ports, map.FanCommand, command);

    private static byte? ReadFanCommand(IPortIo ports, EcMap map, byte fan)
    {
        if (!EcBus.TryWrite(ports, map.FanSelect, fan))
            return null;
        return EcBus.TryRead(ports, map.FanCommand, out var value) ? value : null;
    }
}
