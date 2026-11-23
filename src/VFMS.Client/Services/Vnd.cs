using System.Globalization;
using System.Text.RegularExpressions;

namespace VFMS.Client.Services;

internal static class Vnd
{
    // WebAssembly can run without vi-VN culture data.
    private static readonly NumberFormatInfo NumberFormat = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static readonly Regex Integer = new(@"^-?[0-9]+\z");
    private static readonly Regex GroupedInteger = new(@"^-?[0-9]{1,3}(?:\.[0-9]{3})+\z");
    private static readonly Regex Fraction = new(@"^,[0-9]{0,2}\z");
    private static readonly Regex Thousands = new(@"\B(?=(?:[0-9]{3})+\z)");

    public static string Format(decimal value) => FormatAmount(value) + " ₫";

    public static string FormatAmount(decimal value) => value.ToString("#,##0.##", NumberFormat);

    public static decimal? ParseInput(string? input, bool wholeUnits = false)
    {
        var text = input?.Trim() ?? "";
        if (!TrySplit(text, wholeUnits, out var integer, out var fraction) || fraction == "," ||
            !IsValidInteger(integer)) return null;

        var invariant = integer.Replace(".", "") + fraction.Replace(',', '.');
        return decimal.TryParse(invariant, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    public static string FormatInput(string? previous, string? input, bool wholeUnits = false)
    {
        var text = input?.Trim() ?? "";
        if (!TrySplit(text, wholeUnits, out var integer, out var fraction)) return text;

        var digits = integer.Replace(".", "");
        if (!IsValidInteger(integer))
        {
            // A browser edit can temporarily shift separators that were already in the field.
            var previousInteger = previous?.Split(',')[0] ?? "";
            if (!Integer.IsMatch(digits) || !GroupedInteger.IsMatch(previousInteger) ||
                InsertsNewSeparator(previousInteger, integer)) return text;
        }

        // Retain the comma and the exact fraction being typed, including trailing zeroes.
        return Thousands.Replace(digits, ".") + fraction;
    }

    private static bool IsValidInteger(string text) => Integer.IsMatch(text) || GroupedInteger.IsMatch(text);

    private static bool TrySplit(string text, bool wholeUnits, out string integer, out string fraction)
    {
        var comma = text.IndexOf(',');
        integer = comma < 0 ? text : text[..comma];
        fraction = comma < 0 ? "" : text[comma..];
        return comma < 0 || !wholeUnits && Fraction.IsMatch(fraction);
    }

    private static bool InsertsNewSeparator(string previous, string current)
    {
        var start = 0;
        while (start < previous.Length && start < current.Length && previous[start] == current[start]) start++;

        var previousEnd = previous.Length;
        var currentEnd = current.Length;
        while (previousEnd > start && currentEnd > start && previous[previousEnd - 1] == current[currentEnd - 1])
        {
            previousEnd--;
            currentEnd--;
        }

        return current.AsSpan(start, currentEnd - start).Contains('.');
    }
}
