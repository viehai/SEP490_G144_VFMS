using FluentValidation;

namespace VFMS.Contracts.SystemAdministration.Validators;

/// <summary>UC-84: validates gym details, hours and membership renewal windows.</summary>
public sealed class GymSettingsRequestValidator : AbstractValidator<GymSettingsRequest>
{
    public GymSettingsRequestValidator()
    {
        RuleFor(x => x.GymName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Gym Name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Phone number must not exceed 20 characters.")
            .Matches(@"^\s*[0-9+\-\s()]{7,20}\s*$").WithMessage("Invalid phone number format.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Email)
            .MaximumLength(256)
            .EmailAddress().WithMessage("Invalid email address format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x)
            .Must(x => x.OpenTime.HasValue == x.CloseTime.HasValue)
            .OverridePropertyName("OpenTime")
            .WithMessage("Opening time and closing time must be set together.");
        RuleFor(x => x)
            .Must(x => x.OpenTime is null || x.CloseTime is null || x.OpenTime < x.CloseTime)
            .OverridePropertyName("OpenTime")
            .WithMessage("Opening time must be earlier than closing time.");
        RuleFor(x => x.RenewalAdvanceDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RenewalGraceDays).GreaterThanOrEqualTo(0);
    }
}

public sealed class GenerateBusinessReportRequestValidator : AbstractValidator<GenerateBusinessReportRequest>
{
    public static readonly string[] PeriodTypes = ["Day", "Week", "Month"];

    public GenerateBusinessReportRequestValidator()
    {
        RuleFor(x => x.PeriodType)
            .Must(t => t is not null && PeriodTypes.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Period type must be Day, Week or Month.");
        RuleFor(x => x.Date)
            .Must(d => d.Year >= 2000).WithMessage("Date is invalid.");
    }
}

public sealed class BusinessReportListRequestValidator : AbstractValidator<BusinessReportListRequest>
{
    public BusinessReportListRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .OverridePropertyName("From")
            .WithMessage("The start date cannot be after the end date.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
