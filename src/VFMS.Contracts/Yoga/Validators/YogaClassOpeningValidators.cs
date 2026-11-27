using FluentValidation;

namespace VFMS.Contracts.Yoga.Validators;

/// <summary>GBR-128: the same field rules as the create-class form (GBR-73, GBR-75), plus the weekday count.</summary>
public sealed class YogaClassOpeningRequestRequestValidator : AbstractValidator<YogaClassOpeningRequestRequest>
{
    public YogaClassOpeningRequestRequestValidator()
    {
        RuleFor(x => x.YogaClassTypeId).GreaterThan(0).WithMessage("Vui lòng chọn loại lớp.");
        RuleFor(x => x.ClassName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng nhập tên lớp.")
            .MaximumLength(256).WithMessage("Tối đa 256 ký tự.");
        RuleFor(x => x.DifficultyLevel)
            .Must(YogaRules.IsDifficulty).WithMessage("Độ khó phải là Cơ bản, Trung bình hoặc Nâng cao.");
        RuleFor(x => x.SessionsPerWeek).InclusiveBetween(1, 7).WithMessage("Vui lòng nhập số hợp lệ từ 1 đến 7.");
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1).WithMessage("Số học viên tối thiểu phải từ 1 trở lên.");
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants)
            .WithMessage("Số học viên tối đa phải lớn hơn hoặc bằng số tối thiểu.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(YogaRules.MinSessionMinutes, YogaRules.MaxSessionMinutes)
            .WithMessage("Vui lòng nhập số hợp lệ từ 45 đến 90.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        RuleFor(x => x.PreferredDays)
            .Must((req, days) => days is not null && days.Distinct().Count() == days.Length && days.Length == req.SessionsPerWeek)
            .WithMessage(req => $"Lớp {req.SessionsPerWeek} buổi/tuần: hãy chọn đúng {req.SessionsPerWeek} ngày khác nhau trong tuần.");
        RuleFor(x => x.Description).MaximumLength(1000).WithMessage("Tối đa 1000 ký tự.");
        RuleFor(x => x.Note).MaximumLength(1000).WithMessage("Tối đa 1000 ký tự.");
    }
}

/// <summary>GBR-129: a rejection needs a reason.</summary>
public sealed class RejectYogaClassOpeningRequestValidator : AbstractValidator<RejectYogaClassOpeningRequest>
{
    public RejectYogaClassOpeningRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Vui lòng nhập lý do từ chối.")
            .MaximumLength(500).WithMessage("Tối đa 500 ký tự.");
    }
}
