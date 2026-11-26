using System.Globalization;

namespace VFMS.Client.Services;

internal static class PTDisplay
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    internal static string Money(decimal value) => Vnd.Format(value);
    internal static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", Vietnamese);
    internal static string Time(TimeOnly value) => value.ToString("HH:mm", Vietnamese);
    internal static string LocalTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7).ToString("HH:mm dd/MM/yyyy", Vietnamese);
    internal static string LocalDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy", Vietnamese);
    internal static string Day(DayOfWeek day) => day == DayOfWeek.Sunday ? "Chủ nhật" : $"Thứ {(int)day + 1}";
    internal static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    internal static string Level(string value) => value switch
    {
        "Junior" => "Sơ cấp",
        "Senior" => "Cao cấp",
        "Master" => "Chuyên gia",
        _ => value
    };

    internal static string StatusTone(string value) => value switch
    {
        "Scheduled" => "blue",
        "AwaitingAttendance" => "amber",
        "Completed" or "AutoCompleted" => "green",
        "ReschedulePending" or "RescheduleRequested" or "PendingFreeze" or "MakeUpPending" or "MakeUpRequested" => "amber",
        "Cancelled" or "NoShow" => "red",
        "Disputed" or "TrainerAbsentReported" => "purple",
        _ => "gray"
    };

    internal static string Status(string value) => value switch
    {
        "Active" => "Đang hoạt động",
        "Frozen" => "Đang bảo lưu",
        "Expired" => "Hết hạn",
        "Pending" => "Đang chờ",
        "PendingPayment" => "Chờ thanh toán",
        "Paid" => "Đã thanh toán",
        "AwaitingConfirmation" => "Chờ xác nhận chuyển khoản",
        "RefundPending" => "Chờ hoàn tiền",
        "Refunded" => "Đã hoàn tiền",
        "RefundFailed" => "Hoàn tiền thất bại",
        "Scheduled" => "Đã lên lịch",
        "AwaitingAttendance" => "Chờ PT điểm danh",
        "Completed" => "Hoàn thành",
        "Cancelled" => "Đã hủy",
        "NoShow" => "Vắng mặt",
        "PendingFreeze" => "Chờ duyệt bảo lưu",
        "Approved" => "Đã duyệt",
        "Rejected" => "Đã từ chối",
        "Blacklisted" => "Không đủ điều kiện",
        "ReschedulePending" => "Chờ hội viên phản hồi",
        "RescheduleRequested" => "Chờ PT phản hồi",
        "MakeUpPending" => "Chờ hội viên duyệt buổi bù",
        "MakeUpRequested" => "Chờ PT duyệt buổi bù",
        "CheckoutHold" => "Đang giữ chỗ",
        "FreezeHold" or "Held" => "Giữ chỗ sau bảo lưu",
        "AutoCompleted" => "Hoàn thành (tự động)",
        "Disputed" => "Đang tranh chấp",
        "Withdrawn" => "Đã rút",
        "TrainerAbsentReported" => "Chờ xử lý báo PT vắng mặt",
        "Available" => "Còn trống",
        "Busy" or "Booked" => "Bận",
        "Unavailable" => "Không khả dụng",
        "Past" => "Đã qua",
        "Proposed" => "Dự kiến",
        _ => value
    };
}
