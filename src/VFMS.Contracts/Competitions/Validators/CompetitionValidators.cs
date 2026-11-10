using FluentValidation;

namespace VFMS.Contracts.Competitions.Validators;

internal static class CompetitionRules
{
    public static readonly string[] CompetitionStatuses = ["Draft", "Published", "Ongoing", "Completed", "Cancelled"];
    public static readonly string[] ParticipantStatuses = ["Registered", "Confirmed", "Disqualified", "Completed"];

    public static bool IsOneOf(string? value, string[] allowed) =>
        value is not null && allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsHttpUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ||
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);
}

public sealed class CompetitionRequestValidator : AbstractValidator<CompetitionRequest>
{
    public CompetitionRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Rules).MaximumLength(4000);
        RuleFor(x => x.MaxParticipants)
            .GreaterThan(0).When(x => x.MaxParticipants.HasValue)
            .WithMessage("Max participants must be greater than 0.");
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(CompetitionRules.IsHttpUrl).WithMessage("Image URL must be a valid HTTP or HTTPS URL.");

        RuleFor(x => x.RegistrationEnd)
            .GreaterThanOrEqualTo(x => x.RegistrationStart)
            .WithMessage("The registration end must not be before its start.");
        RuleFor(x => x.StartDate)
            .GreaterThanOrEqualTo(x => x.RegistrationEnd)
            .WithMessage("The competition must start after the registration period ends.");
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("The competition end must not be before its start.");

        RuleForEach(x => x.Prizes).SetValidator(new CompetitionPrizeRequestValidator());
        RuleFor(x => x.Prizes)
            .Must(prizes => prizes == null || prizes.Select(p => p.Rank).Distinct().Count() == prizes.Count)
            .WithMessage("Each rank can have only one prize.");
    }
}

public sealed class CompetitionPrizeRequestValidator : AbstractValidator<CompetitionPrizeRequest>
{
    public CompetitionPrizeRequestValidator()
    {
        RuleFor(x => x.PrizeName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Prize name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.Rank)
            .GreaterThan(0).WithMessage("Rank must be greater than 0.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.VoucherId).GreaterThan(0).When(x => x.VoucherId.HasValue);
    }
}

public sealed class CompetitionListRequestValidator : AbstractValidator<CompetitionListRequest>
{
    public CompetitionListRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || CompetitionRules.IsOneOf(status, CompetitionRules.CompetitionStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class CompetitionParticipantListRequestValidator : AbstractValidator<CompetitionParticipantListRequest>
{
    public CompetitionParticipantListRequestValidator()
    {
        RuleFor(x => x.CompetitionId).GreaterThan(0).When(x => x.CompetitionId.HasValue);
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || CompetitionRules.IsOneOf(status, CompetitionRules.ParticipantStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class RecordCompetitionResultRequestValidator : AbstractValidator<RecordCompetitionResultRequest>
{
    public RecordCompetitionResultRequestValidator()
    {
        RuleFor(x => x.Score)
            .GreaterThanOrEqualTo(0).When(x => x.Score.HasValue)
            .WithMessage("The score cannot be negative.");
        RuleFor(x => x.Rank)
            .GreaterThan(0).When(x => x.Rank.HasValue)
            .WithMessage("Rank must be greater than 0.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class ConfirmCompetitionResultRequestValidator : AbstractValidator<ConfirmCompetitionResultRequest>
{
    public ConfirmCompetitionResultRequestValidator()
    {
        RuleFor(x => x.JoinId).GreaterThan(0).WithMessage("The Join field is required.");
        RuleFor(x => x.Score)
            .GreaterThanOrEqualTo(0).When(x => x.Score.HasValue)
            .WithMessage("The score cannot be negative.");
        RuleFor(x => x.Rank)
            .GreaterThan(0).When(x => x.Rank.HasValue)
            .WithMessage("Rank must be greater than 0.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}
