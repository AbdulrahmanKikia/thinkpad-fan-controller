using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LibraryFan;

/// <summary>
/// Picks the map for this boot. A file is used only when it names the machine that is running.
/// A setup choice is used only when that model's product type matches. Otherwise the built-in
/// map is used on the P1 Gen 3 it was read from, and every other machine keeps its fans with firmware.
/// </summary>
public static class HardwareProfile
{
    public const string FileName = "hardware-profile.json";

    public static string BesideApp() => Path.Combine(AppContext.BaseDirectory, FileName);

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryFan", FileName);

    public static ProfileChoice Choose(MachineIdentity machine, string besideApp, string userFile, string? selectedId = null)
    {
        foreach (var path in new[] { besideApp, userFile })
        {
            if (!File.Exists(path))
                continue;

            ProfileChoice loaded;
            try
            {
                loaded = EcMap.Load(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ProfileChoice.Fail("The hardware profile could not be read. The fans stay with firmware.");
            }

            if (loaded.Map is null)
                return loaded;
            if (!loaded.Map.Matches(machine))
            {
                var shown = string.IsNullOrWhiteSpace(machine.Product) ? "unidentified" : machine.Product;
                return ProfileChoice.Fail($"This profile is for {loaded.Map.Product}, and this machine is {shown}. The fans stay with firmware.");
            }

            return loaded;
        }

        var selected = KnownMachines.Find(selectedId);
        if (selected?.Map is { } chosen && chosen.Matches(machine))
            return new ProfileChoice(chosen, null);

        if (EcMap.BuiltIn.Matches(machine))
            return new ProfileChoice(EcMap.BuiltIn, null);

        return ProfileChoice.Fail(KnownMachines.Refusal(selected, machine));
    }

    [SupportedOSPlatform("windows")]
    public static MachineIdentity ReadMachine()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var product = key?.GetValue("SystemProductName") as string ?? "";
            var bios = key?.GetValue("BIOSVersion") switch
            {
                string text => text,
                string[] parts => string.Join(" ", parts),
                _ => ""
            };
            return new MachineIdentity(product.Trim(), bios.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new MachineIdentity("", "");
        }
    }
}
