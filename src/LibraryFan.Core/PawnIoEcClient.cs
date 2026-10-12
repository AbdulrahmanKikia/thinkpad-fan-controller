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
    private readonly EcMap _map;
    private readonly Mutex _bus = new(false, BusMutex);

    private PawnIoEcClient(SafeHandle? device, string status, EcMap map)
    {
        _device = device;
        _map = map;
        Status = status;
        if (device is not null)
            _ports = new PawnPort(device);
    }

    public bool IsOpen => _ports is not null;
    public string Status { get; }

    public static PawnIoEcClient Open(EcMap? map = null)
    {
        map ??= EcMap.BuiltIn;
        var modulePath = Path.Combine(AppContext.BaseDirectory, ModuleFile);
        if (!File.Exists(modulePath))
            return new PawnIoEcClient(null, $"The PawnIO EC module is missing. Put {ModuleFile} next to LibraryFan.dll.", map);

        var device = CreateFile(DevicePath, GenericReadWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (device.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            return new PawnIoEcClient(null, error == 5
                ? "PawnIO refused this process. Start ThinkPad Fan Controller Utility as administrator."
                : "PawnIO is not installed. Install the signed driver from https://pawnio.eu, then start ThinkPad Fan Controller Utility as administrator.", map);
        }

        var module = File.ReadAllBytes(modulePath);
        if (!DeviceIoControl(device, Ioctl.Load, module, (uint)module.Length, null, 0, out _, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            return new PawnIoEcClient(null, $"PawnIO could not load the EC module (Win32 error {error}).", map);
        }

        return new PawnIoEcClient(device, "", map);
    }

    public FanReading Read()
    {
        if (_ports is null)
            return FanReading.Unreadable;
        return WithBus(() => ThinkPadEc.ReadSnapshot(_ports, _map), out var reading) ? reading : FanReading.Unreadable;
    }

    public bool TryWrite(byte command)
    {
        if (_ports is null || !Registers.IsAllowedCommand(command))
            return false;
        return WithBus(() => ThinkPadEc.TryCommandBoth(_ports, command, _map), out var wrote) && wrote;
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
            _words[0] = port;
            if (!Execute("ioctl_pio_read", _words, inputWords: 1, outputWords: 1, out var output))
                return false;
            value = (byte)output;
            return true;
        }

        public bool WritePort(ushort port, byte data)
        {
            _words[0] = port;
            _words[1] = data;
            return Execute("ioctl_pio_write", _words, inputWords: 2, outputWords: 0, out _);
        }

        // Reused for every port call. A snapshot is dozens of ioctls, and each one used to
        // allocate while the EC mutex was held. Reads use the first word; writes use both.
        private readonly ulong[] _words = new ulong[2];
        private readonly byte[] _input = new byte[FunctionNameLength + (2 * sizeof(ulong))];
        private readonly byte[] _output = new byte[sizeof(ulong)];

        private bool Execute(string name, ulong[] input, int inputWords, int outputWords, out ulong output)
        {
            output = 0;
            if (inputWords > 2 || outputWords > 1)
                return ExecuteAllocated(name, input, inputWords, outputWords, out output);

            Array.Clear(_input, 0, FunctionNameLength);
            Encoding.ASCII.GetBytes(name, _input.AsSpan(0, FunctionNameLength - 1));
            var inputBytes = FunctionNameLength + (inputWords * sizeof(ulong));
            if (inputWords > 0)
                Buffer.BlockCopy(input, 0, _input, FunctionNameLength, inputWords * sizeof(ulong));

            var result = outputWords == 0 ? null : _output;
            if (!DeviceIoControl(_device, Ioctl.Execute, _input, (uint)inputBytes, result, (uint)(result?.Length ?? 0), out var returned, IntPtr.Zero))
                return false;
            if (result is not null && returned >= sizeof(ulong))
                output = BitConverter.ToUInt64(result, 0);
            return true;
        }

        private bool ExecuteAllocated(string name, ulong[] input, int inputWords, int outputWords, out ulong output)
        {
            output = 0;
            var buffer = new byte[FunctionNameLength + (inputWords * sizeof(ulong))];
            Encoding.ASCII.GetBytes(name, buffer.AsSpan(0, FunctionNameLength - 1));
            if (inputWords > 0)
                Buffer.BlockCopy(input, 0, buffer, FunctionNameLength, inputWords * sizeof(ulong));

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
