using System.Globalization;

namespace VFMS.Client.Services;

/// <summary>Vietnamese labels, badge colours and error messages for equipment, procurement and supplements.</summary>
public static class OperationsText
{
    public static readonly string[] EquipmentStatuses = ["Operational", "UnderMaintenance", "Damaged", "Retired"];
    public static readonly string[] IncidentTypes = ["Malfunction", "Damage", "Safety", "Other"];
    public static readonly string[] IssueStatuses = ["Reported", "InProgress", "Escalated", "Resolved", "MarkedSafe"];
    public static readonly string[] ProposalStatuses = ["Draft", "Submitted", "Approved", "Rejected", "Completed"];
    public static readonly string[] ProductStatuses = ["Active", "Inactive", "OutOfStock"];
    public static readonly string[] OrderStatuses = ["Pending", "Processing", "ReadyForPickup", "OutForDelivery", "Completed", "Cancelled"];
    public static readonly string[] FulfillmentTypes = ["GymPickup", "HomeDelivery"];

    // Built by hand: WebAssembly may not ship vi-VN culture data, which would print "1,000" instead of "1.000".
    public static string Money(decimal value) => value.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.') + " ₫";
    public static string Local(DateTime value) => value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Label(string? value) => value switch
    {
        // Equipment
        "Operational" => "Hoạt động",
        "UnderMaintenance" => "Đang bảo trì",
        "Damaged" => "Hư hỏng",
        "Retired" => "Đã thanh lý",
        // Incident types
        "Malfunction" => "Trục trặc",
        "Damage" => "Hư hại",
        "Safety" => "Mất an toàn",
        "Other" => "Khác",
        // Issues
        "Reported" => "Mới báo",
        "InProgress" => "Đang xử lý",
        "Escalated" => "Chuyển quản lý",
        "Resolved" => "Đã xử lý",
        "MarkedSafe" => "Đã xác nhận an toàn",
        // Proposals
        "Draft" => "Bản nháp",
        "Submitted" => "Chờ duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Từ chối",
        "Completed" => "Hoàn tất",
        // Proposal review actions
        "Approve" => "Duyệt",
        "Reject" => "Từ chối",
        "RequestRevision" => "Yêu cầu sửa",
        "Complete" => "Hoàn tất",
        // Products
        "Active" => "Đang bán",
        "Inactive" => "Ngừng bán",
        "OutOfStock" => "Hết hàng",
        // Orders
        "Pending" => "Chờ xử lý",
        "Processing" => "Đang chuẩn bị",
        "ReadyForPickup" => "Chờ nhận tại quầy",
        "OutForDelivery" => "Đang giao",
        "Cancelled" => "Đã hủy",
        "GymPickup" => "Nhận tại phòng tập",
        "HomeDelivery" => "Giao tận nơi (COD)",
        null or "" => "—",
        _ => value
    };

    public static string Badge(string? status) =>
        "inline-block whitespace-nowrap rounded-md border px-2.5 py-1 text-[11px] font-semibold " + status switch
        {
            "Operational" or "Resolved" or "MarkedSafe" or "Approved" or "Completed" or "Active"
                => "border-emerald-200 bg-emerald-50 text-emerald-800",
            "UnderMaintenance" or "InProgress" or "Submitted" or "Processing" or "ReadyForPickup" or "OutForDelivery"
                => "border-amber-200 bg-amber-50 text-amber-800",
            "Damaged" or "Escalated" or "Rejected" or "Reported" or "Pending" or "OutOfStock" or "Safety"
                => "border-red-200 bg-red-50 text-red-800",
            _ => "border-[#D8DED2] bg-[#EEF1EA] text-[#69706A]"
        };

