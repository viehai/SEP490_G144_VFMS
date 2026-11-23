using FluentValidation;
using VFMS.Contracts.Memberships;

namespace VFMS.Contracts.PersonalTraining;

// Syntax rules shared by the API and the Blazor client; schedule and contract state rules stay in the Application layer.
public sealed class PTPublishRequestValidator : AbstractValidator<PTPublishRequest>
{
    public PTPublishRequestValidator()
    {
        RuleFor(x => x.Slots).NotNull().WithMessage("Vui lòng cung cấp danh sách ca tập.").Must(slots => slots.Length <= 168)
            .WithMessage("Mẫu lịch tối đa 168 ca mỗi tuần.");
        RuleForEach(x => x.Slots).NotNull().WithMessage("Vui lòng cung cấp danh sách ca tập.").Must(slot => Enum.IsDefined(slot.DayOfWeek))
            .WithMessage("Ca có ngày trong tuần không hợp lệ.");
    }
}

public sealed class PTPackageRequestValidator : AbstractValidator<PTPackageRequest>
{
    /// <summary>UC 4.3: a package has 1 to 50 sessions.</summary>
    public const int MaxSessions = 50;

    public PTPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên gói là bắt buộc.").MaximumLength(256).WithMessage("Tên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Mô tả không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.Credits).InclusiveBetween(1, MaxSessions).WithMessage($"Vui lòng nhập số buổi từ 1 đến {MaxSessions}.");
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Giá phải lớn hơn {ComparisonValue}.").Must(price => decimal.Truncate(price) == price)
            .WithMessage("Vui lòng nhập giá là số nguyên lớn hơn 0 (VND).");
        RuleFor(x => x.TrainerLevel).Must(level => level is "Junior" or "Senior" or "Master")
            .WithMessage("Chọn cấp huấn luyện viên sơ cấp, cao cấp hoặc chuyên gia.");
        RuleFor(x => x.FreezeMinimumDays).GreaterThan(0).WithMessage("Vui lòng nhập số ngày bảo lưu tối thiểu lớn hơn 0.");
        RuleFor(x => x.FreezeMaxTotalDays).GreaterThanOrEqualTo(x => x.FreezeMinimumDays)
            .WithMessage("Vui lòng nhập số ngày bảo lưu tối đa từ số ngày tối thiểu trở lên.");
    }
}

public sealed class PTPackageStatusRequestValidator : AbstractValidator<PTPackageStatusRequest>
{
    public PTPackageStatusRequestValidator() => RuleFor(x => x.IsActive).NotNull().WithMessage("Vui lòng cung cấp trạng thái mở bán.");
}

public sealed class PTBookingRequestValidator : AbstractValidator<PTBookingRequest>
{
    public PTBookingRequestValidator()
    {
        RuleFor(x => x.TrainerId).NotEmpty().WithMessage("Mã huấn luyện viên không được để trống.").MaximumLength(450).WithMessage("Mã huấn luyện viên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Vui lòng chọn ngày bắt đầu.");
        RuleFor(x => x.TemplateIds).NotNull().WithMessage("Vui lòng cung cấp danh sách ca tập.")
            .Must(ids => ids.Length is >= 1 and <= 7 && ids.Distinct().Count() == ids.Length)
            .WithMessage("Chọn từ 1 đến 7 ca khác nhau.");
    }
}

