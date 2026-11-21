using FluentValidation;
using VFMS.Contracts.Memberships;

namespace VFMS.Contracts.PersonalTraining;

// Syntax rules shared by the API and the Blazor client; schedule and contract state rules stay in the Application layer.
public sealed class PTPublishRequestValidator : AbstractValidator<PTPublishRequest>
{
    public PTPublishRequestValidator()
    {
        RuleFor(x => x.Slots).NotNull().Must(slots => slots.Length <= 168)
            .WithMessage("Mẫu lịch tối đa 168 ca mỗi tuần.");
        RuleForEach(x => x.Slots).NotNull().Must(slot => Enum.IsDefined(slot.DayOfWeek))
            .WithMessage("Ca có ngày trong tuần không hợp lệ.");
    }
}

public sealed class PTPackageRequestValidator : AbstractValidator<PTPackageRequest>
{
    /// <summary>UC 4.3: a package has 1 to 50 sessions.</summary>
    public const int MaxSessions = 50;

    public PTPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên gói là bắt buộc.").MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Credits).InclusiveBetween(1, MaxSessions).WithMessage($"Vui lòng nhập số buổi từ 1 đến {MaxSessions}.");
        RuleFor(x => x.Price).GreaterThan(0).Must(price => decimal.Truncate(price) == price)
            .WithMessage("Vui lòng nhập giá là số nguyên lớn hơn 0 (VND).");
        RuleFor(x => x.TrainerLevel).Must(level => level is "Junior" or "Senior" or "Master")
            .WithMessage("Chọn cấp PT Junior, Senior hoặc Master.");
        RuleFor(x => x.FreezeMinimumDays).GreaterThan(0).WithMessage("Vui lòng nhập số ngày bảo lưu tối thiểu lớn hơn 0.");
        RuleFor(x => x.FreezeMaxTotalDays).GreaterThanOrEqualTo(x => x.FreezeMinimumDays)
            .WithMessage("Vui lòng nhập số ngày bảo lưu tối đa từ số ngày tối thiểu trở lên.");
    }
}

public sealed class PTPackageStatusRequestValidator : AbstractValidator<PTPackageStatusRequest>
{
    public PTPackageStatusRequestValidator() => RuleFor(x => x.IsActive).NotNull();
}

public sealed class PTBookingRequestValidator : AbstractValidator<PTBookingRequest>
{
    public PTBookingRequestValidator()
    {
        RuleFor(x => x.TrainerId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.StartDate).Must(date => date.DayOfWeek == DayOfWeek.Monday)
            .WithMessage("Ngày bắt đầu phải là thứ Hai.");
        RuleFor(x => x.TemplateIds).NotNull()
            .Must(ids => ids.Length is >= 1 and <= 7 && ids.Distinct().Count() == ids.Length)
            .WithMessage("Chọn từ 1 đến 7 ca khác nhau.");
    }
}

public sealed class PTBookingSlotsRequestValidator : AbstractValidator<PTBookingSlotsRequest>
{
    public PTBookingSlotsRequestValidator()
    {
        RuleFor(x => x.TrainerId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.TemplateIds).NotNull().Must(ids => ids.Length <= 7);
    }
}

public sealed class PTPaymentRequestValidator : AbstractValidator<PTPaymentRequest>
{
    public PTPaymentRequestValidator()
    {
        RuleFor(x => x.VoucherCode).MaximumLength(80);
        RuleFor(x => x.PaymentMethod).Must(m => m is null or MembershipPaymentMethods.VietQr or MembershipPaymentMethods.Cash)
            .WithMessage("Chọn VietQR hoặc thanh toán tại quầy.");
    }
}

public sealed class PTCashConfirmationRequestValidator : AbstractValidator<PTCashConfirmationRequest>
{
    public PTCashConfirmationRequestValidator() => RuleFor(x => x.AmountReceived).GreaterThan(0);
}

public sealed class PTProposalRequestValidator : AbstractValidator<PTProposalRequest>
{
    public PTProposalRequestValidator() => RuleFor(x => x.Type).NotEmpty();
}

public sealed class PTNoteRequestValidator : AbstractValidator<PTNoteRequest>
{
    public PTNoteRequestValidator() =>
        RuleFor(x => x.Note).NotEmpty().WithMessage("Nhập ghi chú buổi tập.").MaximumLength(2000);
}

public sealed class PTTrainerAbsenceReportRequestValidator : AbstractValidator<PTTrainerAbsenceReportRequest>
{
    public PTTrainerAbsenceReportRequestValidator() => RuleFor(x => x.Reason).MaximumLength(1000);
}

public sealed class PTAttendanceDisputeDecisionValidator : AbstractValidator<PTAttendanceDisputeDecision>
{
    public PTAttendanceDisputeDecisionValidator()
    {
        RuleFor(x => x.Decision).Must(decision => PTDisputeDecisions.All.Contains(decision))
            .WithMessage("Chọn Buổi đã diễn ra, PT vắng mặt hoặc Hội viên vắng mặt.");
        RuleFor(x => x.Note).Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("Nhập ghi chú quyết định.")
            .MaximumLength(1000);
    }
}

public sealed class PTFreezeRequestValidator : AbstractValidator<PTFreezeRequest>
{
    public PTFreezeRequestValidator()
    {
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Nhập lý do bảo lưu.").MaximumLength(1000);
        RuleFor(x => x.ResumeDate).GreaterThan(x => x.StartDate)
            .WithMessage("Ngày tập lại phải sau ngày bắt đầu bảo lưu.");
        RuleFor(x => x.Moves).Must(moves => moves == null ||
                moves.All(m => m is not null) && moves.Select(m => m.SessionId).Distinct().Count() == moves.Length)
            .WithMessage("Mỗi buổi chỉ được chọn một ca sau bảo lưu.");
        RuleFor(x => x.ReplacementTemplateIds).Must(ids => ids is { Length: >= 1 and <= 7 } && ids.Distinct().Count() == ids.Length)
            .When(x => x.ReplacementTrainerId != null).WithMessage("Chọn từ 1 đến 7 ca khác nhau của huấn luyện viên mới.");
    }
}

public sealed class PTTrainerChangeRequestValidator : AbstractValidator<PTTrainerChangeRequest>
{
    public PTTrainerChangeRequestValidator()
    {
        RuleFor(x => x.TrainerId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.TemplateIds).NotNull().Must(ids => ids.Length is >= 1 and <= 7 && ids.Distinct().Count() == ids.Length)
            .WithMessage("Chọn từ 1 đến 7 ca khác nhau của huấn luyện viên mới.");
    }
}

public sealed class PTFreezeDecisionValidator : AbstractValidator<PTFreezeDecision>
{
    public PTFreezeDecisionValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason)).When(x => !x.Approve)
            .WithMessage("Nhập lý do từ chối.");
    }
}

public sealed class PTEarlyResumeConfirmationValidator : AbstractValidator<PTEarlyResumeConfirmation>
{
    public PTEarlyResumeConfirmationValidator() => RuleFor(x => x.Moves).NotNull();
}

public sealed class PTReviewRequestValidator : AbstractValidator<PTReviewRequest>
{
    public PTReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Chọn từ 1 đến 5 sao.");
        RuleFor(x => x.Comment).NotEmpty().Must(comment => comment.Trim().Length is >= 10 and <= 500)
            .WithMessage("Nhận xét phải từ 10 đến 500 ký tự.");
    }
}
