namespace VFMS.Client.Components.Workspace;

public partial class WorkspaceNavigation
{
    private sealed record Link(string Label, string Href, string? Role = null, bool Alias = false);
    private sealed record Group(string Title, params Link[] Links);

    private static readonly Group[] ManagerGroups =
    [
        new("Tổng quan & báo cáo",
            new("Tổng quan", "/manager/dashboard"),
            new("Báo cáo kinh doanh", "/manager/business-reports", "Manager")),
        new("Vận hành phòng tập",
            new("Lễ tân check-in", "/staff/check-ins"),
            new("Kiosk check-in", "/kiosk"),
            new("Quản lý Yoga", "/manager/yoga"),
            new("Quản lý cuộc thi", "/manager/competitions")),
        new("Gói hội viên & chính sách",
            new("Gói hội viên", "/manager/membership-plans", "Manager"),
            new("Chính sách bảo lưu", "/manager/membership-freeze-policy", "Manager"),
            new("Duyệt bảo lưu", "/manager/membership-freeze-requests", "Manager"),
            new("Chính sách chung", "/manager/general-policies", "Manager")),
        new("Huấn luyện viên & PT",
            new("Hồ sơ huấn luyện viên", "/manager/trainer-applications", "Manager"),
            new("Quản lý gói PT", "/manager/pt-packages", "Manager"),
            new("Duyệt bảo lưu PT", "/manager/pt-freeze-requests", "Manager"),
            new("Tranh chấp điểm danh", "/manager/pt-attendance-disputes", "Manager")),
        new("Thiết bị & mua sắm",
            new("Thiết bị", "/manager/equipment", "Manager"),
            new("Bài tập", "/manager/exercises", "Manager"),
            new("Sửa chữa thiết bị", "/manager/equipment-issues", "Manager"),
            new("Đề xuất mua sắm", "/manager/purchase-proposals", "Manager")),
        new("Tủ đồ",
            new("Khu và tủ", "/manager/lockers", "Manager"),
            new("Bảng giá thuê tủ", "/manager/locker-pricing", "Manager"),
            new("Quá hạn & bảo trì", "/manager/locker-operations", "Manager"),
            new("Xác nhận dọn tủ", "/manager/locker-clearances", "Manager")),
        new("Bán hàng & Marketing",
            new("Sản phẩm bổ sung", "/manager/supplement-products", "Manager"),
            new("Đơn bổ sung", "/manager/supplement-orders", "Manager"),
            new("Khuyến mãi", "/manager/promotions", "Manager")),
        new("Khách hàng",
            new("Tạo tài khoản khách hàng", "/manager/customers/create", "Manager"),
            new("Phản hồi khách hàng", "/manager/feedback", "Manager"),
            new("Tư vấn trực tuyến", "/manager/consultations", "Manager"),
            new("Khuyến mãi", "/manager/promotions", "Manager", Alias: true))
    ];

    private static readonly Group[] AdminGroups =
    [
        new("Tổng quan",
            new("Tổng quan", "/admin/dashboard"),
            new("Truy cập nhanh", "/admin/dashboard#quick-actions")),
        new("Hoạt động & báo cáo",
            new Link("Báo cáo kinh doanh", "/admin/business-reports", "Admin")),
        new("Quản trị hệ thống",
            new("Từ khóa cấm", "/admin/banned-words", "Admin"),
            new("Nhật ký hệ thống", "/admin/audit-logs", "Admin"))
    ];

    private static readonly Group[] StaffGroups =
    [
        new("Tổng quan",
            new("Tổng quan", "/staff/dashboard"),
            new("Truy cập nhanh", "/staff/dashboard#quick-actions")),
        new("Lễ tân & điểm danh",
            new("Lễ tân check-in", "/staff/check-ins"),
            new("Kiosk check-in", "/kiosk"),
            new("Điểm danh Yoga", "/staff/yoga")),
        new("Khách hàng",
            new("Tạo tài khoản khách hàng", "/staff/customers/create", "Staff"),
            new("Tư vấn trực tuyến", "/staff/consultations", "Staff"),
            new("Phản hồi khách hàng", "/staff/feedback", "Staff")),
        new("Đơn hàng",
            new("Đơn gói tập", "/staff/membership-cash-receipt", "Staff"),
            new("Đơn bổ sung", "/staff/supplement-orders", "Staff")),
        new("Tủ đồ",
            new("Sơ đồ tủ", "/staff/lockers", "Staff"),
            new("Hợp đồng tủ", "/staff/locker-contracts", "Staff")),
        new("Thiết bị & mua sắm",
            new("Sự cố thiết bị", "/staff/equipment-issues", "Staff"),
            new("Đề xuất mua sắm", "/staff/purchase-proposals", "Staff")),
        new("Hoạt động", new Link("Cuộc thi", "/staff/competitions"))
    ];

    private static readonly Group[] TrainerGroups =
    [
        new("Lịch huấn luyện",
            new("Lịch tuần PT", "/pt/availability", "PT"),
            new("Lịch dạy PT", "/pt/teaching-schedule", "PT"),
            new("Lớp Yoga của tôi", "/pt/yoga", "YogaInstructor"),
            new("Yêu cầu mở lớp Yoga", "/pt/yoga/class-requests", "YogaInstructor")),
        new("Hội viên", new Link("Hội viên được phân công", "/pt/assigned-members", "PT")),
        new("Hồ sơ", new Link("Hồ sơ huấn luyện viên", "/pt/trainer-profile", "PT"))
    ];

    private static readonly Group[] YogaInstructorGroups =
    [
        new("Điều hướng",
            new("Hồ sơ cá nhân", "/profile"),
            new("Cuộc thi", "/competitions"),
            new("Trang chủ", "/")),
        new("Huấn luyện viên",
            new("Hồ sơ huấn luyện viên", "/pt/trainer-profile", "YogaInstructor"),
            new("Lớp Yoga của tôi", "/pt/yoga", "YogaInstructor"),
            new("Yêu cầu mở lớp Yoga", "/pt/yoga/class-requests", "YogaInstructor"))
    ];
}
