using System.Net.Mail;
using System.Text.RegularExpressions;

namespace VFMS.Contracts.Common;

public static class UserIdentityRules
{
    private static readonly Regex PhoneInput = new(
        @"\A\+?(?:[0-9]+|\([0-9]+\))(?:[ -]?(?:[0-9]+|\([0-9]+\)))*\z",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidEmail(string? email)
    {
        var normalized = NormalizeEmail(email);
        return normalized.Length <= 256 &&
            MailAddress.TryCreate(normalized, out var address) &&
            string.Equals(address.Address, normalized, StringComparison.Ordinal);
    }

    public static bool IsValidPhone(string? phoneNumber) => TryNormalizePhone(phoneNumber, out _);

    public static bool TryNormalizePhone(string? phoneNumber, out string canonical)
    {
        canonical = string.Empty;
        var trimmed = phoneNumber?.Trim();
        if (trimmed is not { Length: >= 7 and <= 20 } || !PhoneInput.IsMatch(trimmed))
            return false;

        var digits = trimmed.Where(character => character is >= '0' and <= '9').ToArray();
        if (digits.Length < 7)
            return false;

        canonical = trimmed[0] == '+' ? "+" + new string(digits) : new string(digits);
        return true;
    }
}