public sealed class PTBookingSlotsRequestValidator : AbstractValidator<PTBookingSlotsRequest>
{
    public PTBookingSlotsRequestValidator()
    {
        RuleFor(x => x.TrainerId).NotEmpty().WithMessage("Mã huấn luyện viên không được để trống.").MaximumLength(450).WithMessage("Mã huấn luyện viên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.TemplateIds).NotNull().WithMessage("Vui lòng cung cấp danh sách ca tập.").Must(ids => ids.Length <= 7).WithMessage("Danh sách ca tập không hợp lệ. Vui lòng kiểm tra lại.");
    }
}

public sealed class PTPaymentRequestValidator : AbstractValidator<PTPaymentRequest>
{
    public PTPaymentRequestValidator()
    {
        RuleFor(x => x.VoucherCode).MaximumLength(80).WithMessage("Mã giảm giá không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.PaymentMethod).Must(m => m is null or MembershipPaymentMethods.VietQr or MembershipPaymentMethods.Cash)
            .WithMessage("Chọn VietQR hoặc thanh toán tại quầy.");
    }
}

public sealed class PTCashConfirmationRequestValidator : AbstractValidator<PTCashConfirmationRequest>
{
    public PTCashConfirmationRequestValidator() => RuleFor(x => x.AmountReceived).GreaterThan(0).WithMessage("Số tiền nhận phải lớn hơn {ComparisonValue}.");
}

public sealed class PTProposalRequestValidator : AbstractValidator<PTProposalRequest>
{
    public PTProposalRequestValidator() => RuleFor(x => x.Type).NotEmpty().WithMessage("Loại đề xuất không được để trống.");
}

public sealed class PTNoteRequestValidator : AbstractValidator<PTNoteRequest>
{
    public PTNoteRequestValidator() =>
        RuleFor(x => x.Note).NotEmpty().WithMessage("Nhập ghi chú buổi tập.").MaximumLength(2000).WithMessage("Ghi chú không được vượt quá {MaxLength} ký tự.");
}

public sealed class PTTrainerAbsenceReportRequestValidator : AbstractValidator<PTTrainerAbsenceReportRequest>
{
    public PTTrainerAbsenceReportRequestValidator() => RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Lý do không được vượt quá {MaxLength} ký tự.");
}

public sealed class PTAttendanceDisputeDecisionValidator : AbstractValidator<PTAttendanceDisputeDecision>
{
    public PTAttendanceDisputeDecisionValidator()
    {
        RuleFor(x => x.Decision).Must(decision => PTDisputeDecisions.All.Contains(decision))
            .WithMessage("Chọn Buổi đã diễn ra, PT vắng mặt hoặc Hội viên vắng mặt.");
        RuleFor(x => x.Note).Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("Nhập ghi chú quyết định.")
            .MaximumLength(1000).WithMessage("Ghi chú không được vượt quá {MaxLength} ký tự.");
    }
}

public sealed class PTFreezeRequestValidator : AbstractValidator<PTFreezeRequest>
{
    public PTFreezeRequestValidator()
    {
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Nhập lý do bảo lưu.").MaximumLength(1000).WithMessage("Lý do không được vượt quá {MaxLength} ký tự.");
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
        RuleFor(x => x.TrainerId).NotEmpty().WithMessage("Mã huấn luyện viên không được để trống.").MaximumLength(450).WithMessage("Mã huấn luyện viên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.TemplateIds).NotNull().WithMessage("Vui lòng cung cấp danh sách ca tập.").Must(ids => ids.Length is >= 1 and <= 7 && ids.Distinct().Count() == ids.Length)
            .WithMessage("Chọn từ 1 đến 7 ca khác nhau của huấn luyện viên mới.");
    }
}

public sealed class PTFreezeSlotChangeValidator : AbstractValidator<PTFreezeSlotChange>
{
    public PTFreezeSlotChangeValidator() => RuleFor(x => x.Moves).Must(moves => moves is { Length: > 0 } &&
            moves.All(m => m is not null) && moves.Select(m => m.SessionId).Distinct().Count() == moves.Length)
        .WithMessage("Chọn ca mới cho ít nhất một buổi, mỗi buổi một ca.");
}

public sealed class PTFreezeDecisionValidator : AbstractValidator<PTFreezeDecision>
{
    public PTFreezeDecisionValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Lý do không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason)).When(x => !x.Approve)
            .WithMessage("Nhập lý do từ chối.");
    }
}

public sealed class PTEarlyResumeConfirmationValidator : AbstractValidator<PTEarlyResumeConfirmation>
{
    public PTEarlyResumeConfirmationValidator() => RuleFor(x => x.Moves).NotNull().WithMessage("Vui lòng cung cấp danh sách ca mới.");
}

public sealed class PTReviewRequestValidator : AbstractValidator<PTReviewRequest>
{
    public PTReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Chọn từ 1 đến 5 sao.");
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Nhận xét không được để trống.").Must(comment => comment.Trim().Length is >= 10 and <= 500)
            .WithMessage("Nhận xét phải từ 10 đến 500 ký tự.");
    }
}
