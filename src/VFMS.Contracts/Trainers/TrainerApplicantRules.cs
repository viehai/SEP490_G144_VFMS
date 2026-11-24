using System.Text.RegularExpressions;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Trainers;

/// <summary>UC 3.1 applicant field rules. They apply to trainer applications only; accounts keep UserIdentityRules.</summary>
public static partial class TrainerApplicantRules
{
    public const int MaxNameLength = 50;
    public const int MinimumAge = 18;

    [GeneratedRegex(@"\A[\p{L}\p{M}][\p{L}\p{M} '’.\-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"\A(?:0|\+?84)([35789][0-9]{8})\z", RegexOptions.CultureInvariant)]
    private static partial Regex VietnamPhonePattern();

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DomainLabelPattern();

    [GeneratedRegex(@"\A[a-z]{2,}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TopLevelDomainPattern();

    public static bool IsValidName(string? name)
    {
        var trimmed = name?.Trim();
        return trimmed is { Length: > 0 and <= MaxNameLength } && NamePattern().IsMatch(trimmed);
    }

    /// <summary>Valid account email whose domain has at least two labels and a letters-only top-level domain of 2+ characters.</summary>
    public static bool IsValidEmail(string? email)
    {
        if (!UserIdentityRules.IsValidEmail(email))
            return false;
        var normalized = UserIdentityRules.NormalizeEmail(email);
        var labels = normalized[(normalized.LastIndexOf('@') + 1)..].Split('.');
        return labels.Length >= 2 && labels.All(DomainLabelPattern().IsMatch) && TopLevelDomainPattern().IsMatch(labels[^1]);
    }

    /// <summary>Accepts 0xxxxxxxxx, 84xxxxxxxxx and +84xxxxxxxxx (prefix 03, 05, 07, 08, 09) and returns the 0xxxxxxxxx form.</summary>
    public static bool TryNormalizePhone(string? phoneNumber, out string canonical)
    {
        canonical = string.Empty;
        var digits = new string((phoneNumber ?? string.Empty).Where(c => c is not (' ' or '-' or '.')).ToArray());
        var match = VietnamPhonePattern().Match(digits);
        if (!match.Success)
            return false;
        canonical = "0" + match.Groups[1].Value;
        return true;
    }

    public static bool IsValidPhone(string? phoneNumber) => TryNormalizePhone(phoneNumber, out _);

    /// <summary>Every stored spelling of the same Vietnamese number; any other value is returned as it is normalized for accounts.</summary>
    public static IReadOnlyList<string> PhoneVariants(string? phoneNumber)
    {
        if (TryNormalizePhone(phoneNumber, out var canonical))
            return [canonical, "+84" + canonical[1..], "84" + canonical[1..]];
        return UserIdentityRules.TryNormalizePhone(phoneNumber, out var other) ? [other] : [];
    }

    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        return dateOfBirth.AddYears(age) > today ? age - 1 : age;
    }

    /// <summary>Today in Vietnam (UTC+7), the date the age rules use.</summary>
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public static bool IsAdult(DateOnly dateOfBirth) => dateOfBirth.Year >= 1900 && AgeOn(dateOfBirth, Today()) >= MinimumAge;

    /// <summary>Experience cannot start before the applicant turned 18 (0 years is always allowed).</summary>
    public static bool IsPlausibleExperience(DateOnly dateOfBirth, int experienceYears) =>
        experienceYears >= 0 && experienceYears <= Math.Max(0, AgeOn(dateOfBirth, Today()) - MinimumAge);
}
