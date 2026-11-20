using VFMS.Contracts.Equipment;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.Supplements;

namespace VFMS.Client.Services;

/// <summary>The columns of each list's CSV export (equipment inventory, supplement orders, audit logs).</summary>
public static class ListExports
{
    public static byte[] Equipment(IEnumerable<EquipmentResponse> rows) => CsvFile.Build(
        ["Mã", "Tên thiết bị", "Danh mục", "Hãng", "Model", "Trạng thái", "Ngày mua", "Giá mua (VND)", "Sự cố 90 ngày", "Ghi chú"],
        rows.Select(e => (IReadOnlyList<string?>)
        [
            e.Code, e.Name, e.CategoryName, e.Brand, e.Model, OperationsText.Label(e.Status), CsvFile.Date(e.PurchaseDate),
            e.PurchasePrice is { } price ? CsvFile.Number(price) : "", e.RecentIssueCount.ToString(), e.Note,
        ]));

    public static byte[] SupplementOrders(IEnumerable<SupplementOrderResponse> rows) => CsvFile.Build(
        ["Mã đơn", "Đặt lúc", "Khách hàng", "Hình thức", "Trạng thái", "Số sản phẩm", "Tổng tiền (VND)", "Sản phẩm",
         "Người nhận", "Điện thoại", "Địa chỉ", "Đơn vị vận chuyển", "Mã vận đơn", "Hoàn tất lúc", "Hủy lúc"],
        rows.Select(o => (IReadOnlyList<string?>)
        [
            o.OrderNumber, CsvFile.Time(o.PlacedAt), o.CustomerName, OperationsText.Label(o.FulfillmentType), OperationsText.Label(o.Status),
            o.Items.Sum(i => i.Quantity).ToString(), CsvFile.Number(o.TotalAmount),
            string.Join("; ", o.Items.Select(i => $"{i.ProductName} × {i.Quantity}")),
            o.Delivery?.RecipientName, o.Delivery?.ContactPhone, o.Delivery?.DeliveryAddress, o.Delivery?.CourierName,
            o.Delivery?.TrackingCode, CsvFile.Time(o.CompletedAt), CsvFile.Time(o.CancelledAt),
        ]));

    public static byte[] AuditLogs(IEnumerable<AuditLogResponse> rows, Func<string, string> actionLabel) => CsvFile.Build(
        ["Thời gian", "Người thực hiện", "Mã người dùng", "Thao tác", "Đối tượng", "ID đối tượng", "Giá trị cũ", "Giá trị mới", "Địa chỉ IP"],
        rows.Select(log => (IReadOnlyList<string?>)
        [
            CsvFile.Time(log.OccurredAt), log.ActorName, log.ActorUserId, actionLabel(log.ActionType), log.AuditEntity,
            log.EntityId?.ToString(), log.OldValue, log.NewValue, log.IpAddress,
        ]));
}
