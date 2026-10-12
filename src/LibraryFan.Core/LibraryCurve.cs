using System.Text.Json;

namespace LibraryFan;

public sealed record CurveStep(int Celsius, int Level);

/// <summary>
/// The quiet curve. Fans off only below <see cref="OffBelowCelsius"/>, then fixed steps.
/// A level steps down only after the temperature falls <see cref="HysteresisCelsius"/>
/// below the point that engaged it. The 80°C ceiling lives in <see cref="FanSession"/>, not here.
/// </summary>
public sealed class LibraryCurve
{
    public int OffBelowCelsius { get; init; } = 55;
    public int HysteresisCelsius { get; init; } = 3;
    public CurveStep[] Steps { get; init; } =
    [
        new(55, 1),
        new(62, 2),
        new(68, 3),
        new(74, 4)
    ];

    private CurveStep[]? _byTemperature;

    private CurveStep[] ByTemperature => _byTemperature ??= Steps.OrderBy(step => step.Celsius).ToArray();

    public int Choose(int temperature, ref int? heldLevel)
    {
        var upward = LevelFor(temperature);
        int level;
        if (heldLevel is null || upward >= heldLevel)
        {
            level = upward;
        }
        else
        {
            level = heldLevel.Value;
            while (level > 0 && temperature < EngageCelsius(level) - HysteresisCelsius)
                level--;
        }

        if (temperature >= OffBelowCelsius && level == 0)
            level = 1;

        heldLevel = level;
        return level;
    }

    public int LevelFor(int temperature)
    {
        var level = 0;
        foreach (var step in ByTemperature)
        {
            if (temperature >= step.Celsius)
                level = step.Level;
        }

        return level;
    }

    public int EngageCelsius(int level) =>
        ByTemperature.FirstOrDefault(step => step.Level == level)?.Celsius ?? OffBelowCelsius;

    public static string DefaultPath() =>
        Path.Combine(AppContext.BaseDirectory, "library-curve.json");

    public static LibraryCurve Load(string path)
    {
        if (!File.Exists(path))
            return new LibraryCurve();

        var file = JsonSerializer.Deserialize<CurveFile>(File.ReadAllText(path), JsonOptions);
        if (file?.Steps is null || file.Steps.Length == 0)
            return new LibraryCurve();

        return new LibraryCurve
        {
            OffBelowCelsius = Math.Min(file.OffBelowCelsius, 55),
            HysteresisCelsius = file.HysteresisCelsius,
            Steps = file.Steps
                .Where(step => step.Level is >= 1 and <= Registers.MaxLevel)
                .Select(step => new CurveStep(step.Celsius, step.Level))
                .ToArray()
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class CurveFile
    {
        public int OffBelowCelsius { get; set; } = 55;
        public int HysteresisCelsius { get; set; } = 3;
        public CurveStepFile[]? Steps { get; set; }
    }

    private sealed class CurveStepFile
    {
        public int Celsius { get; set; }
        public int Level { get; set; }
    }
}
