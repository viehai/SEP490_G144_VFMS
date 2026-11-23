using System.Globalization;
using System.Text;
using VFMS.Contracts.Equipment;

namespace VFMS.Client.Services;

/// <summary>Search-as-you-type matching for the equipment picker: name, code, brand, model and category, accents ignored.</summary>
public static class EquipmentSearch
{
    /// <summary>Every word of the query must appear somewhere in the equipment's text, so "chay precor" finds "Máy chạy bộ Precor".</summary>
    public static bool Matches(EquipmentResponse item, string? query)
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var text = Fold(string.Join(' ', item.Name, item.Code, item.Brand, item.Model, item.CategoryName));
        return words.All(text.Contains);
    }

    /// <summary>What the picker shows once a piece of equipment is chosen.</summary>
    public static string Display(EquipmentResponse item) => item.Code is { Length: > 0 } code ? $"{item.Name} · {code}" : item.Name;

    /// <summary>Lower case without Vietnamese accents ("Máy Đạp" becomes "may dap").</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decomposed = value.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) plain.Append(ch);
        return plain.ToString().Normalize(NormalizationForm.FormC);
    }
}
