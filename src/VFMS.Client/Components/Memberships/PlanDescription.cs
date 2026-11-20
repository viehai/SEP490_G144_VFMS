namespace VFMS.Client.Components.Memberships;

/// <summary>
/// A plan description as the price cards show it: the first line is the tagline, every further line is one benefit.
/// Managers write it in the plan form's "Mô tả" box, one benefit per line.
/// </summary>
public sealed record PlanDescription(string? Tagline, IReadOnlyList<string> Features)
{
    public static PlanDescription Parse(string? description)
    {
        var lines = (description ?? "").Split('\n')
            .Select(line => line.Trim().TrimStart('-', '•', '✓').Trim())
            .Where(line => line.Length > 0).ToArray();
        return new(lines.FirstOrDefault(), lines.Skip(1).ToArray());
    }

    /// <summary>Whole months in a plan term (30-day months), at least 1; 365 days is 12.</summary>
    public static int Months(int? durationInDays) => Math.Max(1, (int)Math.Round((durationInDays ?? 30) / 30.0));
}