    /// <summary>Vietnamese text for an API error code, or null to keep the server's message.</summary>
    public static string? ErrorMessage(string? code) => code switch
    {
        "EQUIPMENT_NOT_FOUND" => "Không tìm thấy thiết bị.",
        "EQUIPMENT_CODE_EXISTS" => "Mã thiết bị đã tồn tại.",
        "EQUIPMENT_CODE_INVALID_FORMAT" => "Mã thiết bị phải theo dạng TIỀN TỐ-0001 của danh mục đã chọn, hoặc để trống để tự tạo.",
        "EQUIPMENT_CATEGORY_PREFIX_MISSING" => "Danh mục này chưa có tiền tố mã. Hãy cập nhật danh mục trước.",
        "EQUIPMENT_CATEGORY_PREFIX_EXISTS" => "Tiền tố mã đã được danh mục khác sử dụng.",
        "EQUIPMENT_CATEGORY_NOT_FOUND" => "Không tìm thấy danh mục thiết bị.",
        "EQUIPMENT_CATEGORY_EXISTS" => "Tên danh mục đã tồn tại.",
        "EQUIPMENT_CATEGORY_IN_USE" => "Danh mục đang có thiết bị nên không thể xóa.",
        "EQUIPMENT_STATUS_UNCHANGED" => "Thiết bị đã ở trạng thái này.",
        "EQUIPMENT_RETIRED" => "Thiết bị đã thanh lý, không thể thao tác.",
        "EQUIPMENT_NOTE_REQUIRED" => "Cần nhập lý do khi thanh lý thiết bị.",
        "EQUIPMENT_HAS_OPEN_ISSUE" => "Thiết bị đang có sự cố chưa xử lý xong.",
        "EQUIPMENT_INVALID_STATUS" or "EQUIPMENT_ISSUE_INVALID_STATUS" or "PROPOSAL_INVALID_STATUS"
            or "SUPPLEMENT_PRODUCT_INVALID_STATUS" or "SUPPLEMENT_ORDER_INVALID_STATUS" => "Trạng thái không hợp lệ.",
        "EQUIPMENT_ISSUE_INVALID_TYPE" => "Loại sự cố không hợp lệ.",
        "EQUIPMENT_ISSUE_DUPLICATE" => "Thiết bị này đã có báo cáo sự cố đang mở.",
        "EQUIPMENT_ISSUE_NOT_FOUND" => "Không tìm thấy báo cáo sự cố.",
        "EQUIPMENT_ISSUE_INVALID_STATE" => "Báo cáo sự cố không ở trạng thái cho phép thao tác này.",
        "EQUIPMENT_ISSUE_UNDER_REPAIR" => "Thiết bị đang được quản lý sửa chữa.",
        "PROPOSAL_INCOMPLETE" => "Đề xuất cần mô tả và ít nhất một hạng mục trước khi gửi.",
        "PROPOSAL_NOTE_REQUIRED" => "Cần nhập ghi chú khi từ chối hoặc yêu cầu sửa.",
        "PROPOSAL_EQUIPMENT_NOT_FOUND" => "Thiết bị liên quan không tồn tại.",
        "PROPOSAL_INVALID_STATE" => "Đề xuất không ở trạng thái cho phép thao tác này.",
        "PROPOSAL_NOT_FOUND" => "Không tìm thấy đề xuất mua sắm.",
        "SUPPLEMENT_PRODUCT_NOT_FOUND" => "Không tìm thấy sản phẩm.",
        "SUPPLEMENT_PRODUCT_NAME_EXISTS" => "Tên sản phẩm đã tồn tại.",
        "SUPPLEMENT_PRODUCT_STATUS_UNCHANGED" => "Sản phẩm đã ở trạng thái này.",
        "SUPPLEMENT_PRODUCT_UNAVAILABLE" => "Có sản phẩm trong giỏ đã ngừng bán hoặc hết hàng.",
        "SUPPLEMENT_ORDER_INVALID_FULFILLMENT" => "Hình thức nhận hàng không hợp lệ.",
        "SUPPLEMENT_ORDER_DELIVERY_REQUIRED" => "Giao tận nơi cần tên người nhận và địa chỉ.",
        "SUPPLEMENT_ORDER_QUANTITY_INVALID" => "Số lượng mỗi sản phẩm từ 1 đến 999.",
        "SUPPLEMENT_ORDER_DELIVERY_INFO_REQUIRED" => "Cần nhập đơn vị vận chuyển và mã vận đơn.",
        "SUPPLEMENT_ORDER_CANCELLED" => "Đơn đã bị khách hủy, không thể thao tác.",
        "SUPPLEMENT_ORDER_WRONG_FULFILLMENT" => "Thao tác không phù hợp với hình thức nhận hàng của đơn.",
        "SUPPLEMENT_ORDER_INVALID_DATE_RANGE" => "Ngày bắt đầu không được sau ngày kết thúc.",
        "SUPPLEMENT_ORDER_INVALID_STATE" => "Đơn hàng không ở trạng thái cho phép thao tác này.",
        "SUPPLEMENT_ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng.",
        _ => null
    };

