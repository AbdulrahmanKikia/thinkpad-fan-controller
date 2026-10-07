using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LibraryFan;

/// <summary>
/// Port access through the signed PawnIO driver and its LpcACPIEC module, which only allows
/// ports 0x62 and 0x66. The calls below are the ones that succeeded on this machine; see
/// .cursor/skills/thinkpad-ec/reference.md.
/// </summary>
public sealed class PawnIoEcClient : IEcClient
{
    public const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    public const string ModuleFile = "LpcACPIEC.bin";
    public const string BusMutex = @"Global\Access_EC";

    private const uint GenericReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private static readonly TimeSpan BusWait = TimeSpan.FromSeconds(2);

    private readonly SafeHandle? _device;
    private readonly PawnPort? _ports;
    private readonly Mutex _bus = new(false, BusMutex);

    private PawnIoEcClient(SafeHandle? device, string status)
    {
        _device = device;
        Status = status;
        if (device is not null)
            _ports = new PawnPort(device);
    }

    public bool IsOpen => _ports is not null;
    public string Status { get; }

    public static PawnIoEcClient Open()
    {
        var modulePath = Path.Combine(AppContext.BaseDirectory, ModuleFile);
        if (!File.Exists(modulePath))
            return new PawnIoEcClient(null, $"The PawnIO EC module is missing. Put {ModuleFile} next to LibraryFan.dll.");

        var device = CreateFile(DevicePath, GenericReadWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (device.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            return new PawnIoEcClient(null, error == 5
                ? "PawnIO refused this process. Start Library Fan as administrator."
                : "PawnIO is not installed. Install the signed driver from https://pawnio.eu, then start Library Fan as administrator.");
        }

        var module = File.ReadAllBytes(modulePath);
        if (!DeviceIoControl(device, Ioctl.Load, module, (uint)module.Length, null, 0, out _, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            return new PawnIoEcClient(null, $"PawnIO could not load the EC module (Win32 error {error}).");
        }

        return new PawnIoEcClient(device,
            "PawnIO is reading the embedded controller. Fan levels stay with firmware until both fans have been read and you choose a mode.");
    }

    public FanReading Read()
    {
        if (_ports is null)
            return FanReading.Unreadable;
        return WithBus(() => ThinkPadEc.ReadSnapshot(_ports), out var reading) ? reading : FanReading.Unreadable;
    }

    public bool TryWrite(byte command)
    {
        if (_ports is null || !Registers.IsAllowedCommand(command))
            return false;
        return WithBus(() => ThinkPadEc.TryCommandBoth(_ports, command), out var wrote) && wrote;
    }

    public void RestoreBios() => TryWrite(Registers.BiosControl);

    public void Dispose()
    {
        _device?.Dispose();
        _bus.Dispose();
    }

    private bool WithBus<T>(Func<T> action, out T value)
    {
        value = default!;
        bool taken;
        try
        {
            taken = _bus.WaitOne(BusWait);
        }
        catch (AbandonedMutexException)
        {
            taken = true;
        }

        if (!taken)
            return false;
        try
        {
            value = action();
            return true;
        }
        finally
        {
            _bus.ReleaseMutex();
        }
    }

    private sealed class PawnPort : IPortIo
    {
        private const int FunctionNameLength = 32;
        private readonly SafeHandle _device;

        public PawnPort(SafeHandle device) => _device = device;

        public bool ReadPort(ushort port, out byte value)
        {
            value = 0;
            if (!Execute("ioctl_pio_read", [port], 1, out var output))
                return false;
            value = (byte)output;
            return true;
        }

        public bool WritePort(ushort port, byte data) =>
            Execute("ioctl_pio_write", [port, data], 0, out _);

        private bool Execute(string name, ulong[] input, int outputWords, out ulong output)
        {
            output = 0;
            var buffer = new byte[FunctionNameLength + (input.Length * sizeof(ulong))];
            var nameBytes = Encoding.ASCII.GetBytes(name);
            Buffer.BlockCopy(nameBytes, 0, buffer, 0, Math.Min(nameBytes.Length, FunctionNameLength - 1));
            if (input.Length > 0)
                Buffer.BlockCopy(input, 0, buffer, FunctionNameLength, input.Length * sizeof(ulong));

            var result = outputWords == 0 ? null : new byte[outputWords * sizeof(ulong)];
            if (!DeviceIoControl(_device, Ioctl.Execute, buffer, (uint)buffer.Length, result, (uint)(result?.Length ?? 0), out var returned, IntPtr.Zero))
                return false;
            if (result is not null && returned >= sizeof(ulong))
                output = BitConverter.ToUInt64(result, 0);
            return true;
        }
    }

    private static class Ioctl
    {
        public const uint Load = 0xA1B22084;
        public const uint Execute = 0xA1B22104;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeHandle device,
        uint controlCode,
        byte[]? inBuffer,
        uint inSize,
        byte[]? outBuffer,
        uint outSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
