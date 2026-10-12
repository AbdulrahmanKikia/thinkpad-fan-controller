namespace LibraryFan;

/// <summary>
/// Computers offered on the first launch. A ThinkPad entry uses the register map copied from
/// nikolasgd/TPFanControl-dual-fan, and only when this machine's product type matches.
/// Dell, HP, and Framework are listed because their fan firmware is known and different.
/// Selecting one of those never applies the ThinkPad map.
/// </summary>
public static class KnownMachines
{
    public static IReadOnlyList<KnownMachine> All { get; } =
    [
        new("lenovo-p1-gen3", "Lenovo", "ThinkPad P1 Gen 3", EcMap.BuiltIn),
        new("lenovo-x1-extreme-gen3", "Lenovo", "ThinkPad X1 Extreme Gen 3",
            EcMap.Shared("ThinkPad X1 Extreme Gen 3", "20TK", null, "20TK", "20TL")),
        new("lenovo-p15-gen2", "Lenovo", "ThinkPad P15 Gen 2",
            EcMap.Shared("ThinkPad P15 Gen 2", "20YQ", null, "20YQ", "20YR")),
        new("lenovo-p14s-gen5-intel", "Lenovo", "ThinkPad P14s Gen 5 Intel",
            EcMap.Shared("ThinkPad P14s Gen 5 Intel", "21G2", null, "21G2", "21G3")),
        new("dell-smm", "Dell", "SMM fan controller", null),
        new("hp-wmi", "HP", "WMI fan controller", null),
        new("framework-ec", "Framework", "EC host commands", null)
    ];

    public static KnownMachine? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(machine => machine.Id == id);

    public static KnownMachine? Match(MachineIdentity machine) =>
        All.FirstOrDefault(item => item.Map?.Matches(machine) == true);

    public static string Explain(KnownMachine? selected, MachineIdentity machine)
    {
        if (selected?.Map is { } map && map.Matches(machine))
            return "This computer matches that model.";
        if (EcMap.BuiltIn.Matches(machine))
            return "This computer is the ThinkPad P1 Gen 3, so that map is used.";
        return Refusal(selected, machine);
    }

    public static string Refusal(KnownMachine? selected, MachineIdentity machine)
    {
        if (selected is { Map: null })
            return $"{selected.Manufacturer} uses a different fan controller. This program cannot command it, so the fans stay with firmware.";
        if (selected?.Map is not null)
        {
            var shown = string.IsNullOrWhiteSpace(machine.Product) ? "unidentified" : machine.Product;
            return $"This selection is for {selected.Model}, and this machine is {shown}. The fans stay with firmware.";
        }

        return "This machine has no hardware profile, so the fans stay with firmware. Someone who knows the embedded-controller map can add hardware-profile.json next to LibraryFan.dll.";
    }
}

public sealed record KnownMachine(string Id, string Manufacturer, string Model, EcMap? Map)
{
    public string Label => $"{Manufacturer} · {Model}";
}