    /// <summary>Vietnamese text for the contract validators' messages (falls back to the original).</summary>
    public static string Validation(string message) => message switch
    {
        "The Name field is required." => "Vui lòng nhập tên.",
        "Purchase date cannot be in the future." => "Ngày mua không được ở tương lai.",
        "Description URL must be a valid HTTP or HTTPS URL." => "Đường dẫn mô tả phải là liên kết http(s) hợp lệ.",
        "A reason is required to retire equipment." => "Cần nhập lý do khi thanh lý thiết bị.",
        "The Equipment field is required." => "Vui lòng chọn thiết bị.",
        "The Category field is required." => "Vui lòng chọn danh mục.",
        "The Code Prefix field is required." => "Vui lòng nhập tiền tố mã.",
        "Code prefix must be 2 to 5 letters." => "Tiền tố mã gồm 2 đến 5 chữ cái, VD: TM.",
        "The Description field is required." => "Vui lòng nhập mô tả.",
        "Image URL must be a valid HTTP or HTTPS URL." => "Ảnh phải là liên kết http(s) hợp lệ.",
        "The Note field is required." => "Vui lòng nhập ghi chú.",
        "The Item Name field is required." => "Vui lòng nhập tên hạng mục.",
        "Quantity must be between 1 and 10000." => "Số lượng từ 1 đến 10.000.",
        "Estimated unit price must be at least 1 VND." => "Đơn giá dự kiến tối thiểu 1 ₫.",
        "At least one item is required." => "Cần ít nhất một hạng mục.",
        "A proposal can contain at most 50 items." => "Một đề xuất tối đa 50 hạng mục.",
        "The Image field is required." => "Vui lòng nhập ảnh sản phẩm.",
        "The image must be an http(s) link to a png, jpg or jpeg file." => "Ảnh phải là liên kết http(s) tới tệp png, jpg hoặc jpeg.",
        "Unit price must be at least 1 VND." => "Giá bán tối thiểu 1 ₫.",
        "Quantity must be between 1 and 999." => "Số lượng từ 1 đến 999.",
        "The cart must contain at least one product." => "Giỏ hàng chưa có sản phẩm.",
        "An order can contain at most 50 products." => "Một đơn tối đa 50 sản phẩm.",
        "The Recipient Name field is required for Home Delivery." => "Vui lòng nhập tên người nhận.",
        "The Delivery Address field is required for Home Delivery." => "Vui lòng nhập địa chỉ giao hàng.",
        "The start date cannot be after the end date." => "Ngày bắt đầu không được sau ngày kết thúc.",
        "The Courier Name field is required." => "Vui lòng nhập đơn vị vận chuyển.",
        "The Tracking Code field is required." => "Vui lòng nhập mã vận đơn.",
        _ when message.Contains("must be") && message.Contains("characters") => "Nội dung vượt quá độ dài cho phép.",
        _ => message
    };
}
