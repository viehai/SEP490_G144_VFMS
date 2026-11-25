using System.Globalization;

namespace VFMS.Client.Services;

/// <summary>Vietnamese labels, badge colours and error messages for equipment, procurement, supplements and system configuration.</summary>
public static class OperationsText
{
    public static readonly string[] EquipmentStatuses = ["Operational", "UnderMaintenance", "Damaged", "Retired"];
    public static readonly string[] IncidentTypes = ["Malfunction", "Damage", "Safety", "Other"];
    public static readonly string[] IssueStatuses = ["InProgress", "Escalated", "Resolved"];
    public static readonly string[] ProposalStatuses = ["Draft", "Submitted", "Approved", "Rejected", "Completed"];
    public static readonly string[] ProductStatuses = ["Active", "Inactive", "OutOfStock"];
    public static readonly string[] OrderStatuses = ["Pending", "Processing", "ReadyForPickup", "OutForDelivery", "Completed", "Cancelled"];
    public static readonly string[] FulfillmentTypes = ["GymPickup", "HomeDelivery"];

    public static string RoleLabel(string role) => role switch {
        "Member" => "Hội viên", "Customer" => "Khách hàng", "PT" => "Huấn luyện viên PT", "YogaInstructor" => "Giáo viên Yoga",
        "Staff" => "Nhân viên", "Admin" => "Quản trị viên", "Manager" => "Quản lý", "Unassigned" => "Chưa có vai trò", _ => "Vai trò khác"
    };

    public static string AuditAction(string type) => type switch
    {
        "Create" => "Tạo mới",
        "Update" => "Cập nhật",
        "Delete" => "Xóa",
        "Login" => "Đăng nhập",
        "Logout" => "Đăng xuất",
        "RoleChange" => "Đổi vai trò",
        "AccountLock" => "Khóa tài khoản",
        "AccountUnlock" => "Mở khóa tài khoản",
        "PasswordChange" => "Đổi mật khẩu",
        "CreateMembershipPlan" => "Tạo gói hội viên",
        "UpdateMembershipPlan" => "Sửa gói hội viên",
        "ActivateMembershipPlan" => "Mở bán gói hội viên",
        "DeactivateMembershipPlan" => "Ngừng bán gói hội viên",
        "DeleteMembershipPlan" => "Xóa gói hội viên",
        _ => type
    };

    public static string Money(decimal value) => Vnd.Format(value);
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
        "InProgress" => "Đang xử lý",
        "Escalated" => "Chờ quản lý sửa",
        "Resolved" => "Đã xong",
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

    /// <summary>Semantic status badge (frontend guidelines 6.E): pill, tinted background, always paired with text.</summary>
    public static string Badge(string? status) =>
        "inline-block whitespace-nowrap rounded-full border px-2.5 py-1 text-[10px] font-bold uppercase tracking-wide " + status switch
        {
            "Operational" or "Resolved" or "Approved" or "Completed" or "Active"
                => "border-emerald-200 bg-emerald-50 text-[#16A34A]",
            "Pending" or "InProgress" or "Submitted" or "Processing" or "ReadyForPickup" or "OutForDelivery"
                or "UnderMaintenance" or "Draft"
                => "border-amber-200 bg-amber-50 text-[#D97706]",
            "Damaged" or "Escalated" or "Rejected" or "OutOfStock" or "Safety"
                => "border-red-200 bg-red-50 text-[#DC2626]",
            _ => "border-[#D8D1CD] bg-[#ECE8E4] text-[#6B7280]"
        };

    /// <summary>A status filter option with its count, e.g. "Đang xử lý (3)"; "Tất cả (12)" when <paramref name="status"/> is empty.</summary>
    public static string CountLabel(string? status, VFMS.Contracts.Operations.StatusCountsResponse? counts)
    {
        var label = string.IsNullOrEmpty(status) ? "Tất cả" : Label(status);
        if (counts is null) return label;
        var count = string.IsNullOrEmpty(status) ? counts.Total : counts.Counts.GetValueOrDefault(status);
        return $"{label} ({count})";
    }

