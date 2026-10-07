using System.Text.Json;

namespace LibraryFan;

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
        foreach (var step in Steps.OrderBy(step => step.Celsius))
        {
            if (temperature >= step.Celsius)
                level = step.Level;
        }

        return level;
    }

    public int EngageCelsius(int level)
    {
        var match = Steps.FirstOrDefault(step => step.Level == level);
        return match?.Celsius ?? OffBelowCelsius;
    }

    public static LibraryCurve Load(string path)
    {
        if (!File.Exists(path))
            return new LibraryCurve();

        var file = JsonSerializer.Deserialize<CurveFile>(File.ReadAllText(path));
        if (file?.Steps is null || file.Steps.Length == 0)
            return new LibraryCurve();

        return new LibraryCurve
        {
            OffBelowCelsius = file.OffBelowCelsius,
            HysteresisCelsius = file.HysteresisCelsius,
            Steps = file.Steps.Select(step => new CurveStep(step.Celsius, step.Level)).ToArray()
        };
    }

    public static string DefaultPath() =>
        Path.Combine(AppContext.BaseDirectory, "library-curve.json");

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

public sealed record CurveStep(int Celsius, int Level);
