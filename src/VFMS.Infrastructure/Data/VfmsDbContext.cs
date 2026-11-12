using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;

namespace VFMS.Infrastructure.Data;

/// <summary>
/// EF Core 8 database context for the VFMS system.
/// All timestamps are stored as DATETIME2 in UTC (converted to UTC+7 at the presentation boundary).
/// Global soft-delete query filter is applied to all <see cref="VFMS.Domain.Common.BaseEntity"/>-derived entities.
/// </summary>
public class VfmsDbContext : DbContext
{
    public VfmsDbContext(DbContextOptions<VfmsDbContext> options) : base(options) { }

    // ─────────────────────────────────────────────────────────────
    //  Identity & Role
    // ─────────────────────────────────────────────────────────────
    public DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public DbSet<ApplicationUserRole> ApplicationUserRoles { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Gym
    // ─────────────────────────────────────────────────────────────
    public DbSet<Gym> Gyms { get; set; }
    public DbSet<GymAnnouncement> GymAnnouncements { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Package, Order & Payment (Module 2)
    // ─────────────────────────────────────────────────────────────
    public DbSet<Package> Packages { get; set; }
    public DbSet<PackageComponent> PackageComponents { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<MemberMembership> MemberMemberships { get; set; }
    public DbSet<Voucher> Vouchers { get; set; }
    public DbSet<VoucherRedemption> VoucherRedemptions { get; set; }
    public DbSet<FreezePolicy> FreezePolicies { get; set; }
    public DbSet<MembershipFreeze> MembershipFreezes { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Trainer (Module 3)
    // ─────────────────────────────────────────────────────────────
    public DbSet<TrainerApplication> TrainerApplications { get; set; }
    public DbSet<TrainerProfile> TrainerProfiles { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Personal Training / PT (Module 4)
    // ─────────────────────────────────────────────────────────────
    public DbSet<PTAvailability> PTAvailabilities { get; set; }
    public DbSet<PTPurchase> PTPurchases { get; set; }
    public DbSet<PTSession> PTSessions { get; set; }
    public DbSet<PTFreeze> PTFreezes { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Locker (Module 5)
    // ─────────────────────────────────────────────────────────────
    public DbSet<LockerZone> LockerZones { get; set; }
    public DbSet<Locker> Lockers { get; set; }
    public DbSet<LockerPricingPolicy> LockerPricingPolicies { get; set; }
    public DbSet<LockerRentalContract> LockerRentalContracts { get; set; }
    public DbSet<LockerIncidentLog> LockerIncidentLogs { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Yoga & Group Classes (Module 7)
    // ─────────────────────────────────────────────────────────────
    public DbSet<YogaStudio> YogaStudios { get; set; }
    public DbSet<YogaClassType> YogaClassTypes { get; set; }
    public DbSet<YogaClass> YogaClasses { get; set; }
    public DbSet<YogaClassSchedule> YogaClassSchedules { get; set; }
    public DbSet<YogaBooking> YogaBookings { get; set; }
    public DbSet<YogaWaitlist> YogaWaitlists { get; set; }
    public DbSet<YogaAttendance> YogaAttendances { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Check-in / Biometrics (Module 8)
    // ─────────────────────────────────────────────────────────────
    public DbSet<CheckIn> CheckIns { get; set; }
    public DbSet<FaceData> FaceData { get; set; }
    public DbSet<QRCode> QRCodes { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  AI Workout Plan (Module 9)
    // ─────────────────────────────────────────────────────────────
    public DbSet<PhysicalProfile> PhysicalProfiles { get; set; }
    public DbSet<WorkoutPlan> WorkoutPlans { get; set; }
    public DbSet<Exercise> Exercises { get; set; }
    public DbSet<PlanItem> PlanItems { get; set; }
    public DbSet<WorkoutLog> WorkoutLogs { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Equipment & Procurement (Module 10)
    // ─────────────────────────────────────────────────────────────
    public DbSet<CatalogCategory> CatalogCategories { get; set; }
    public DbSet<Equipment> Equipments { get; set; }
    public DbSet<EquipmentIssueReport> EquipmentIssueReports { get; set; }
    public DbSet<EquipmentSafetyConfirmation> EquipmentSafetyConfirmations { get; set; }
    public DbSet<PurchaseProposal> PurchaseProposals { get; set; }
    public DbSet<PurchaseProposalItem> PurchaseProposalItems { get; set; }
    public DbSet<PurchaseProposalReview> PurchaseProposalReviews { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Supplement Store (Module 11)
    // ─────────────────────────────────────────────────────────────
    public DbSet<SupplementProduct> SupplementProducts { get; set; }
    public DbSet<SupplementOrder> SupplementOrders { get; set; }
    public DbSet<SupplementOrderItem> SupplementOrderItems { get; set; }
    public DbSet<SupplementOrderDelivery> SupplementOrderDeliveries { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Competitions (Module 6)
    // ─────────────────────────────────────────────────────────────
    public DbSet<Competition> Competitions { get; set; }
    public DbSet<CompetitionPrize> CompetitionPrizes { get; set; }
    public DbSet<CompetitionJoin> CompetitionJoins { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Reviews & Trainer Feedback
    // ─────────────────────────────────────────────────────────────
    public DbSet<TrainerReview> TrainerReviews { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Communication & System (Modules 12, 13)
    // ─────────────────────────────────────────────────────────────
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<BannedWordList> BannedWordLists { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<BusinessReportSnapshot> BusinessReportSnapshots { get; set; }
    public DbSet<ReportMetric> ReportMetrics { get; set; }

    // ─────────────────────────────────────────────────────────────
    //  Model Configuration
    // ─────────────────────────────────────────────────────────────
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Global soft-delete query filter (GBR-33) ──────────────
        // Automatically filters out soft-deleted records for ALL entities.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(VFMS.Domain.Common.BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(VFMS.Domain.Common.BaseEntity.IsDeleted));
                var condition = System.Linq.Expressions.Expression.Equal(property,
                    System.Linq.Expressions.Expression.Constant(false));
                var lambda = System.Linq.Expressions.Expression.Lambda(condition, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        // ── ApplicationUser ───────────────────────────────────────
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.UserId).HasMaxLength(128);
            entity.Property(e => e.FullName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ResetTokenHash).HasMaxLength(64);
            entity.HasIndex(e => e.ResetTokenHash).IsUnique();
            entity.Property(e => e.AccountStatus).HasConversion<string>().HasMaxLength(50);

            entity.HasOne(e => e.Gym)
                .WithMany(g => g.Users)
                .HasForeignKey(e => e.GymId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ApplicationRole ───────────────────────────────────────
        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RoleName).IsUnique();
            entity.Property(e => e.RoleName).HasMaxLength(100).IsRequired();
        });

        // ── ApplicationUserRole ───────────────────────────────────
        modelBuilder.Entity<ApplicationUserRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.RoleId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── RefreshToken ──────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.Property(e => e.Token).HasMaxLength(512).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Package ───────────────────────────────────────────────
        modelBuilder.Entity<Package>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PackageStatus).HasConversion<string>().HasMaxLength(50);
        });

        // ── PackageComponent ──────────────────────────────────────
        modelBuilder.Entity<PackageComponent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ComponentType).HasConversion<string>().HasMaxLength(10);

            entity.HasOne(e => e.Package)
                .WithMany(p => p.Components)
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.YogaClassType)
                .WithMany(t => t.PackageComponents)
                .HasForeignKey(e => e.YogaClassTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Order ─────────────────────────────────────────────────
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.OrderStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Purchaser)
                .WithMany(u => u.PurchasedOrders)
                .HasForeignKey(e => e.PurchaserUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── OrderItem ─────────────────────────────────────────────
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPriceSnapshot).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PackageNameSnapshot).HasMaxLength(256).IsRequired();

            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Package)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Beneficiary)
                .WithMany(u => u.BeneficiaryOrderItems)
                .HasForeignKey(e => e.BeneficiaryUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RenewedFromMembership)
                .WithMany()
                .HasForeignKey(e => e.RenewedFromMembershipId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PaymentTransaction ────────────────────────────────────
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GatewayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.GatewayTransactionReference).HasMaxLength(100);
            entity.HasIndex(e => e.GatewayTransactionReference).IsUnique()
                .HasFilter("[GatewayName] = 'SePay' AND [GatewayTransactionReference] IS NOT NULL");
            entity.Property(e => e.AmountDue).HasColumnType("decimal(18,2)");
            entity.Property(e => e.AmountReceived).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PaymentStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.PaymentMethod).HasConversion<string>().HasMaxLength(50);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Order)
                .WithOne(o => o.PaymentTransaction)
                .HasForeignKey<PaymentTransaction>(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Invoice ───────────────────────────────────────────────
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InvoiceNumber).IsUnique();
            entity.Property(e => e.InvoiceNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.InvoiceStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.PaymentTransaction)
                .WithOne(pt => pt.Invoice)
                .HasForeignKey<Invoice>(e => e.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MemberMembership ──────────────────────────────────────
        modelBuilder.Entity<MemberMembership>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MembershipStatus).HasConversion<string>().HasMaxLength(50);

            entity.HasOne(e => e.OrderItem)
                .WithMany(oi => oi.MemberMemberships)
                .HasForeignKey(e => e.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PackageComponent)
                .WithMany(pc => pc.MemberMemberships)
                .HasForeignKey(e => e.PackageComponentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.Memberships)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Voucher ───────────────────────────────────────────────
        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DiscountType).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.DiscountValue).HasColumnType("decimal(18,2)");
            entity.Property(e => e.MinimumSpend).HasColumnType("decimal(18,2)");
            entity.Property(e => e.MaxDiscountAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.VoucherStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany(u => u.CreatedVouchers)
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── VoucherRedemption ─────────────────────────────────────
        modelBuilder.Entity<VoucherRedemption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderId).IsUnique();  // One voucher per order
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RedemptionStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Voucher)
                .WithMany(v => v.Redemptions)
                .HasForeignKey(e => e.VoucherId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Order)
                .WithOne(o => o.VoucherRedemption)
                .HasForeignKey<VoucherRedemption>(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── FreezePolicy ──────────────────────────────────────────
        modelBuilder.Entity<FreezePolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PolicyStatus).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.IsGlobal).IsUnique()
                .HasFilter("[IsGlobal] = 1 AND [IsDeleted] = 0 AND [PolicyStatus] = 'Active'");
            entity.OwnsMany(e => e.TierQuotas, quotas =>
            {
                quotas.ToTable("FreezePolicyTierQuotas");
                quotas.WithOwner().HasForeignKey("FreezePolicyId");
                quotas.HasKey("FreezePolicyId", nameof(FreezePolicyTierQuota.Unit), nameof(FreezePolicyTierQuota.Duration));
                quotas.Property(quota => quota.Unit).HasConversion<string>().HasMaxLength(10);
            });

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MembershipFreeze ──────────────────────────────────────
        modelBuilder.Entity<MembershipFreeze>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Reason).HasMaxLength(4000);
            entity.Property(e => e.EarlyUnfreezeRequestStatus).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.FreezePolicy).WithMany().HasForeignKey(e => e.FreezePolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MemberMembership)
                .WithMany(m => m.MembershipFreezes)
                .HasForeignKey(e => e.MemberMembershipId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RequestedByUser)
                .WithMany(u => u.MembershipFreezeRequests)
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TrainerApplication ────────────────────────────────────
        modelBuilder.Entity<TrainerApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ApplicationStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.FullName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CitizenId).HasMaxLength(12).IsRequired();

            entity.HasOne(e => e.Applicant)
                .WithOne(u => u.TrainerApplication)
                .HasForeignKey<TrainerApplication>(e => e.ApplicantUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TrainerProfile ────────────────────────────────────────
        modelBuilder.Entity<TrainerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.PTUser)
                .WithOne(u => u.TrainerProfile)
                .HasForeignKey<TrainerProfile>(e => e.PTUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PTAvailability ────────────────────────────────────────
        modelBuilder.Entity<PTAvailability>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SlotStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.PTUser)
                .WithMany(u => u.PTAvailabilities)
                .HasForeignKey(e => e.PTUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PTPurchase ────────────────────────────────────────────
        modelBuilder.Entity<PTPurchase>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PurchasePrice).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.MemberMembership)
                .WithMany()
                .HasForeignKey(e => e.MemberMembershipId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.PTPurchasesAsMember)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PTUser)
                .WithMany(u => u.PTPurchasesAsPT)
                .HasForeignKey(e => e.PTUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PTSession ─────────────────────────────────────────────
        modelBuilder.Entity<PTSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.PTPurchase)
                .WithMany(p => p.Sessions)
                .HasForeignKey(e => e.PTPurchaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PTAvailability)
                .WithMany(a => a.PTSessions)
                .HasForeignKey(e => e.PTAvailabilityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.PTSessionsAsTrainee)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PTUser)
                .WithMany(u => u.PTSessionsAsTrainer)
                .HasForeignKey(e => e.PTUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PTFreeze ──────────────────────────────────────────────
        modelBuilder.Entity<PTFreeze>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestStatus).HasConversion<string>().HasMaxLength(50);

            entity.HasOne(e => e.PTPurchase)
                .WithMany(p => p.PTFreezes)
                .HasForeignKey(e => e.PTPurchaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RequestedByUser)
                .WithMany(u => u.PTFreezeRequestsAsMember)
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany(u => u.PTFreezeRequestsAsPT)
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaStudio ────────────────────────────────────────────
        modelBuilder.Entity<YogaStudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GymId, e.StudioName }).IsUnique();   // GBR-71
            entity.Property(e => e.StudioName).HasMaxLength(200).IsRequired();

            entity.HasOne(e => e.Gym)
                .WithMany(g => g.YogaStudios)
                .HasForeignKey(e => e.GymId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaClassType ─────────────────────────────────────────
        modelBuilder.Entity<YogaClassType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TypeName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // ── YogaClass ─────────────────────────────────────────────
        modelBuilder.Entity<YogaClass>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClassName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ClassStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DifficultyLevel).HasMaxLength(20).IsRequired();

            entity.HasOne(e => e.YogaClassType)
                .WithMany(t => t.YogaClasses)
                .HasForeignKey(e => e.YogaClassTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Instructor)
                .WithMany()
                .HasForeignKey(e => e.InstructorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaClassSchedule ─────────────────────────────────────
        modelBuilder.Entity<YogaClassSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScheduleStatus).HasConversion<string>().HasMaxLength(50);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.InstructorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.YogaClass)
                .WithMany(yc => yc.Schedules)
                .HasForeignKey(e => e.YogaClassId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.YogaStudio)
                .WithMany(s => s.ClassSchedules)
                .HasForeignKey(e => e.YogaStudioId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RescheduledFrom)
                .WithMany(e => e.RescheduledSessions)
                .HasForeignKey(e => e.RescheduledFromId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaBooking ───────────────────────────────────────────
        modelBuilder.Entity<YogaBooking>(entity =>
        {
            entity.HasKey(e => e.Id);
            // GBR-79: one active enrolment per member per class; cancelled ones do not block rebooking.
            entity.HasIndex(e => new { e.YogaClassId, e.MemberUserId }).IsUnique()
                .HasFilter("[BookingStatus] = 'Confirmed'");
            entity.Property(e => e.BookingStatus).HasConversion<string>().HasMaxLength(20);
            // One confirmed booking per mat per session (MSG-33).
            entity.HasIndex(e => new { e.YogaClassId, e.MatNumber }).IsUnique()
                .HasFilter("[MatNumber] IS NOT NULL AND [BookingStatus] = 'Confirmed'");

            entity.HasOne(e => e.YogaClass)
                .WithMany(c => c.Bookings)
                .HasForeignKey(e => e.YogaClassId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.OrderItem)
                .WithMany()
                .HasForeignKey(e => e.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.YogaBookings)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaWaitlist ──────────────────────────────────────────
        modelBuilder.Entity<YogaWaitlist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.YogaClassScheduleId, e.MemberUserId }).IsUnique();
            entity.Property(e => e.WaitlistStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.YogaClassSchedule)
                .WithMany(s => s.Waitlists)
                .HasForeignKey(e => e.YogaClassScheduleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.YogaClassType)
                .WithMany(t => t.YogaWaitlists)
                .HasForeignKey(e => e.YogaClassTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.YogaWaitlists)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── YogaAttendance ────────────────────────────────────────
        modelBuilder.Entity<YogaAttendance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AttendanceStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.YogaClassSchedule)
                .WithMany(s => s.Attendances)
                .HasForeignKey(e => e.YogaClassScheduleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.YogaBooking)
                .WithMany(b => b.Attendances)
                .HasForeignKey(e => e.YogaBookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.YogaAttendances)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Instructor)
                .WithMany()
                .HasForeignKey(e => e.InstructorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── LockerZone ────────────────────────────────────────────
        modelBuilder.Entity<LockerZone>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ZoneName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.GenderRestriction).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Gym)
                .WithMany(g => g.LockerZones)
                .HasForeignKey(e => e.GymId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Locker ────────────────────────────────────────────────
        modelBuilder.Entity<Locker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LockerNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.LockerType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.LockerStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.KeyOrPinType).HasConversion<string>().HasMaxLength(10);

            entity.HasOne(e => e.LockerZone)
                .WithMany(z => z.Lockers)
                .HasForeignKey(e => e.LockerZoneId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.LockerPricingPolicy)
                .WithMany(p => p.Lockers)
                .HasForeignKey(e => e.LockerPricingPolicyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── LockerPricingPolicy ───────────────────────────────────
        modelBuilder.Entity<LockerPricingPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RentalType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RentalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.DepositAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PolicyStatus).HasConversion<string>().HasMaxLength(20);
        });

        // ── LockerRentalContract ──────────────────────────────────
        modelBuilder.Entity<LockerRentalContract>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ContractNumber).IsUnique();
            entity.Property(e => e.ContractNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DepositAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RentalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.FineAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ContractStatus).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Locker)
                .WithMany(l => l.RentalContracts)
                .HasForeignKey(e => e.LockerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.LockerPricingPolicy)
                .WithMany(p => p.Contracts)
                .HasForeignKey(e => e.LockerPricingPolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Customer)
                .WithMany(u => u.LockerContracts)
                .HasForeignKey(e => e.CustomerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── LockerIncidentLog ─────────────────────────────────────
        modelBuilder.Entity<LockerIncidentLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IncidentType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.FineAmount).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Locker)
                .WithMany(l => l.IncidentLogs)
                .HasForeignKey(e => e.LockerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany()
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.HandledByUser)
                .WithMany()
                .HasForeignKey(e => e.HandledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── CheckIn ───────────────────────────────────────────────
        modelBuilder.Entity<CheckIn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CheckinMethod).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.CheckoutMethod).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            // One open visit per member: stops two kiosk scans at once from opening two visits.
            entity.HasIndex(e => e.MemberUserId).IsUnique().HasFilter("[Status] = 'Active'");

            entity.HasOne(e => e.Member)
                .WithMany(u => u.CheckIns)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MemberMembership)
                .WithMany()
                .HasForeignKey(e => e.MemberMembershipId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.HandledByUser)
                .WithMany()
                .HasForeignKey(e => e.HandledByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CheckedOutByUser)
                .WithMany()
                .HasForeignKey(e => e.CheckedOutByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── FaceData ──────────────────────────────────────────────
        modelBuilder.Entity<FaceData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FaceNetEmbedding).HasColumnType("VARBINARY(MAX)").IsRequired();

            entity.HasOne(e => e.User)
                .WithOne(u => u.FaceData)
                .HasForeignKey<FaceData>(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── QRCode ────────────────────────────────────────────────
        modelBuilder.Entity<QRCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.QRToken).IsUnique();
            entity.Property(e => e.QRToken).HasMaxLength(512).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.QRCodes)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PhysicalProfile ───────────────────────────────────────
        modelBuilder.Entity<PhysicalProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WeightKg).HasColumnType("decimal(5,2)");
            entity.Property(e => e.HeightCm).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BodyFatPercent).HasColumnType("decimal(5,2)");
            entity.Property(e => e.SkeletalMuscleMassKg).HasColumnType("decimal(5,2)");

            entity.HasOne(e => e.Member)
                .WithOne(u => u.PhysicalProfile)
                .HasForeignKey<PhysicalProfile>(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── WorkoutPlan ───────────────────────────────────────────
        modelBuilder.Entity<WorkoutPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PlanStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.NutritionAdvice).HasMaxLength(1000);
            // GBR-94: at most one active personal plan per member, even under concurrent generation.
            entity.HasIndex(e => e.MemberUserId).IsUnique()
                .HasFilter("[PlanStatus] = 'Active' AND [IsTemplate] = 0 AND [IsDeleted] = 0")
                .HasDatabaseName("IX_WorkoutPlans_MemberUserId_ActivePlan");

            entity.HasOne(e => e.Member)
                .WithMany(u => u.WorkoutPlans)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PhysicalProfile)
                .WithMany(p => p.WorkoutPlans)
                .HasForeignKey(e => e.PhysicalProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Exercise ──────────────────────────────────────────────
        modelBuilder.Entity<Exercise>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
        });

        // ── PlanItem ──────────────────────────────────────────────
        modelBuilder.Entity<PlanItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.WorkoutPlan)
                .WithMany(p => p.PlanItems)
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Exercise)
                .WithMany(ex => ex.PlanItems)
                .HasForeignKey(e => e.ExerciseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Equipment)
                .WithMany()
                .HasForeignKey(e => e.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── WorkoutLog ────────────────────────────────────────────
        modelBuilder.Entity<WorkoutLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WeightKg).HasColumnType("decimal(6,2)");
            entity.HasIndex(e => new { e.PlanItemId, e.LoggedOn }).IsUnique();   // one log per exercise per day

            entity.HasOne(e => e.PlanItem)
                .WithMany(i => i.Logs)
                .HasForeignKey(e => e.PlanItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CatalogCategory ───────────────────────────────────────
        modelBuilder.Entity<CatalogCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CategoryName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // ── Equipment ─────────────────────────────────────────────
        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EquipmentName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.EquipmentStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.PurchasePrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.EquipmentModel).HasMaxLength(100);
            entity.Property(e => e.EquipmentBrand).HasMaxLength(100);

            entity.HasOne(e => e.Gym)
                .WithMany(g => g.Equipments)
                .HasForeignKey(e => e.GymId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CatalogCategory)
                .WithMany(c => c.Equipments)
                .HasForeignKey(e => e.CatalogCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── EquipmentIssueReport ──────────────────────────────────
        modelBuilder.Entity<EquipmentIssueReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IssueStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.IncidentType).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(e => e.Equipment)
                .WithMany(eq => eq.IssueReports)
                .HasForeignKey(e => e.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReportedByUser)
                .WithMany(u => u.EquipmentIssueReports)
                .HasForeignKey(e => e.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.HandledByUser)
                .WithMany()
                .HasForeignKey(e => e.HandledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── EquipmentSafetyConfirmation ───────────────────────────
        modelBuilder.Entity<EquipmentSafetyConfirmation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Equipment)
                .WithMany(eq => eq.SafetyConfirmations)
                .HasForeignKey(e => e.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.EquipmentIssueReport)
                .WithMany(r => r.SafetyConfirmations)
                .HasForeignKey(e => e.EquipmentIssueReportId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ConfirmedByUser)
                .WithMany()
                .HasForeignKey(e => e.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PurchaseProposal ──────────────────────────────────────
        modelBuilder.Entity<PurchaseProposal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProposalNumber).IsUnique();
            entity.Property(e => e.ProposalNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ProposalStatus).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(e => e.ProposedByUser)
                .WithMany(u => u.PurchaseProposals)
                .HasForeignKey(e => e.ProposedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PurchaseProposalItem ──────────────────────────────────
        modelBuilder.Entity<PurchaseProposalItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EstimatedUnitPrice).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.PurchaseProposal)
                .WithMany(p => p.Items)
                .HasForeignKey(e => e.PurchaseProposalId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Equipment)
                .WithMany()
                .HasForeignKey(e => e.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PurchaseProposalReview ────────────────────────────────
        modelBuilder.Entity<PurchaseProposalReview>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(e => e.PurchaseProposal)
                .WithMany(p => p.Reviews)
                .HasForeignKey(e => e.PurchaseProposalId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplementProduct ─────────────────────────────────────
        modelBuilder.Entity<SupplementProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ProductStatus).HasConversion<string>().HasMaxLength(20);
        });

        // ── SupplementOrder ───────────────────────────────────────
        modelBuilder.Entity<SupplementOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.OrderStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.DeliveryType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Customer)
                .WithMany(u => u.SupplementOrders)
                .HasForeignKey(e => e.CustomerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplementOrderItem ───────────────────────────────────
        modelBuilder.Entity<SupplementOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPriceAtPurchase).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.SupplementOrder)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.SupplementOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SupplementProduct)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(e => e.SupplementProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplementOrderDelivery ───────────────────────────────
        modelBuilder.Entity<SupplementOrderDelivery>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.SupplementOrder)
                .WithOne(o => o.Delivery)
                .HasForeignKey<SupplementOrderDelivery>(e => e.SupplementOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Competition ───────────────────────────────────────────
        modelBuilder.Entity<Competition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CompetitionName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── CompetitionPrize ──────────────────────────────────────
        modelBuilder.Entity<CompetitionPrize>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PrizeName).HasMaxLength(256).IsRequired();

            entity.HasOne(e => e.Competition)
                .WithMany(c => c.Prizes)
                .HasForeignKey(e => e.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Voucher)
                .WithMany()
                .HasForeignKey(e => e.VoucherId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── CompetitionJoin ───────────────────────────────────────
        modelBuilder.Entity<CompetitionJoin>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CompetitionId, e.MemberUserId }).IsUnique();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Score).HasColumnType("decimal(10,2)");

            entity.HasOne(e => e.Competition)
                .WithMany(c => c.Participants)
                .HasForeignKey(e => e.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Member)
                .WithMany(u => u.CompetitionRegistrations)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ConfirmedByUser)
                .WithMany()
                .HasForeignKey(e => e.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Notification ──────────────────────────────────────────
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NotificationType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(256).IsRequired();

            entity.HasOne(e => e.Recipient)
                .WithMany(u => u.Notifications)
                .HasForeignKey(e => e.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── GymAnnouncement ───────────────────────────────────────
        modelBuilder.Entity<GymAnnouncement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(256).IsRequired();

            entity.HasOne(e => e.Gym)
                .WithMany(g => g.Announcements)
                .HasForeignKey(e => e.GymId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TrainerReview ─────────────────────────────────────────
        modelBuilder.Entity<TrainerReview>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReviewStatus).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(e => e.TrainerUser)
                .WithMany()
                .HasForeignKey(e => e.TrainerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MemberUser)
                .WithMany(u => u.TrainerReviewsGiven)
                .HasForeignKey(e => e.MemberUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── BannedWordList ────────────────────────────────────────
        modelBuilder.Entity<BannedWordList>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Word).IsUnique();
            entity.Property(e => e.Word).HasMaxLength(100).IsRequired();

            entity.HasOne(e  => e.Admin)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AuditLog ──────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.AuditEntity).HasMaxLength(100).IsRequired();

            entity.HasOne(e => e.ActorUser)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── BusinessReportSnapshot ────────────────────────────────
        modelBuilder.Entity<BusinessReportSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReportName).HasMaxLength(256).IsRequired();

            entity.HasOne(e => e.GeneratedByUser)
                .WithMany()
                .HasForeignKey(e => e.GeneratedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ReportMetric ──────────────────────────────────────────
        modelBuilder.Entity<ReportMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MetricName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MetricType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.MetricValue).HasColumnType("decimal(18,4)");
            entity.Property(e => e.ValueType).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.BusinessReportSnapshot)
                .WithMany(s => s.Metrics)
                .HasForeignKey(e => e.BusinessReportSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TrainerProfile ────────────────────────────────────────
        // One profile per trainer. YogaClass.InstructorUserId points at ApplicationUsers only: a Yoga Instructor
        // created by an Admin has no TrainerProfile yet, so a second FK to TrainerProfiles broke every class insert.
        modelBuilder.Entity<TrainerProfile>()
            .HasIndex(tp => tp.PTUserId)
            .IsUnique();
    }

    // ─────────────────────────────────────────────────────────────
    //  SaveChanges – auto-set UpdatedAt
    // ─────────────────────────────────────────────────────────────
    public override int SaveChanges()
    {
        SetTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTimestamps()
    {
        var entries = ChangeTracker.Entries<VFMS.Domain.Common.BaseEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is ApplicationUser user && entry.Property(nameof(ApplicationUser.PasswordHash)).IsModified)
                {
                    // Clear reset links on any tracked password change, including concurrent token issuance.
                    user.ResetTokenHash = null;
                    user.ResetTokenExpiresAt = null;
                    entry.Property(nameof(ApplicationUser.ResetTokenHash)).IsModified = true;
                    entry.Property(nameof(ApplicationUser.ResetTokenExpiresAt)).IsModified = true;
                }
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
