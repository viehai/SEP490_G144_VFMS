using FluentValidation;

namespace VFMS.Contracts.Yoga.Validators;

public static class YogaRules
{
    public static readonly string[] DifficultyLevels = ["Beginner", "Intermediate", "Advanced"];
    public const int MinSessionMinutes = 45;   // GBR-75
    public const int MaxSessionMinutes = 90;   // GBR-75

    public static bool IsDifficulty(string? value) =>
        value is not null && DifficultyLevels.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>1 Beginner, 2 Intermediate, 3 Advanced; 0 for anything else.</summary>
    public static int LevelRank(string? level) =>
        level is null ? 0 : Array.FindIndex(DifficultyLevels, d => d.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase)) + 1;

    /// <summary>
    /// A yoga plan of <paramref name="planLevel"/> books classes of that level and every level below it.
    /// A null plan level (plans sold before levels existed) books every class.
    /// </summary>
    public static bool PlanCovers(string? planLevel, string? classLevel) =>
        planLevel is null || LevelRank(planLevel) >= LevelRank(classLevel);

    /// <summary>Vietnamese label: Cơ bản / Trung bình / Nâng cao.</summary>
    public static string LevelLabel(string? level) => LevelRank(level) switch
    {
        1 => "Cơ bản", 2 => "Trung bình", 3 => "Nâng cao", _ => level ?? string.Empty,
    };
}

public sealed class YogaStudioRequestValidator : AbstractValidator<YogaStudioRequest>
{
    public YogaStudioRequestValidator()
    {
        RuleFor(x => x.StudioName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng nhập tên phòng.")
            .MaximumLength(200).WithMessage("Tối đa 200 ký tự.");
        RuleFor(x => x.FloorNumber).InclusiveBetween(-5, 100)
            .WithMessage("Vui lòng nhập số hợp lệ từ -5 đến 100.");
        RuleFor(x => x.Capacity).InclusiveBetween(1, 200)
            .WithMessage("Vui lòng nhập số hợp lệ từ 1 đến 200.");
    }
}

public sealed class YogaClassTypeRequestValidator : AbstractValidator<YogaClassTypeRequest>
{
    public YogaClassTypeRequestValidator()
    {
        RuleFor(x => x.TypeName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng nhập tên loại lớp.")
            .MaximumLength(100).WithMessage("Tối đa 100 ký tự.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Tối đa 500 ký tự.");
    }
}

/// <summary>GBR-73 completeness and GBR-75 duration.</summary>
public sealed class YogaClassRequestValidator : AbstractValidator<YogaClassRequest>
{
    public YogaClassRequestValidator()
    {
        RuleFor(x => x.YogaClassTypeId).GreaterThan(0).WithMessage("Vui lòng chọn loại lớp.");
        RuleFor(x => x.ClassName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng nhập tên lớp.")
            .MaximumLength(256).WithMessage("Tối đa 256 ký tự.");
        RuleFor(x => x.InstructorUserId)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng chọn huấn luyện viên.");
        RuleFor(x => x.DifficultyLevel)
            .Must(YogaRules.IsDifficulty).WithMessage("Độ khó phải là Cơ bản, Trung bình hoặc Nâng cao.");
        RuleFor(x => x.SessionsPerWeek).InclusiveBetween(1, 7)
            .WithMessage("Vui lòng nhập số hợp lệ từ 1 đến 7.");
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1)
            .WithMessage("Số học viên tối thiểu phải từ 1 trở lên.");
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants)
            .WithMessage("Số học viên tối đa phải lớn hơn hoặc bằng số tối thiểu.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(YogaRules.MinSessionMinutes, YogaRules.MaxSessionMinutes)
            .WithMessage("Vui lòng nhập số hợp lệ từ 45 đến 90.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        RuleFor(x => x.RegistrationDeadline)
            .Must((req, deadline) => DateOnly.FromDateTime(deadline.AddHours(7)) <= req.StartDate)
            .WithMessage("Hạn đăng ký phải bằng hoặc trước ngày bắt đầu.");
        RuleFor(x => x.Description).MaximumLength(1000).WithMessage("Tối đa 1000 ký tự.");
        RuleFor(x => x.ImageUrl).MaximumLength(500).WithMessage("Tối đa 500 ký tự.");
    }
}

public sealed class YogaSessionRequestValidator : AbstractValidator<YogaSessionRequest>
{
    public YogaSessionRequestValidator()
    {
        RuleFor(x => x.YogaClassId).GreaterThan(0).WithMessage("Vui lòng chọn lớp.");
        RuleFor(x => x.YogaStudioId).GreaterThan(0).WithMessage("Vui lòng chọn phòng.");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("Giờ kết thúc phải sau giờ bắt đầu.");
        RuleFor(x => x)
            .Must(x => (x.EndTime - x.StartTime).TotalMinutes is >= YogaRules.MinSessionMinutes and <= YogaRules.MaxSessionMinutes)
            .When(x => x.EndTime > x.StartTime)
            .WithName("EndTime")
            .WithMessage("Mỗi buổi học phải dài từ 45 đến 90 phút.");
    }
}

public sealed class YogaWeeklySessionsRequestValidator : AbstractValidator<YogaWeeklySessionsRequest>
{
    public YogaWeeklySessionsRequestValidator()
    {
        RuleFor(x => x.YogaStudioId).GreaterThan(0).WithMessage("Vui lòng chọn phòng.");
        RuleFor(x => x.Days).NotEmpty().WithMessage("Hãy chọn ít nhất một thứ trong tuần.");
        RuleFor(x => x.Days).Must(d => d.Distinct().Count() == d.Count && d.All(Enum.IsDefined))
            .When(x => x.Days is { Count: > 0 }).WithMessage("Các thứ trong tuần không hợp lệ hoặc bị trùng.");
    }
}

public sealed class YogaBookingRequestValidator : AbstractValidator<YogaBookingRequest>
{
    public YogaBookingRequestValidator()
    {
        RuleFor(x => x.YogaClassId).GreaterThan(0);
        RuleFor(x => x.MatNumber).InclusiveBetween(1, 500).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.MatNumber is not null);
    }
}

public sealed class SubmitYogaAttendanceRequestValidator : AbstractValidator<SubmitYogaAttendanceRequest>
{
    public SubmitYogaAttendanceRequestValidator()
    {
        RuleFor(x => x.Marks).NotNull()
            .Must(m => m.Select(x => x.BookingId).Distinct().Count() == m.Count).WithMessage("Mỗi lượt đặt chỗ chỉ được điểm danh một lần.");
        RuleForEach(x => x.Marks).ChildRules(m => m.RuleFor(x => x.BookingId).GreaterThan(0));
    }
}

public sealed class YogaAttendanceQrRequestValidator : AbstractValidator<YogaAttendanceQrRequest>
{
    public YogaAttendanceQrRequestValidator() => RuleFor(x => x.QrToken).NotEmpty().WithMessage("Hãy quét mã QR.").MaximumLength(128).WithMessage("Tối đa 128 ký tự.");
}
