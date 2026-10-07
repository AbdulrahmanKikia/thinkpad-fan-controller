using Xunit;

namespace LibraryFan.Tests;

/// <summary>
/// Runs only in an elevated test host on the ThinkPad. Reads both fans, asks Library for a level,
/// writes it, and always hands the fans back with 0x80. It never heats the machine on purpose.
/// </summary>
public class HardwareProbeTests
{
    [Fact]
    public void Read_both_fans_apply_the_library_level_and_restore_bios()
    {
        if (!IsAdministrator())
            return;

        var report = new List<string>();
        var reportPath = Path.Combine(AppContext.BaseDirectory, "hardware-probe.txt");
        var client = PawnIoEcClient.Open();
        report.Add(client.Status);
        try
        {
            Assert.True(client.IsOpen, client.Status);

            var reading = client.Read();
            report.Add(Describe(reading));
            Assert.True(reading.Ok, "The embedded controller did not answer.");
            Assert.Contains(reading.Sensors, sensor => sensor.IsValid && sensor.Celsius is >= 15 and <= 95);
            Assert.True(reading.Fan1Live && reading.Fan2Live, "Both fan speeds were not read.");
            Assert.False(reading.IsDangerous, "A sensor is already at 80°C; the probe will not take the fans.");

            var session = new FanSession();
            session.Tick(reading);
            var library = session.Request(ControlMode.Library);
            Assert.True(library.HasCommand);
            Assert.InRange(library.Command, (byte)0, (byte)4);
            Assert.True(client.TryWrite(library.Command), "The level write was not accepted.");
            report.Add($"library-command=0x{library.Command:X2} {library.Reason}");

            Thread.Sleep(1500);
            var after = client.Read();
            report.Add(Describe(after));
            Assert.True(after.Ok);

            Assert.True(client.TryWrite(Registers.BiosControl), "The restore write was not accepted.");
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
            sensor.IsValid ? $"{sensor.Label}:{sensor.Celsius}" : sensor.WasRead ? $"{sensor.Label}:--" : $"{sensor.Label}:??"));
        return $"ok={reading.Ok} read={reading.RegistersRead}/{reading.RegistersTotal} fan1={reading.Fan1Rpm} fan2={reading.Fan2Rpm} {sensors}";
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
