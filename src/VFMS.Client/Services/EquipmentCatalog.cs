namespace VFMS.Client.Services;

/// <summary>
/// Real commercial gym equipment brands and their product lines, used for the Brand and Model dropdowns
/// (UC-76 Manage Gym Equipment, UC-74 purchase proposal items). The values are saved in the existing
/// Equipment.EquipmentBrand / EquipmentModel text columns.
/// </summary>
public static class EquipmentCatalog
{
    public sealed record Brand(string Name, IReadOnlyList<string> Models);

    public static readonly IReadOnlyList<Brand> Brands =
    [
        new("Technogym", ["Skillrun", "Excite Run", "Skillbike", "Excite Bike", "Excite Synchro", "Skillrow",
            "Selection 700 Chest Press", "Selection 700 Leg Press", "Kinesis Personal"]),
        new("Life Fitness", ["Integrity Series Treadmill", "Integrity Series Lifecycle Bike", "Integrity Series Cross-Trainer",
            "Insignia Series Chest Press", "Insignia Series Leg Press", "Signature Series Cable Crossover"]),
        new("Matrix", ["Performance Treadmill", "Performance Upright Cycle", "Performance Elliptical",
            "Ultra Leg Press", "Aura Lat Pulldown", "Magnum Power Rack"]),
        new("Precor", ["TRM 885", "TRM 835", "EFX 885", "AMT 885", "UBK 835", "Discovery Series Leg Press"]),
        new("Hammer Strength", ["Plate-Loaded Iso-Lateral Chest Press", "Plate-Loaded Iso-Lateral Row",
            "HD Elite Power Rack", "Select Leg Extension", "Olympic Flat Bench"]),
        new("Cybex", ["R Series Treadmill", "Arc Trainer", "Eagle NX Chest Press", "Eagle NX Leg Press"]),
        new("Concept2", ["RowErg", "SkiErg", "BikeErg"]),
        new("Rogue Fitness", ["R-3 Power Rack", "Echo Bike", "Ohio Bar", "Rubber Hex Dumbbell", "Flat Utility Bench"]),
    ];

    public static IReadOnlyList<string> BrandNames { get; } = Brands.Select(b => b.Name).ToArray();

    /// <summary>Models of a brand; empty when the brand is blank or not in the list.</summary>
    public static IReadOnlyList<string> ModelsOf(string? brand) =>
        Brands.FirstOrDefault(b => string.Equals(b.Name, brand, StringComparison.OrdinalIgnoreCase))?.Models ?? [];

    /// <summary>
    /// Dropdown options: the list, plus <paramref name="current"/> when it is not in the list (equipment saved before
    /// the dropdowns existed keeps its value).
    /// </summary>
    public static IReadOnlyList<string> WithCurrent(IReadOnlyList<string> options, string? current) =>
        string.IsNullOrWhiteSpace(current) || options.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? options
            : [.. options, current];
}