    /// <summary>How long an open issue has waited, e.g. "Mở 9 ngày"; null once it is closed (Resolved).</summary>
    public static string? IssueAge(string status, DateTime reportedAtUtc, DateTime nowUtc)
    {
        if (status == "Resolved") return null;
        var age = nowUtc - reportedAtUtc;
        return age.TotalHours < 1 ? "Vừa báo" : age.TotalDays < 1 ? $"Mở {(int)age.TotalHours} giờ" : $"Mở {(int)age.TotalDays} ngày";
    }

    /// <summary>An open issue that has waited too long: a Safety issue after 1 day, any other after 3 days.</summary>
    public static bool IssueOverdue(string status, string incidentType, DateTime reportedAtUtc, DateTime nowUtc) =>
        status != "Resolved"
        && nowUtc - reportedAtUtc >= (incidentType == "Safety" ? TimeSpan.FromDays(1) : TimeSpan.FromDays(3));

    /// <summary>Vietnamese names for the entity types the audit log records, in the order of the "Đối tượng" filter.</summary>
    public static readonly (string Type, string Label)[] AuditEntities =
    [
        ("Equipment", "Thiết bị"), ("CatalogCategory", "Danh mục thiết bị"), ("EquipmentIssueReport", "Sự cố thiết bị"),
        ("PurchaseProposal", "Đề xuất mua sắm"), ("SupplementProduct", "Sản phẩm bổ sung"), ("SupplementOrder", "Đơn thực phẩm bổ sung"),
        ("Gym", "Thông tin phòng tập"), ("BannedWordList", "Từ khóa cấm"), ("BusinessReportSnapshot", "Báo cáo kinh doanh"),
        ("ApplicationUser", "Tài khoản"), ("Package", "Gói tập PT"), ("FreezePolicy", "Chính sách bảo lưu"),
        ("MembershipFreeze", "Bảo lưu hội viên"), ("PaymentTransaction", "Giao dịch thanh toán"), ("Voucher", "Mã giảm giá"),
        ("Competition", "Cuộc thi"), ("CompetitionJoin", "Đăng ký cuộc thi"), ("TrainerReview", "Đánh giá huấn luyện viên"),
        ("WorkoutPlan", "Kế hoạch tập luyện"), ("FaceData", "Dữ liệu khuôn mặt"), ("YogaClass", "Lớp Yoga"),
        ("YogaClassType", "Loại lớp Yoga"), ("YogaClassSchedule", "Lịch lớp Yoga"), ("YogaStudio", "Phòng Yoga"),
        ("YogaAttendance", "Điểm danh Yoga"),
    ];

    /// <summary>
    /// The audit log's "Đối tượng" in Vietnamese: "SupplementOrder" + 50 → "Đơn thực phẩm bổ sung #50". Some entries carry
    /// the type and a user id in one value ("ApplicationUser:3f2a…"); the id is left out, the actor column names the person.
    /// </summary>
    public static string AuditEntity(string auditEntity, int? entityId)
    {
        var type = auditEntity.Split([':', ' '], 2)[0];
        var label = AuditEntities.FirstOrDefault(e => e.Type == type).Label ?? type;
        return entityId is { } id ? $"{label} #{id}" : label;
    }

