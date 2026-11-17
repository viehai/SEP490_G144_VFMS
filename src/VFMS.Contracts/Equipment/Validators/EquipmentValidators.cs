using FluentValidation;

namespace VFMS.Contracts.Equipment.Validators;

internal static class EquipmentRules
{
    public static readonly string[] EquipmentStatuses = ["Operational", "UnderMaintenance", "Damaged", "Retired"];
    public static readonly string[] IncidentTypes = ["Malfunction", "Damage", "Safety", "Other"];
    public static readonly string[] IssueStatuses = ["Reported", "InProgress", "Escalated", "Resolved", "MarkedSafe"];

    public static bool IsOneOf(string? value, string[] allowed) =>
        value is not null && allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsHttpUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ||
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);

    /// <summary>Today's date at the gym (UTC+7).</summary>
    public static DateOnly GymToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
}

public sealed class EquipmentCategoryRequestValidator : AbstractValidator<EquipmentCategoryRequest>
{
    public EquipmentCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Name field is required.")
            .MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CodePrefix)
            .Must(prefix => !string.IsNullOrWhiteSpace(prefix)).WithMessage("The Code Prefix field is required.")
            .Matches(@"^\s*[A-Za-z]{2,5}\s*$").WithMessage("Code prefix must be 2 to 5 letters.");
    }
}

public sealed class EquipmentRequestValidator : AbstractValidator<EquipmentRequest>
{
    public EquipmentRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.CategoryId)
            .Must(id => id is > 0).WithMessage("The Category field is required.");
        RuleFor(x => x.Code).MaximumLength(50);
        RuleFor(x => x.Brand).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).When(x => x.PurchasePrice.HasValue);
        RuleFor(x => x.PurchaseDate)
            .Must(date => date is null || date <= EquipmentRules.GymToday())
            .WithMessage("Purchase date cannot be in the future.");
        RuleFor(x => x.DescriptionUrl)
            .MaximumLength(2048)
            .Must(EquipmentRules.IsHttpUrl).WithMessage("Description URL must be a valid HTTP or HTTPS URL.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class EquipmentStatusRequestValidator : AbstractValidator<EquipmentStatusRequest>
{
    public EquipmentStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => EquipmentRules.IsOneOf(status, EquipmentRules.EquipmentStatuses))
            .WithMessage("Status must be Operational, UnderMaintenance, Damaged or Retired.");
        RuleFor(x => x.Note)
            .Must(note => !string.IsNullOrWhiteSpace(note))
            .When(x => string.Equals(x.Status?.Trim(), "Retired", StringComparison.OrdinalIgnoreCase))
            .WithMessage("A reason is required to retire equipment.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class EquipmentListRequestValidator : AbstractValidator<EquipmentListRequest>
{
    public EquipmentListRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || EquipmentRules.IsOneOf(status, EquipmentRules.EquipmentStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ReportEquipmentIssueRequestValidator : AbstractValidator<ReportEquipmentIssueRequest>
{
    public ReportEquipmentIssueRequestValidator()
    {
        RuleFor(x => x.EquipmentId).GreaterThan(0).WithMessage("The Equipment field is required.");
        RuleFor(x => x.IncidentType)
            .Must(type => EquipmentRules.IsOneOf(type, EquipmentRules.IncidentTypes))
            .WithMessage("Incident type must be Malfunction, Damage, Safety or Other.");
        RuleFor(x => x.Description)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("The Description field is required.")
            .MaximumLength(2000);
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(EquipmentRules.IsHttpUrl).WithMessage("Image URL must be a valid HTTP or HTTPS URL.");
    }
}

public sealed class EquipmentIssueListRequestValidator : AbstractValidator<EquipmentIssueListRequest>
{
    public EquipmentIssueListRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || EquipmentRules.IsOneOf(status, EquipmentRules.IssueStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class EquipmentIssueActionRequestValidator : AbstractValidator<EquipmentIssueActionRequest>
{
    public EquipmentIssueActionRequestValidator()
    {
        RuleFor(x => x.Note)
            .Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("The Note field is required.")
            .MaximumLength(1000);
    }
}
