using System.Globalization;
using System.Text.Json;

namespace LibraryFan;

/// <summary>
/// One machine's embedded-controller map for the protocol this program already speaks:
/// fan select, one command byte, a speed word, and temperature bytes. The firmware command,
/// the disengaged-fan bit, and the 80°C handoff are not in here.
/// </summary>
public sealed class EcMap
{
    private EcMap(
        string name,
        string product,
        string? biosPrefix,
        byte fanSelect,
        byte[] fans,
        byte fanCommand,
        byte speedLow,
        byte speedHigh,
        byte[] temperatures,
        int staleRpmAbove,
        string[]? productPrefixes = null)
    {
        Name = name;
        Product = product;
        BiosPrefix = biosPrefix;
        FanSelect = fanSelect;
        Fans = fans;
        FanCommand = fanCommand;
        SpeedLow = speedLow;
        SpeedHigh = speedHigh;
        Temperatures = temperatures;
        StaleRpmAbove = staleRpmAbove;
        ProductPrefixes = productPrefixes ?? [];
    }

    public string Name { get; }
    public string Product { get; }
    public string? BiosPrefix { get; }
    public byte FanSelect { get; }
    public byte[] Fans { get; }
    public byte FanCommand { get; }
    public byte SpeedLow { get; }
    public byte SpeedHigh { get; }
    public byte[] Temperatures { get; }
    public int StaleRpmAbove { get; }
    public string[] ProductPrefixes { get; }
    public int SnapshotRegisters => Fans.Length + Temperatures.Length;

    public static EcMap BuiltIn { get; } = Shared(
        "ThinkPad P1 Gen 3",
        "20TH000XUS",
        "N2VET41W",
        "20TH");

    /// <summary>
    /// The register map copied from the dual-fan tool. Callers name the machine type.
    /// </summary>
    public static EcMap Shared(string name, string product, string? biosPrefix, params string[] productPrefixes) => new(
        name,
        product,
        biosPrefix,
        Registers.FanSelect,
        [Registers.Fan1, Registers.Fan2],
        Registers.FanCommand,
        Registers.FanSpeedLow,
        Registers.FanSpeedHigh,
        Registers.TemperatureAddresses,
        Registers.StaleRpmAbove,
        productPrefixes);

    public bool Matches(MachineIdentity machine)
    {
        var product = machine.Product ?? "";
        var productOk = Product.Equals(product, StringComparison.OrdinalIgnoreCase);
        if (!productOk)
        {
            foreach (var prefix in ProductPrefixes)
            {
                if (prefix.Length >= 4 && product.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    productOk = true;
                    break;
                }
            }
        }

        if (!productOk)
            return false;
        var bios = machine.Bios ?? "";
        return BiosPrefix is null || bios.Contains(BiosPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static ProfileChoice Load(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ProfileChoice.Fail("The hardware profile must be a JSON object. The fans stay with firmware.");
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return ProfileChoice.Fail("The hardware profile is not valid JSON. The fans stay with firmware.");
        }
    }

    private static ProfileChoice Read(JsonElement root)
    {
        foreach (var banned in new[] { "biosControl", "disengagedFullSpeed", "dangerCelsius", "maxLevel" })
        {
            if (root.TryGetProperty(banned, out _))
                return ProfileChoice.Fail("The profile cannot change the firmware command, the disengaged-fan bit, or the 80°C handoff.");
        }

        if (root.TryGetProperty("dataPort", out var dataPort) || root.TryGetProperty("controlPort", out var controlPort))
        {
            var dataOk = root.TryGetProperty("dataPort", out dataPort) && TryByte(dataPort, out var data) && data == Registers.DataPort;
            var controlOk = root.TryGetProperty("controlPort", out controlPort) && TryByte(controlPort, out var control) && control == Registers.ControlPort;
            if (!dataOk || !controlOk)
                return ProfileChoice.Fail("PawnIO only opens data port 0x62 and control port 0x66. This profile cannot command the fans.");
        }

        if (!TryText(root, "name", out var name) || !TryText(root, "product", out var product))
            return ProfileChoice.Fail("The profile needs a name and the product it was written for.");
        if (!TryByte(root, "fanSelect", out var fanSelect)
            || !TryByte(root, "fanCommand", out var fanCommand)
            || !TryByte(root, "speedLow", out var speedLow)
            || !TryByte(root, "speedHigh", out var speedHigh)
            || !TryBytes(root, "fans", out var fans)
            || !TryBytes(root, "temperatures", out var temperatures))
            return ProfileChoice.Fail("The profile needs fanSelect, fans, fanCommand, speedLow, speedHigh, and temperatures.");

        if (fans.Length is < 1 or > 2 || fans.Distinct().Count() != fans.Length)
            return ProfileChoice.Fail("The profile needs one or two distinct fan selectors.");
        if (temperatures.Length is < 1 or > 16 || temperatures.Distinct().Count() != temperatures.Length)
            return ProfileChoice.Fail("The profile needs between 1 and 16 distinct temperature registers.");

        var stale = Registers.StaleRpmAbove;
        if (root.TryGetProperty("staleRpmAbove", out var staleElement))
        {
            if (!TryInt(staleElement, out stale) || stale is < 1 or > Registers.StaleRpmAbove)
                return ProfileChoice.Fail("staleRpmAbove cannot be raised above 8191.");
        }

        string? bios = null;
        if (TryText(root, "bios", out var biosText))
            bios = biosText;

        return new ProfileChoice(new EcMap(name, product, bios, fanSelect, fans, fanCommand, speedLow, speedHigh, temperatures, stale), null);
    }

    private static bool TryText(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString()?.Trim() ?? "";
        return value.Length is > 0 and <= 80;
    }

    private static bool TryByte(JsonElement root, string name, out byte value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && TryByte(element, out value);
    }

    private static bool TryBytes(JsonElement root, string name, out byte[] values)
    {
        values = [];
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
            return false;
        var list = new List<byte>();
        foreach (var item in element.EnumerateArray())
        {
            if (!TryByte(item, out var value))
                return false;
            list.Add(value);
        }

        values = list.ToArray();
        return true;
    }

    private static bool TryByte(JsonElement element, out byte value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number is >= 0 and <= 255)
        {
            value = (byte)number;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString()?.Trim() ?? "";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryInt(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out value);
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString()?.Trim() ?? "";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record MachineIdentity(string Product, string Bios);

public sealed record ProfileChoice(EcMap? Map, string? Problem)
{
    public static ProfileChoice Fail(string problem) => new(null, problem);
}
