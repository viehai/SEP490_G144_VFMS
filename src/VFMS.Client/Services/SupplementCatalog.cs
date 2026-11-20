namespace VFMS.Client.Services;

/// <summary>
/// Standard supplement product types and real supplement brands, used for the Type and Brand dropdowns on the
/// Manager product form (UC-83) and for the store filters (UC-77). The values are saved in the existing
/// SupplementProduct.CatalogCategory and Brand text columns, so the server does not limit them to this list.
/// </summary>
public static class SupplementCatalog
{
    /// <summary>A product type with a short Vietnamese hint shown under the dropdown.</summary>
    public sealed record ProductType(string Name, string Hint);

    public static readonly IReadOnlyList<ProductType> Types =
    [
        new("Whey Protein", "Đạm whey cô đặc hoặc phân lập, uống sau tập."),
        new("Plant Protein", "Đạm thực vật (đậu nành, đậu Hà Lan), cho người ăn chay."),
        new("Mass Gainer", "Tăng cân: đạm và tinh bột nhiều năng lượng."),
        new("Creatine", "Tăng sức mạnh và sức bền cho bài tập nặng."),
        new("Pre-workout", "Uống trước tập, thường có caffeine."),
        new("BCAA/EAA", "Axit amin, uống trong khi tập."),
        new("Vitamin & Khoáng chất", "Vitamin tổng hợp, kẽm, magie..."),
        new("Omega-3", "Dầu cá, hỗ trợ tim mạch và khớp."),
        new("Đốt mỡ", "Hỗ trợ giảm mỡ, L-carnitine, CLA."),
        new("Điện giải", "Bù nước và khoáng khi tập lâu."),
        new("Thanh protein", "Thanh ăn nhẹ giàu đạm."),
    ];

    public static IReadOnlyList<string> TypeNames { get; } = Types.Select(t => t.Name).ToArray();

    /// <summary>Supplement brands sold in Vietnam, A to Z.</summary>
    public static readonly IReadOnlyList<string> Brands =
    [
        "Applied Nutrition",
        "BSN",
        "Dymatize",
        "Kaged",
        "MuscleTech",
        "Myprotein",
        "Now Foods",
        "Nutrex",
        "Optimum Nutrition",
        "Ostrovit",
        "Rule 1",
        "Ultimate Nutrition",
    ];

    /// <summary>The hint for a product type; empty when the type is blank or not in the list.</summary>
    public static string HintOf(string? type) =>
        Types.FirstOrDefault(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase))?.Hint ?? string.Empty;

    /// <summary>
    /// Dropdown options: the list, plus <paramref name="current"/> when it is not in the list (a product saved before
    /// the dropdowns existed keeps its value).
    /// </summary>
    public static IReadOnlyList<string> WithCurrent(IReadOnlyList<string> options, string? current) =>
        EquipmentCatalog.WithCurrent(options, current);
}
