using VFMS.Contracts.Lockers;

namespace VFMS.Client.Services;

/// <summary>Vietnamese labels and display rules for the locker pages (spec v3, UC 5.1-5.7).</summary>
public static class LockerText
{
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    // Rental durations offered in the price list: Daily, then 1, 3, 6 and 12 months.
    public static string Duration(LockerPolicy policy) => policy.Type == "Daily" ? "Theo ngày" : $"{policy.Months} tháng";

    public static string Slot(string status) => status switch
    {
        "Available" => "Còn trống",
        "Reserved" => "Đang giữ",
        "Occupied" => "Đang thuê",
        "Overdue" => "Quá hạn",
        "Maintenance" => "Bảo trì",
        "Inactive" => "Ngừng dùng",
        _ => status
    };

    /// <summary>The StatusBadge key whose colour matches the slot state.</summary>
    public static string SlotTone(string status) => status switch
    {
        "Available" => "Available",
        "Reserved" => "Pending",
        "Overdue" => "Overdue",
        _ => "Inactive"
    };

    public static string Contract(string status) => status switch
    {
        "PendingPayment" => "Chờ thanh toán",
        "PendingHandover" => "Chờ nhận chìa",
        "Active" => "Đang thuê",
        "Overdue" => "Quá hạn",
        "Expired" => "Hết hạn",
        "Terminated" => "Đã kết thúc",
        "Abandoned" => "Đã dọn tủ",
        "Cancelled" => "Đã hủy",
        _ => status
    };

    public static string ContractTone(string status) => status switch
    {
        "Active" => "Active",
        "PendingPayment" or "PendingHandover" => "Pending",
        "Overdue" or "Abandoned" => "Overdue",
        _ => "Inactive"
    };

    public static string Order(string status) => status switch
    {
        "Pending" => "Chờ thanh toán",
        "AwaitingConfirmation" => "Chờ xác nhận giao dịch",
        "Paid" => "Đã thanh toán",
        "Expired" => "Hết hạn",
        "Cancelled" => "Đã hủy",
        "RefundPending" => "Chờ xử lý hoàn tiền",
        "Refunded" => "Đã hoàn tiền",
        "RefundFailed" => "Hoàn tiền thất bại",
        _ => status
    };

    public static string OrderTone(string status) => status switch
    {
        "Paid" => "Paid",
        "Pending" or "AwaitingConfirmation" or "RefundPending" => "Pending",
        "Expired" or "RefundFailed" => "Expired",
        _ => "Inactive"
    };

    /// <summary>Time left until a UTC deadline as m:ss, or "0:00" once it has passed.</summary>
    public static string Remaining(DateTime utcDeadline, DateTime utcNow)
    {
        var left = DateTime.SpecifyKind(utcDeadline, DateTimeKind.Utc) - utcNow;
        if (left <= TimeSpan.Zero)
            return "0:00";
        return $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    /// <summary>Gym local time (UTC+7), independent of the browser time zone.</summary>
    public static string Local(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(GymOffset).ToString("HH:mm dd/MM/yyyy");

    public static string LocalDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(GymOffset).ToString("dd/MM/yyyy");

    /// <summary>The last day of a term is shown as a date: the expiry instant is midnight after it.</summary>
    public static string LastDay(DateTime utcExpiry) => LocalDate(DateTime.SpecifyKind(utcExpiry, DateTimeKind.Utc).AddDays(-1));
}
