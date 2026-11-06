namespace VFMS.Domain.Enums;

// ─────────────────────────────────────────────────────────────
//  Identity & Role
// ─────────────────────────────────────────────────────────────

public enum UserRole
{
    Guest,
    Customer,
    Member,
    PT,
    Staff,
    Manager,
    Admin
}

public enum AccountStatus
{
    Active,
    Locked,
    Inactive,
    PendingVerification
}

// ─────────────────────────────────────────────────────────────
//  Package & Membership
// ─────────────────────────────────────────────────────────────

public enum PackageStatus
{
    Active,
    Inactive
}

public enum ComponentType
{
    GYM,
    YOGA,
    PT
}

public enum MembershipStatus
{
    Scheduled,
    Active,
    Frozen,
    Expired,
    Cancelled
}

public enum FreezeRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Active,
    Completed,
    Expired
}

// ─────────────────────────────────────────────────────────────
//  Order & Payment
// ─────────────────────────────────────────────────────────────

public enum OrderStatus
{
    Pending,
    Paid,
    Cancelled,
    Expired
}

public enum OrderChannel
{
    Online,
    Counter
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded,
    Cancelled
}

public enum PaymentMethod
{
    VietQR,
    Cash,
    BankTransfer
}

public enum InvoiceStatus
{
    Issued,
    Voided
}

// ─────────────────────────────────────────────────────────────
//  Voucher
// ─────────────────────────────────────────────────────────────

public enum DiscountType
{
    PERCENT,
    FIXED
}

public enum VoucherStatus
{
    Active,
    Inactive,
    Expired
}

public enum VoucherRedemptionStatus
{
    Reserved,
    Confirmed,
    Released
}

// ─────────────────────────────────────────────────────────────
//  Trainer
// ─────────────────────────────────────────────────────────────

public enum TrainerApplicationStatus
{
    Pending,
    UnderReview,
    Approved,
    Rejected,
    Blacklisted,
    Interviewing,
    Hired
}

public enum TrainerLevel
{
    Junior,
    Senior,
    Expert,
    Master
}

// ─────────────────────────────────────────────────────────────
//  PT Session & Freeze
// ─────────────────────────────────────────────────────────────

public enum PTSessionStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoShow
}

public enum SlotStatus
{
    Available,
    Booked,
    Blocked
}

public enum PTFreezeRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Active,
    Completed,
    Expired
}

// ─────────────────────────────────────────────────────────────
//  Yoga
// ─────────────────────────────────────────────────────────────

public enum YogaClassStatus
{
    Draft,
    Published,
    Cancelled,
    Completed
}

public enum YogaScheduleStatus
{
    Scheduled,
    Ongoing,
    Completed,
    Cancelled,
    Rescheduled
}

public enum YogaBookingStatus
{
    Confirmed,
    Cancelled,
    NoShow,
    Attended
}

public enum YogaWaitlistStatus
{
    Waiting,
    Promoted,
    Expired
}

public enum YogaAttendanceStatus
{
    Present,
    Absent,
    NoShow
}

// ─────────────────────────────────────────────────────────────
//  Locker
// ─────────────────────────────────────────────────────────────

public enum LockerStatus
{
    Available,
    Rented,
    Maintenance,
    Damaged,
    Reserved
}

public enum LockerType
{
    Standard,
    Large,
    Small
}

public enum GenderRestriction
{
    None,
    Male,
    Female
}

public enum RentalType
{
    Monthly,
    Quarterly,
    Yearly
}

public enum LockerContractStatus
{
    Active,
    Expired,
    Terminated,
    Overdue
}

public enum LockerIncidentType
{
    Damage,
    Loss,
    Theft,
    Other
}

public enum KeyOrPinType
{
    Key,
    Pin
}

// ─────────────────────────────────────────────────────────────
//  Check-in
// ─────────────────────────────────────────────────────────────

public enum CheckInMethod
{
    FaceRecognition,
    QRCode,
    ManualStaff
}

public enum CheckInStatus
{
    Active,
    CheckedOut,
    AutoClosed
}

// ─────────────────────────────────────────────────────────────
//  Equipment
// ─────────────────────────────────────────────────────────────

public enum EquipmentStatus
{
    Operational,
    UnderMaintenance,
    Damaged,
    Retired
}

public enum EquipmentIssueStatus
{
    Reported,
    InProgress,
    Resolved,
    Escalated,
    MarkedSafe
}

public enum IncidentType
{
    Malfunction,
    Damage,
    Safety,
    Other
}

// ─────────────────────────────────────────────────────────────
//  Purchase Proposal
// ─────────────────────────────────────────────────────────────

public enum PurchaseProposalStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Completed
}

public enum PurchaseProposalReviewAction
{
    Approved,
    Rejected,
    RequestRevision
}

// ─────────────────────────────────────────────────────────────
//  Supplement
// ─────────────────────────────────────────────────────────────

public enum SupplementProductStatus
{
    Active,
    Inactive,
    OutOfStock
}

public enum SupplementOrderStatus
{
    Pending,
    Processing,
    ReadyForPickup,
    OutForDelivery,
    Completed,
    Cancelled
}

public enum SupplementDeliveryType
{
    GymPickup,
    HomeDelivery
}

// ─────────────────────────────────────────────────────────────
//  Competition
// ─────────────────────────────────────────────────────────────

public enum CompetitionStatus
{
    Draft,
    Published,
    Ongoing,
    Completed,
    Cancelled
}

public enum CompetitionJoinStatus
{
    Registered,
    Confirmed,
    Disqualified,
    Completed
}

// ─────────────────────────────────────────────────────────────
//  Trainer Review
// ─────────────────────────────────────────────────────────────

public enum TrainerReviewStatus
{
    Pending,
    Approved,
    Rejected
}

// ─────────────────────────────────────────────────────────────
//  Workout Plan
// ─────────────────────────────────────────────────────────────

public enum WorkoutPlanStatus
{
    Active,
    Archived
}

public enum FitnessGoal
{
    WeightLoss,
    MuscleGain,
    Endurance,
    Flexibility,
    GeneralFitness
}

public enum FitnessLevel
{
    Beginner,
    Intermediate,
    Advanced
}

// ─────────────────────────────────────────────────────────────
//  Notification
// ─────────────────────────────────────────────────────────────

public enum NotificationType
{
    MembershipExpiry,
    FreezeApproval,
    PTSessionReminder,
    YogaBookingConfirmed,
    PaymentConfirmed,
    EquipmentIssueUpdate,
    CompetitionResult,
    LockerOverdue,
    WorkoutPlanGenerated,
    SystemAlert,
    GymAnnouncement,
    VoucherExpiry
}

// ─────────────────────────────────────────────────────────────
//  Audit Log
// ─────────────────────────────────────────────────────────────

public enum AuditActionType
{
    Create,
    Update,
    Delete,
    Login,
    Logout,
    RoleChange,
    AccountLock,
    AccountUnlock,
    PasswordChange
}

// ─────────────────────────────────────────────────────────────
//  Business Report
// ─────────────────────────────────────────────────────────────

public enum ReportMetricType
{
    TotalRevenue,
    NewMembers,
    ActiveMemberships,
    PTSessionsCompleted,
    YogaClassAttendance,
    LockerOccupancy,
    SupplementSales,
    EquipmentIssues
}

public enum MetricValueType
{
    Currency,
    Count,
    Percentage
}

// ─────────────────────────────────────────────────────────────
//  Policy
// ─────────────────────────────────────────────────────────────

public enum PolicyStatus
{
    Active,
    Inactive
}

