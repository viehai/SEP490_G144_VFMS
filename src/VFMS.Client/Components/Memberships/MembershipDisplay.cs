using System.Globalization;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Components.Memberships;

internal static class MembershipDisplay
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public static string DateInput(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Status(string status) => status switch
    {
        MembershipStatuses.AwaitingActivation => "Chưa chọn ngày kích hoạt",
        MembershipStatuses.Scheduled => "Đã hẹn kích hoạt",
        MembershipStatuses.Active => "Đang hoạt động",
        MembershipStatuses.Frozen => "Đang bảo lưu",
        MembershipStatuses.Expired => "Đã hết hạn",
        MembershipStatuses.Suspended => "Đang tạm ngưng",
        MembershipStatuses.Cancelled => "Đã hủy",
        _ => status
    };
}
