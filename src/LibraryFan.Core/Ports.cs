namespace LibraryFan;

/// <summary>Byte-wide access to the two embedded-controller ports.</summary>
public interface IPortIo
{
    bool ReadPort(ushort port, out byte value);
    bool WritePort(ushort port, byte value);
}

/// <summary>What the controller needs from a driver, or from the simulator.</summary>
public interface IEcClient : IDisposable
{
    bool IsOpen { get; }
    string Status { get; }
    FanReading Read();
    bool TryWrite(byte command);
    void RestoreBios();
}
