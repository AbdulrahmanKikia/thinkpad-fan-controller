using System.IO;

namespace LibraryFan;

/// <summary>
/// Drives the simulator window through the cases in .cursor/skills/fan-safety/SKILL.md and
/// writes self-test.log next to the app. Used by <c>--self-test</c> and by the window test.
/// </summary>
internal static class SelfTest
{
    public static async Task<bool> RunAsync(MainWindow window)
    {
        var log = new List<string>();

        bool Fail(string message)
        {
            log.Add(message);
            Write(log);
            window.ShowFailure(message);
            return false;
        }

        string Reason() => window.Latest?.Reason ?? "";

        if (!await window.WaitForUpdateAsync(TimeSpan.FromSeconds(5)))
            return Fail("The window never received a fan update.");

        MainWindow.Click(window.LibraryButton);
        window.SetSimTemperature(64);
        if (Reason() != "Library is holding level 2 because 0x78 is 64°C.")
            return Fail($"Library at 64°C said '{Reason()}'.");

        window.SetSimTemperature(80);
        if (Reason() != FanSession.ThermalHandoff)
            return Fail($"Library handoff said '{Reason()}'.");

        MainWindow.Click(window.LibraryButton);
        if (Reason() != FanSession.ThermalHandoff)
            return Fail("Library resumed while the sensor was still at 80°C.");
        if (!(window.Latest?.Refusal?.Contains("stays in control", StringComparison.Ordinal) ?? false))
            return Fail("The refused handoff did not explain itself.");

        window.SetSimTemperature(60);
        MainWindow.Click(window.LibraryButton);
        if (!Reason().StartsWith("Library is holding level", StringComparison.Ordinal))
            return Fail($"Library did not resume after cooling: '{Reason()}'.");

        MainWindow.Click(window.LevelButton(1));
        if (Reason() != "Manual is holding level 1.")
            return Fail($"Manual said '{Reason()}'.");

        var before = window.Latest?.Fan1Rpm;
        window.Controller.DropSimRpm(1);
        if (window.Latest?.Fan1Held != true || window.Latest.Fan1Rpm != before)
            return Fail("One missed fan reading did not hold the previous speed.");
        window.Controller.Poll();
        if (window.Latest?.Fan1Held != false)
            return Fail("The held fan speed did not clear after a good poll.");

        window.SetSimTemperature(80);
        if (Reason() != FanSession.ThermalHandoff)
            return Fail($"Manual handoff said '{Reason()}'.");

        window.SetSimTemperature(50);
        MainWindow.Click(window.BiosButton);
        if (Reason() != FanSession.BiosSentence)
            return Fail($"BIOS said '{Reason()}'.");

        MainWindow.Click(window.LevelButton(2));
        if (Reason() != "Manual is holding level 2.")
            return Fail($"Manual level 2 said '{Reason()}'.");

        window.Controller.FailSimReads(1);
        if (window.Latest?.IsLatched != false || window.Latest.Mode != ControlMode.Manual)
            return Fail("One unreadable poll handed control back.");

        window.Controller.FailSimReads(2);
        if (window.Latest?.IsLatched != true || Reason() != FanSession.ReadHandoff)
            return Fail($"Three unreadable polls said '{Reason()}'.");

        MainWindow.Click(window.BiosButton);
        if (Reason() != FanSession.BiosSentence)
            return Fail($"BIOS after the read handoff said '{Reason()}'.");

        log.Add("self-test passed");
        Write(log);
        return true;
    }

    private static void Write(List<string> log)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), string.Join(Environment.NewLine, log));
        }
        catch (IOException)
        {
        }
    }
}