    /// <summary>The "work still to do" filter option, e.g. "Cần xử lý (5)": the total of the given statuses.</summary>
    public static string WorkCountLabel(IEnumerable<string> statuses, VFMS.Contracts.Operations.StatusCountsResponse? counts) =>
        counts is null ? "Cần xử lý" : $"Cần xử lý ({statuses.Sum(status => counts.Counts.GetValueOrDefault(status))})";

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
        "PROPOSAL_INCOMPLETE" => "Đề xuất cần mô tả và ít nhất một hạng mục trước khi gửi.",
        "PROPOSAL_NOTE_REQUIRED" => "Cần nhập ghi chú khi từ chối hoặc yêu cầu sửa.",
        "PROPOSAL_PRICES_REQUIRED" => "Vui lòng nhập giá mua cho tất cả hạng mục.",
        "PROPOSAL_EQUIPMENT_NOT_FOUND" => "Thiết bị liên quan không tồn tại.",
        "PROPOSAL_INVALID_STATE" => "Đề xuất không ở trạng thái cho phép thao tác này.",
        "PROPOSAL_NOT_FOUND" => "Không tìm thấy đề xuất mua sắm.",
        "SUPPLEMENT_PRODUCT_NOT_FOUND" => "Không tìm thấy sản phẩm.",
        "SUPPLEMENT_PRODUCT_NAME_EXISTS" => "Tên sản phẩm đã tồn tại.",
        "SUPPLEMENT_PRODUCT_STATUS_UNCHANGED" => "Sản phẩm đã ở trạng thái này.",
        "SUPPLEMENT_PRODUCT_UNAVAILABLE" => "Có sản phẩm trong giỏ đã ngừng bán hoặc hết hàng.",
        "SUPPLEMENT_PRODUCT_INSUFFICIENT_STOCK" => "Không đủ hàng cho một số sản phẩm trong giỏ.",
        "SUPPLEMENT_PRODUCT_NO_STOCK" => "Sản phẩm đã hết hàng trong kho. Hãy nhập thêm hàng trước khi mở bán.",
        "SUPPLEMENT_STOCK_INVALID_ACTION" => "Thao tác kho không hợp lệ.",
        "SUPPLEMENT_STOCK_INVALID_QUANTITY" => "Số lượng không hợp lệ.",
        "SUPPLEMENT_ORDER_INVALID_FULFILLMENT" => "Hình thức nhận hàng không hợp lệ.",
        "SUPPLEMENT_ORDER_DELIVERY_REQUIRED" => "Giao tận nơi cần tên người nhận và địa chỉ.",
        "SUPPLEMENT_ORDER_QUANTITY_INVALID" => "Số lượng mỗi sản phẩm từ 1 đến 999.",
        "SUPPLEMENT_ORDER_DELIVERY_INFO_REQUIRED" => "Cần nhập đơn vị vận chuyển và mã vận đơn.",
        "SUPPLEMENT_ORDER_CANCELLED" => "Đơn đã bị khách hủy, không thể thao tác.",
        "SUPPLEMENT_ORDER_WRONG_FULFILLMENT" => "Thao tác không phù hợp với hình thức nhận hàng của đơn.",
        "SUPPLEMENT_ORDER_INVALID_DATE_RANGE" => "Ngày bắt đầu không được sau ngày kết thúc.",
        "SUPPLEMENT_ORDER_INVALID_STATE" => "Đơn hàng không ở trạng thái cho phép thao tác này.",
        "SUPPLEMENT_ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng.",
        "IMAGE_EMPTY" => "Tệp ảnh trống.",
        "IMAGE_TOO_LARGE" => "Ảnh tối đa 5 MB.",
        "IMAGE_INVALID_TYPE" => "Chỉ nhận ảnh PNG, JPG hoặc WebP.",
        "IMAGE_NOT_FOUND" => "Không tìm thấy ảnh.",
        "BANNED_WORD_EXISTS" => "Từ khóa này đã có trong danh sách.",
        "BANNED_WORD_INACTIVE_EXISTS" => "Từ khóa này đã có nhưng đang tắt. Hãy bật lại thay vì thêm mới.",
        "BANNED_WORD_ALREADY_ACTIVE" => "Từ khóa đang được bật.",
        "BANNED_WORD_ALREADY_INACTIVE" => "Từ khóa đang được tắt.",
        "BANNED_WORD_NOT_FOUND" => "Không tìm thấy từ khóa.",
        "BUSINESS_REPORT_EXISTS" => "Đã có báo cáo cho kỳ này.",
        "BUSINESS_REPORT_INVALID_DATE_RANGE" or "AUDIT_INVALID_DATE_RANGE" => "Ngày bắt đầu không được sau ngày kết thúc.",
        "BUSINESS_REPORT_INVALID_PERIOD" => "Kỳ báo cáo không hợp lệ.",
        "BUSINESS_REPORT_PERIOD_IN_FUTURE" => "Kỳ báo cáo chưa kết thúc. Hãy chọn một kỳ đã qua.",
        "BUSINESS_REPORT_NOT_FOUND" => "Không tìm thấy báo cáo.",
        "AUDIT_INVALID_ACTION_TYPE" => "Loại thao tác không hợp lệ.",
        "AUDIT_LOG_NOT_FOUND" => "Không tìm thấy bản ghi nhật ký.",
        "GYM_NOT_CONFIGURED" => "Chưa có thông tin phòng tập.",
        "GYM_SETTINGS_INVALID_HOURS" => "Giờ mở cửa phải sớm hơn giờ đóng cửa.",
        "GYM_SETTINGS_INVALID_RENEWAL_WINDOW" => "Số ngày gia hạn không được âm.",
        _ => null
    };

    /// <summary>
    /// An issue's resolution note for display. Notes saved before 23/11/2026 begin with an English prefix
    /// ("Escalated: ", "Not safe after repair: ", "Disposed: "); show the Vietnamese one the service writes now.
    /// </summary>
    public static string? Note(string? note) => note switch
    {
        null => null,
        _ when note.StartsWith("Escalated: ", StringComparison.Ordinal) => "Chuyển quản lý: " + note["Escalated: ".Length..],
        _ when note.StartsWith("Not safe after repair: ", StringComparison.Ordinal) => "Chưa an toàn sau sửa chữa: " + note["Not safe after repair: ".Length..],
        _ when note.StartsWith("Disposed: ", StringComparison.Ordinal) => "Thanh lý: " + note["Disposed: ".Length..],
        _ => note
    };

    /// <summary>Vietnamese names for the properties FluentValidation names in its default messages.</summary>
    private static string FieldName(string name) => name switch
    {
        "Purchase Price" => "Giá mua",
        "Min Price" => "Giá từ",
        "Max Price" => "Giá đến",
        "Unit Price" or "Estimated Unit Price" => "Đơn giá",
        "Stock Quantity" => "Tồn kho",
        "Quantity" => "Số lượng",
        "Renewal Advance Days" => "Số ngày nhắc gia hạn",
        "Renewal Grace Days" => "Số ngày ân hạn",
        "Equipment Id" => "Thiết bị",
        "Product Id" => "Sản phẩm",
        "Page" => "Trang",
        "Page Size" => "Số dòng mỗi trang",
        _ => name
    };

    private static string Number(string value) =>
        long.TryParse(value.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.')
            : value;

    private static readonly System.Text.RegularExpressions.Regex AtLeastZero =
        new(@"^'(?<field>[^']+)' must be greater than or equal to '0'\.");
    private static readonly System.Text.RegularExpressions.Regex AboveZero =
        new(@"^'(?<field>[^']+)' must be greater than '0'\.");
    private static readonly System.Text.RegularExpressions.Regex Between =
        new(@"^'?(?<field>[^']+?)'? must be between (?<from>[\d,.]+) and (?<to>[\d,.]+)\.");

    /// <summary>Vietnamese text for the contract validators' messages, including FluentValidation's own defaults.</summary>
    public static string Validation(string message)
    {
        var text = KnownValidation(message);
        if (!ReferenceEquals(text, message)) return text;
        if (AtLeastZero.Match(message) is { Success: true } zero) return $"{FieldName(zero.Groups["field"].Value)} không được là số âm.";
        if (AboveZero.Match(message) is { Success: true } above) return $"Vui lòng chọn {FieldName(above.Groups["field"].Value).ToLowerInvariant()}.";
        if (Between.Match(message) is { Success: true } range)
            return $"{FieldName(range.Groups["field"].Value)} phải từ {Number(range.Groups["from"].Value)} đến {Number(range.Groups["to"].Value)}.";
        if (message.Contains("must be") && message.Contains("characters")) return "Nội dung vượt quá độ dài cho phép.";
        return message;
    }

    private static string KnownValidation(string message) => message switch
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
        "Enter the price paid for every item." => "Vui lòng nhập giá mua cho tất cả hạng mục.",
        "Each item can be priced only once." => "Mỗi hạng mục chỉ nhập giá một lần.",
        "Purchase price must be at least 1 VND." => "Giá mua tối thiểu 1 ₫.",
        "At least one item is required." => "Cần ít nhất một hạng mục.",
        "A proposal can contain at most 50 items." => "Một đề xuất tối đa 50 hạng mục.",
        "The Image field is required." => "Vui lòng tải ảnh sản phẩm lên (hoặc dán link ảnh).",
        "The image must be an uploaded image or an http(s) link to a png, jpg, jpeg or webp file."
            => "Hãy tải ảnh lên, hoặc dán liên kết http(s) tới tệp png, jpg, jpeg hoặc webp.",
        "The image must be an uploaded image or an HTTP or HTTPS link." => "Hãy tải ảnh lên, hoặc dán liên kết http(s) tới ảnh.",
        "Unit price must be at least 1 VND." => "Giá bán tối thiểu 1 ₫.",
        "Quantity must be between 1 and 999." => "Số lượng từ 1 đến 999.",
        "The cart must contain at least one product." => "Giỏ hàng chưa có sản phẩm.",
        "An order can contain at most 50 products." => "Một đơn tối đa 50 sản phẩm.",
        "The Recipient Name field is required for Home Delivery." => "Vui lòng nhập tên người nhận.",
        "The Delivery Address field is required for Home Delivery." => "Vui lòng nhập địa chỉ giao hàng.",
        "The start date cannot be after the end date." => "Ngày bắt đầu không được sau ngày kết thúc.",
        "The Courier Name field is required." => "Vui lòng nhập đơn vị vận chuyển.",
        "The Tracking Code field is required." => "Vui lòng nhập mã vận đơn.",
        "Status must be Operational, UnderMaintenance, Damaged or Retired." or "Status must be Active, Inactive or OutOfStock."
            => "Vui lòng chọn trạng thái hợp lệ.",
        "Status filter is invalid." => "Bộ lọc trạng thái không hợp lệ.",
        "Incident type must be Malfunction, Damage, Safety or Other." => "Vui lòng chọn loại sự cố.",
        "Action must be Add, Set or StopTracking." => "Thao tác kho không hợp lệ.",
        "Maximum price must not be less than the minimum price." => "Giá đến không được nhỏ hơn giá từ.",
        "Fulfillment type must be GymPickup or HomeDelivery." => "Vui lòng chọn hình thức nhận hàng.",
        "Fulfillment type filter is invalid." => "Bộ lọc hình thức nhận hàng không hợp lệ.",
        "The Word field is required." => "Vui lòng nhập từ khóa.",
        "Input exceeds maximum allowed length of 100 characters." => "Từ khóa tối đa 100 ký tự.",
        "Action type filter is invalid." => "Bộ lọc loại thao tác không hợp lệ.",
        "The Gym Name field is required." => "Vui lòng nhập tên phòng tập.",
        "Phone number must not exceed 20 characters." => "Số điện thoại tối đa 20 ký tự.",
        "Invalid phone number format." => "Số điện thoại không đúng định dạng.",
        "Invalid email address format." => "Email không đúng định dạng.",
        "Opening time and closing time must be set together." => "Cần nhập cả giờ mở cửa và giờ đóng cửa.",
        "Opening time must be earlier than closing time." => "Giờ mở cửa phải sớm hơn giờ đóng cửa.",
        "Membership renewal windows cannot be negative." => "Số ngày gia hạn không được âm.",
        "Period type must be Day, Week or Month." => "Vui lòng chọn kỳ báo cáo: ngày, tuần hoặc tháng.",
        "Date is invalid." => "Ngày không hợp lệ.",
        _ when message.StartsWith("Sort must be", StringComparison.Ordinal) => "Cách sắp xếp không hợp lệ.",
        _ when message.StartsWith("Stock must be between", StringComparison.Ordinal) => $"Tồn kho phải từ 0 đến {Number(SupplementStockMax)}.",
        _ when message.StartsWith("Quantity to add must be between", StringComparison.Ordinal) => $"Số lượng nhập phải từ 1 đến {Number(SupplementStockMax)}.",
        _ when message.StartsWith("Count must be between", StringComparison.Ordinal) => "Số kỳ so sánh không hợp lệ.",
        _ => message
    };

    private static readonly string SupplementStockMax =
        VFMS.Contracts.Supplements.SupplementStock.MaxQuantity.ToString(CultureInfo.InvariantCulture);
}
