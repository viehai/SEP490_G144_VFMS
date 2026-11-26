namespace VFMS.Contracts.Dashboards;

public sealed record ChartPoint(DateOnly Date, decimal Value);
public sealed record CategoryCount(string Category, int Count);
public sealed record AuditActivity(DateTime OccurredAt, string? Actor, string Action, string Entity, int? EntityId);
public sealed record DashboardOrder(string Number, string Customer, string Product, string Status, decimal? Amount,
    string? Method, DateTime CreatedAt);
public sealed record GymVisit(string Member, DateTime ArrivedAt, bool CheckedOut);
public sealed record TrainingSlot(TimeOnly Start, TimeOnly End, string Type, string Name, string Place, string Status);
public sealed record MemberProgress(int ContractId, string Member, string Package, int Total, int Remaining);
public sealed record DashboardNotice(string Title, string Message, DateTime SentAt, bool Read, int? Id = null);
public sealed record DashboardInbox(int Unread, IReadOnlyList<DashboardNotice> Recent);

public sealed record AdminOverview(DateOnly Today, int Accounts, int Active, int NewAccounts, int Locked,
    int Applications, int Proposals, IReadOnlyList<CategoryCount> Roles, IReadOnlyList<ChartPoint> Activity,
    IReadOnlyList<AuditActivity> Recent);
public sealed record ManagerOverview(DateOnly Today, decimal RevenueToday, decimal RevenueYesterday, int ActiveMembers,
    int PendingOrders, int PTSessions, IReadOnlyList<ChartPoint> Revenue, IReadOnlyList<CategoryCount> Memberships,
    IReadOnlyList<DashboardOrder> Recent, IReadOnlyList<DashboardOrder> Counter);
public sealed record StaffOverview(DateOnly Today, int Visits, int YesterdayVisits, int PendingOrders, int NewMembers,
    int OpenConsultations, IReadOnlyList<GymVisit> Arrivals, IReadOnlyList<DashboardOrder> Orders,
    IReadOnlyList<TrainingSlot> Schedule);
public sealed record TrainerOverview(DateOnly Today, int Members, int PTCount, int YogaCount, int Upcoming,
    IReadOnlyList<TrainingSlot> Schedule, IReadOnlyList<MemberProgress> Progress);
