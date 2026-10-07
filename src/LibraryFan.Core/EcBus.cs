namespace LibraryFan;

/// <summary>
/// One ACPI embedded-controller transaction at a time on ports 0x62 and 0x66.
/// The firmware's own driver uses the same two ports, so a single byte can time out
/// while that driver is mid-transaction. Each byte is retried on its own; a failed
/// byte never fails a whole snapshot.
/// </summary>
public static class EcBus
{
    public const int Attempts = 3;
    private const int SpinChecks = 40;
    private const int SleepChecks = 12;
    private const int SettleChecks = 6;

    public static bool TryRead(IPortIo ports, byte offset, out byte value)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (ReadOnce(ports, offset, out value))
                return true;
            Thread.Sleep(2);
        }

        value = 0;
        return false;
    }

    public static bool TryWrite(IPortIo ports, byte offset, byte value)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (WriteOnce(ports, offset, value))
                return true;
            Thread.Sleep(2);
        }

        return false;
    }

    private static bool ReadOnce(IPortIo ports, byte offset, out byte value)
    {
        value = 0;
        if (!Settle(ports))
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

    private static bool WriteOnce(IPortIo ports, byte offset, byte value)
    {
        if (!Settle(ports))
            return false;
        if (!ports.WritePort(Registers.ControlPort, Registers.CommandWrite))
            return false;
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.DataPort, offset))
            return false;
        if (!WaitInputClear(ports))
            return false;
        if (!ports.WritePort(Registers.DataPort, value))
            return false;
        return WaitInputClear(ports);
    }

    /// <summary>
    /// Wait for the controller to be idle. A byte sitting in the output buffer belongs to
    /// someone else's transaction; give them a moment to collect it, and only if nobody does,
    /// clear it so it is not mistaken for the answer to the next read.
    /// </summary>
    private static bool Settle(IPortIo ports)
    {
        if (!WaitInputClear(ports))
            return false;
        if (Wait(ports, status => (status & Registers.StatusOutputFull) == 0, SettleChecks))
            return true;
        if (!ports.ReadPort(Registers.DataPort, out _))
            return false;
        return WaitInputClear(ports)
            && Wait(ports, status => (status & Registers.StatusOutputFull) == 0, SettleChecks);
    }

    private static bool WaitInputClear(IPortIo ports) =>
        Wait(ports, status => (status & Registers.StatusInputFull) == 0, SleepChecks);

    private static bool WaitOutputFull(IPortIo ports) =>
        Wait(ports, status => (status & Registers.StatusOutputFull) != 0, SleepChecks);

    private static bool Wait(IPortIo ports, Func<byte, bool> ready, int sleepChecks)
    {
        for (var i = 0; i < SpinChecks + sleepChecks; i++)
        {
            if (!ports.ReadPort(Registers.ControlPort, out var status))
                return false;
            if (ready(status))
                return true;
            if (i >= SpinChecks)
                Thread.Sleep(1);
        }

        return false;
    }
}
