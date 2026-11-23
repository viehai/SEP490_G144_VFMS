using System.Globalization;
using VFMS.Client.Services;

namespace VFMS.Client.Components.Memberships;

public static class TransactionDisplay
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static string Money(decimal amount) => Vnd.Format(amount);

    public static string Date(DateTimeOffset timestamp) =>
        timestamp.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", VietnameseCulture);

    public static string Method(string method) => method switch
    {
        "VietQR" => "VietQR / SePay",
        "Cash" => "Tiền mặt tại quầy",
        "BankTransfer" => "Chuyển khoản",
        "NoCharge" => "Không thu tiền",
        _ => string.IsNullOrWhiteSpace(method) ? "Chưa ghi nhận" : method
    };

    public static string Status(string status) => status switch
    {
        "Pending" => "Chờ thanh toán",
        "Paid" => "Đã thanh toán",
        "Failed" => "Thất bại",
        "Cancelled" => "Đã hủy",
        "Expired" => "Hết hạn",
        "Refunded" => "Đã hoàn tiền",
        "Issued" => "Đã phát hành",
        "Voided" => "Đã hủy",
        "Reserved" => "Đã giữ mã",
        "Confirmed" => "Đã sử dụng",
        "Released" => "Đã giải phóng",
        _ => status
    };
}
