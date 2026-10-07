using LibraryFan;
using Xunit;

namespace LibraryFan.Tests;

public class HardwareProbeTests
{
    [Fact]
    public void Read_fans_and_restore_bios_when_elevated()
    {
        if (!IsAdministrator())
            return;

        var client = PawnIoEcClient.Open();
        var report = new List<string> { client.Status };
        var reportPath = Path.Combine(AppContext.BaseDirectory, "hardware-probe.txt");
        try
        {
        Assert.True(client.IsOpen, client.Status);

        var reading = client.Read();
        report.Add(Describe(reading));
        Assert.True(reading.Ok, "The embedded controller did not answer.");
        Assert.Contains(reading.Sensors, sensor => sensor.IsValid && sensor.Celsius is >= 15 and <= 95);
        Assert.True(reading.Fan1Live && reading.Fan2Live, "Both fan speeds were not read.");

        var session = new FanSession();
        session.Tick(reading);
        var library = session.Request(ControlMode.Library);
        Assert.True(library.HasCommand);
        Assert.True(library.Command <= 4);
        Assert.True(client.TryWrite(library.Command));
        report.Add($"library-command=0x{library.Command:X2} {library.Reason}");

        var started = reading.Hottest?.Celsius ?? 0;
        if (started < 70)
        {
            Parallel.For(0, Environment.ProcessorCount, _ =>
            {
                var until = DateTime.UtcNow.AddSeconds(18);
                var value = 1.0;
                while (DateTime.UtcNow < until)
                    value = Math.Sqrt(value + 1.000001);
            });
        }

        var after = client.Read();
        report.Add(Describe(after));
        Assert.True(after.Ok);
        Assert.False(after.IsDangerous, "The probe got hot enough to require the thermal handoff.");
        if (after.Hottest?.Celsius is int hot && hot >= 55)
        {
            var stepped = session.Tick(after);
            Assert.Equal(ControlMode.Library, stepped.Mode);
            Assert.InRange(stepped.Command, (byte)1, (byte)4);
            Assert.True(client.TryWrite(stepped.Command));
            report.Add($"after-load=0x{stepped.Command:X2} {stepped.Reason}");
        }

        Assert.True(client.TryWrite(Registers.BiosControl));
        report.Add("restored=0x80");
        }
        finally
        {
            if (client.IsOpen)
                client.TryWrite(Registers.BiosControl);
            File.WriteAllText(reportPath, string.Join(Environment.NewLine, report));
            client.Dispose();
        }
    }

    private static string Describe(FanReading reading)
    {
        var sensors = string.Join(", ", reading.Sensors.Select(sensor =>
            sensor.IsValid ? $"{sensor.Label}:{sensor.Celsius}" : $"{sensor.Label}:--"));
        return $"ok={reading.Ok} fan1={reading.Fan1Rpm} fan2={reading.Fan2Rpm} {sensors}";
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
