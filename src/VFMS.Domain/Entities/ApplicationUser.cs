using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Application user account. Covers all roles: Customer, Member, PT, Staff, Manager, Admin.
/// Accounts must never be hard-deleted (GBR-33). Use <see cref="AccountStatus"/> transitions instead.
/// </summary>
public class ApplicationUser : BaseEntity
{
    public string UserId { get; set; } = string.Empty;   // NVARCHAR – natural key / sub from JWT
    public int GymId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.PendingVerification;
    public DateTime? LastLoginAt { get; set; }
    public bool IsBanned { get; set; } = false;

    // ── Navigation Properties ──────────────────────────────────
    public Gym Gym { get; set; } = null!;
    public ICollection<ApplicationUserRole> UserRoles { get; set; } = [];
    public ICollection<Order> PurchasedOrders { get; set; } = [];
    public ICollection<OrderItem> BeneficiaryOrderItems { get; set; } = [];   // via OrderItem.BeneficiaryUserId
    public ICollection<MemberMembership> Memberships { get; set; } = [];
    public ICollection<MembershipFreeze> MembershipFreezeRequests { get; set; } = [];
    public ICollection<PTSession> PTSessionsAsTrainee { get; set; } = [];
    public ICollection<PTSession> PTSessionsAsTrainer { get; set; } = [];
    public ICollection<PTPurchase> PTPurchasesAsMember { get; set; } = [];
    public ICollection<PTPurchase> PTPurchasesAsPT { get; set; } = [];
    public ICollection<PTAvailability> PTAvailabilities { get; set; } = [];
    public ICollection<PTFreeze> PTFreezeRequestsAsMember { get; set; } = [];
    public ICollection<PTFreeze> PTFreezeRequestsAsPT { get; set; } = [];
    public ICollection<YogaBooking> YogaBookings { get; set; } = [];
    public ICollection<YogaWaitlist> YogaWaitlists { get; set; } = [];
    public ICollection<YogaAttendance> YogaAttendances { get; set; } = [];
    public ICollection<CheckIn> CheckIns { get; set; } = [];
    public ICollection<QRCode> QRCodes { get; set; } = [];
    public FaceData? FaceData { get; set; }
    public TrainerApplication? TrainerApplication { get; set; }
    public TrainerProfile? TrainerProfile { get; set; }
    public ICollection<TrainerReview> TrainerReviewsGiven { get; set; } = [];
    public ICollection<LockerRentalContract> LockerContracts { get; set; } = [];
    public ICollection<WorkoutPlan> WorkoutPlans { get; set; } = [];
    public PhysicalProfile? PhysicalProfile { get; set; }
    public ICollection<EquipmentIssueReport> EquipmentIssueReports { get; set; } = [];
    public ICollection<CompetitionJoin> CompetitionRegistrations { get; set; } = [];
    public ICollection<SupplementOrder> SupplementOrders { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<Voucher> CreatedVouchers { get; set; } = [];
    public ICollection<PurchaseProposal> PurchaseProposals { get; set; } = [];
}

