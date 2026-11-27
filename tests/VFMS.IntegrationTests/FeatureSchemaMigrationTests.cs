using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed class FeatureSchemaMigrationTests
{
    private const string Previous = "20261126140850_TrainerInterviewTimestamp";
    private const string Latest = "20261127112843_SimplifyFeature3456Schema";
    private const string ConflictingAccountAvatar = "/uploads/avatars/account-avatar.png";

    [SqlTrainerFact]
    public async Task GenericPTFreezeLimitsCannotSilentlyReplacePreviouslyUnconfiguredPTLimits()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await SeedPurchasedTerms(db);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE PackageComponents SET PTFreezeMinimumDays=NULL, MinFreezeDaysPerRequest=3 WHERE ComponentType=N'PT'
            """);

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51127, error.Number);
        Assert.Equal(19, await ColumnCount(db, "PackageComponents"));
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>(
            "SELECT MinFreezeDaysPerRequest AS [Value] FROM PackageComponents WHERE ComponentType=N'PT'").SingleAsync());
    }

    [SqlTrainerFact]
    public async Task MalformedCapturedSessionCountIsRejectedWithoutOverwritingItsEvidence()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await SeedPurchasedTerms(db);
        const string malformed = "[{\"Type\":2,\"SessionCount\":{\"legacy\":1}}]";
        await db.Database.ExecuteSqlRawAsync("UPDATE OrderItems SET ComponentTermsSnapshot=@terms",
            new SqlParameter("@terms", malformed));

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51113, error.Number);
        Assert.Equal(malformed, await db.Database.SqlQueryRaw<string>(
            "SELECT ComponentTermsSnapshot AS [Value] FROM OrderItems").SingleAsync());
        Assert.Equal(26, await ColumnCount(db, "PTPurchases"));
    }

    [SqlTrainerFact]
    public async Task ExistingPTTermsWithMissingCountUseTheCapturedPurchaseCountAndKeepOtherTerms()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await SeedPurchasedTerms(db);
        await db.Database.ExecuteSqlRawAsync("UPDATE OrderItems SET ComponentTermsSnapshot=@terms",
            new SqlParameter("@terms", "[{\"Type\":2,\"SessionCount\":null,\"Days\":42}]"));

        await migrator.MigrateAsync(Latest);

        var purchase = await db.PTPurchases.Include(p => p.Order!).ThenInclude(o => o.Items).SingleAsync();
        Assert.Equal(12, purchase.TotalSessions);
        using var terms = JsonDocument.Parse(purchase.PurchasedItem.ComponentTermsSnapshot!);
        Assert.Equal(42, terms.RootElement[0].GetProperty("Days").GetInt32());
    }
    private const string EarlyResumePayload = """{"ResumeDate":"2026-12-05","CurrentResumeDate":"2026-12-09","Moves":[]}""";

    [SqlTrainerFact]
    public async Task MigrationPreservesSharedIdentityAndPurchasedTermsBeforeDroppingDuplicates()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await SeedPurchasedTerms(db);

        await migrator.MigrateAsync(Latest);

        var trainers = await db.TrainerProfiles.AsNoTracking()
            .Include(p => p.PTUser).ThenInclude(u => u.PhysicalProfile)
            .ToDictionaryAsync(p => p.PTUserId);
        var active = trainers["legacy-pt"];
        Assert.Equal($"/api/trainers/{active.Id}/avatar", active.PTUser.AvatarUrl);
        var avatar = JsonSerializer.Deserialize<TrainerStoredFile>(active.AvatarJson!)!;
        Assert.Equal(("historical-avatar", "avatar.png", true), (avatar.Id, avatar.FileName, avatar.IsAvatar));
        Assert.Equal(new DateOnly(2026, 11, 6), active.ReadinessSince);
        var survey = Assert.IsType<PhysicalProfile>(active.PTUser.PhysicalProfile);
        Assert.Equal("Male", survey.Gender);
        Assert.Equal(74.25m, survey.WeightKg);
        Assert.Equal("Build strength", survey.FitnessGoal);

        var inactive = trainers["legacy-member"];
        Assert.Equal(new DateOnly(2026, 11, 8), inactive.ReadinessSince);
        Assert.Equal(5, inactive.NotAcceptingAccumulatedDays);
        Assert.Equal("Female", inactive.PTUser.PhysicalProfile!.Gender);

        var purchase = await db.PTPurchases.AsNoTracking()
            .Include(p => p.Order!).ThenInclude(o => o.Items)
            .Include(p => p.Order!).ThenInclude(o => o.PaymentTransaction)
            .Include(p => p.Sessions)
            .Include(p => p.PTFreezes)
            .AsSplitQuery()
            .SingleAsync();
        Assert.Equal(("Purchased PT package", 12, 600_000m),
            (purchase.PackageNameSnapshot, purchase.TotalSessions, purchase.PurchasePrice));
        var session = Assert.Single(purchase.Sessions);
        Assert.Equal(PTSessionStatus.CancellationRequested, session.SessionStatus);
        Assert.Equal(new TimeOnly(19, 0), session.ProposedEndTime);
        Assert.Equal("legacy-member", session.MemberUserId);
        var freeze = Assert.Single(purchase.PTFreezes);
        Assert.Equal(PTFreezeRequestStatus.Approved, freeze.RequestStatus);
        Assert.Equal(PTFreezeChangeState.EarlyResumePending, freeze.ChangeState);
        Assert.Equal(new DateOnly(2026, 11, 28), freeze.ActualStartDate);
        using var change = JsonDocument.Parse(freeze.ChangePayloadJson!);
        Assert.Equal("2026-12-05", change.RootElement.GetProperty("ResumeDate").GetString());

        var component = await db.PackageComponents.SingleAsync(c => c.ComponentType == ComponentType.PT);
        Assert.Equal(24, component.SessionCount);
        Assert.Equal(7, component.MinFreezeDaysPerRequest);
        Assert.Equal(15, component.MaxFreezeDaysPerRequest);

        (string Table, int Count)[] expected =
        [
            ("ApplicationUsers", 19), ("PhysicalProfiles", 18), ("TrainerProfiles", 17), ("PTPurchases", 16),
            ("PTSessions", 31), ("PTFreezes", 22), ("PTAvailabilities", 8), ("PackageComponents", 17)
        ];
        foreach (var (table, count) in expected)
            Assert.Equal(count, await ColumnCount(db, table));
    }

    [SqlTrainerFact]
    public async Task ConflictingAvatarSourcesRejectTheMigrationAndKeepHistoricalColumnsAndValues()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, ConflictingAccountAvatar);

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51101, error.Number);
        Assert.Contains("avatar", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(22, await ColumnCount(db, "TrainerProfiles"));
        Assert.Equal(ConflictingAccountAvatar, await db.Database.SqlQueryRaw<string>("""
            SELECT AvatarUrl AS [Value] FROM ApplicationUsers WHERE UserId = N'legacy-pt'
            """).SingleAsync());
        Assert.StartsWith("/api/trainers/", await db.Database.SqlQueryRaw<string>("""
            SELECT AvatarUrl AS [Value] FROM TrainerProfiles WHERE PTUserId = N'legacy-pt'
            """).SingleAsync());
    }

    [SqlTrainerFact]
    public async Task ActiveTrainerUploadConflictsWithAccountAvatarEvenWhenLegacyProfileUrlIsNull()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, ConflictingAccountAvatar);
        await db.Database.ExecuteSqlRawAsync("UPDATE TrainerProfiles SET AvatarUrl = NULL WHERE PTUserId = N'legacy-pt'");

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51101, error.Number);
        Assert.Contains("uploaded trainer avatar", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(22, await ColumnCount(db, "TrainerProfiles"));
        Assert.Equal(ConflictingAccountAvatar, await db.Database.SqlQueryRaw<string>("""
            SELECT AvatarUrl AS [Value] FROM ApplicationUsers WHERE UserId = N'legacy-pt'
            """).SingleAsync());
    }

    [SqlTrainerFact]
    public async Task OrphanPurchaseRejectsTheMigrationAndRollsBackCanonicalBackfills()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            INSERT INTO PTPurchases (MemberUserId, PTUserId, [Status], StartDate, EndDate, PurchaseDate,
                PackageNameSnapshot, PurchasePrice, TotalSessions, RemainingSessions, IsCompleted,
                FreezeMinimumDaysSnapshot, FreezeMaxTotalDaysSnapshot, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'legacy-member', N'legacy-pt', N'Active', '20261105', '20261205', '20261104',
                N'Orphan PT purchase', 600000, 12, 12, 0, 7, 15, @now, @now, 0);
            """);

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51110, error.Number);
        Assert.Contains("PT purchase", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(26, await ColumnCount(db, "PTPurchases"));
        Assert.Equal(12, await db.Database.SqlQueryRaw<int>("SELECT TotalSessions AS [Value] FROM PTPurchases").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM ApplicationUsers WHERE UserId = N'legacy-pt' AND AvatarUrl IS NOT NULL
            """).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM PhysicalProfiles WHERE MemberUserId = N'legacy-pt' AND Gender IS NOT NULL
            """).SingleAsync());
    }

    [SqlTrainerFact]
    public async Task ApprovedPTFreezeWithoutARecordedActualStartRejectsTheMigration()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await SeedPurchasedTerms(db);
        await db.Database.ExecuteSqlRawAsync("UPDATE PTFreezes SET ActualStartDate = NULL");

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51124, error.Number);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(29, await ColumnCount(db, "PTFreezes"));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM PTFreezes WHERE RequestStatus = N'Approved' AND ActualStartDate IS NULL
            """).SingleAsync());
    }

    [SqlTrainerFact]
    public async Task LockerTermsThatDifferFromTheLinkedPolicyRejectTheMigration()
    {
        await using var fixture = new TrainerTestDatabase();
        var db = fixture.Db;
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedTrainerIdentity(db, accountAvatar: null);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            DECLARE @gym int = (SELECT GymId FROM ApplicationUsers WHERE UserId = N'legacy-member');
            INSERT INTO LockerZones (GymId, ZoneName, FloorNumber, GenderRestriction, TotalLockers, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (@gym, N'Legacy zone', 1, N'None', 1, @now, @now, 0);
            DECLARE @zone int = SCOPE_IDENTITY();
            INSERT INTO Lockers (LockerZoneId, LockerNumber, LockerStatus, LockerType, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (@zone, N'L-01', N'Rented', N'Standard', @now, @now, 0);
            DECLARE @locker int = SCOPE_IDENTITY();
            INSERT INTO LockerPricingPolicies (PolicyName, PolicyStatus, RentalType, DurationMonths, RentalPrice,
                DepositAmount, OverdueDailyFee, KeyReplacementFee, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'Legacy policy', N'Active', N'Monthly', 1, 120000, 200000, 10000, 0, @now, @now, 0);
            DECLARE @policy int = SCOPE_IDENTITY();
            INSERT INTO LockerRentalContracts (LockerId, LockerPricingPolicyId, CustomerUserId, ContractNumber, ContractStatus,
                StartDate, EndDate, RentalPrice, DepositAmount, RentalTypeSnapshot, TermMonthsSnapshot, OverdueDailyFeeSnapshot,
                CreatedAt, UpdatedAt, IsDeleted)
            VALUES (@locker, @policy, N'legacy-member', N'RENT-LEGACY-001', N'Active',
                '20261101', '20261201', 130000, 200000, N'Monthly', 1, 10000, @now, @now, 0);
            """);

        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Latest));

        Assert.Equal(51131, error.Number);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(35, await ColumnCount(db, "LockerRentalContracts"));
        Assert.Equal(130_000m, await db.Database.SqlQueryRaw<decimal>(
            "SELECT RentalPrice AS [Value] FROM LockerRentalContracts").SingleAsync());
    }

    private static Task<int> ColumnCount(VfmsDbContext db, string table) =>
        db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID({table})").SingleAsync();

    private static Task<int> SeedTrainerIdentity(VfmsDbContext db, string? accountAvatar) =>
        db.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @now datetime2 = SYSUTCDATETIME();
            INSERT INTO Gyms (GymName, IsAcceptingMembers, IsAcceptingTrainerApplications,
                RenewalAdvanceDays, RenewalGraceDays, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'Legacy gym', 1, 1, 30, 7, @now, @now, 0);
            DECLARE @gym int = SCOPE_IDENTITY();
            INSERT INTO ApplicationUsers (UserId, GymId, FullName, Email, PhoneNumber, PasswordHash, AccountStatus,
                AvatarUrl, IsBanned, Id, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'legacy-pt', @gym, N'Legacy PT', N'trainer@example.test', N'0900000001', N'', N'Active',
                    {{accountAvatar}}, 0, 1, @now, @now, 0),
                   (N'legacy-member', @gym, N'Legacy Member', N'member@example.test', N'0900000002', N'', N'Active',
                    NULL, 0, 2, @now, @now, 0);
            INSERT INTO PhysicalProfiles (MemberUserId, WeightKg, FitnessGoal, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'legacy-pt', 74.25, N'Build strength', @now, @now, 0);
            DECLARE @ptRole int = (SELECT Id FROM TrainerRoles WHERE RoleName = N'PT');
            INSERT INTO TrainerProfiles (PTUserId, TrainerRoleId, Specialty, ExperienceYears, Gender, [Level],
                CertificatesJson, AvatarJson, AcceptingNewMembers, AcceptingSince, NotAcceptingSince,
                NotAcceptingAccumulatedDays, IsActive, CreatedAt, UpdatedAt, IsDeleted)
            VALUES (N'legacy-pt', @ptRole, N'Strength', 5, N'Male', N'Senior', N'[]',
                    N'{"Id":"historical-avatar","FileName":"avatar.png","ContentType":"image/png","IsAvatar":true,"IsPublic":true}',
                    1, '20261106', NULL, 0, 1, @now, @now, 0),
                   (N'legacy-member', @ptRole, N'Strength', 2, N'Female', N'Junior', N'[]', NULL,
                    0, NULL, '20261108', 5, 1, @now, @now, 0);
            UPDATE TrainerProfiles SET AvatarUrl = CONCAT(N'/api/trainers/', Id, N'/avatar') WHERE PTUserId = N'legacy-pt';
            """);

    private static Task<int> SeedPurchasedTerms(VfmsDbContext db) => db.Database.ExecuteSqlInterpolatedAsync($$"""
        DECLARE @now datetime2 = SYSUTCDATETIME();
        INSERT INTO Packages (Name, Price, PackageStatus, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (N'Current PT package', 900000, N'Active', @now, @now, 0);
        DECLARE @package int = SCOPE_IDENTITY();
        INSERT INTO PackageComponents (PackageId, ComponentType, SessionCount, AllowFreeze, PTFreezeMinimumDays,
            PTFreezeMaxTotalDays, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@package, N'PT', 24, 0, 7, 15, @now, @now, 0);
        INSERT INTO Orders (PurchaserUserId, OrderNumber, Channel, OrderStatus, CompletedAt, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (N'legacy-member', N'ORD-LEGACY-PT', N'Counter', N'Paid', @now, @now, @now, 0);
        DECLARE @order int = SCOPE_IDENTITY();
        INSERT INTO OrderItems (OrderId, PackageId, BeneficiaryUserId, PackageNameSnapshot, UnitPriceSnapshot,
            CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@order, @package, N'legacy-member', N'Purchased PT package', 600000, @now, @now, 0);
        INSERT INTO PaymentTransactions (OrderId, UserId, GatewayName, PaymentMethod, PaymentStatus, PaidAt, AmountDue,
            AmountReceived, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@order, N'legacy-member', N'Cash', N'Cash', N'Paid', @now, 600000, 600000, @now, @now, 0);
        INSERT INTO PTPurchases (OrderId, MemberUserId, PTUserId, [Status], StartDate, EndDate, PurchaseDate,
            PackageNameSnapshot, PurchasePrice, TotalSessions, RemainingSessions, IsCompleted,
            FreezeMinimumDaysSnapshot, FreezeMaxTotalDaysSnapshot, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@order, N'legacy-member', N'legacy-pt', N'Active', '20261105', '20261220', '20261104',
            N'Purchased PT package', 600000, 12, 12, 0, 7, 15, @now, @now, 0);
        DECLARE @purchase int = SCOPE_IDENTITY();
        INSERT INTO PTSessions (PTPurchaseId, MemberUserId, PTUserId, SessionDate, StartTime, EndTime, SessionStatus,
            ProposedDate, ProposedStartTime, ProposedEndTime, ProposedAdjustmentType, ProposalExpiresAt, IsCreditForfeited,
            CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@purchase, N'legacy-member', N'legacy-pt', '20261127', '18:00:00', '19:00:00', N'RescheduleRequested',
            '20261127', '18:00:00', '19:00:00', N'Cancellation', '2026-11-27T08:00:00', 0, @now, @now, 0);
        INSERT INTO PTFreezes (PTPurchaseId, RequestedByUserId, RequestedStartDate, RequestedEndDate, ActualStartDate,
            ActualEndDate, RequestStatus, MinimumDaysSnapshot, MaxDaysSnapshot, ChangeRequestType, ChangeRequestStatus,
            ChangeResumeDate, ChangePayloadJson, ChangeRequestedAt, CreatedAt, UpdatedAt, IsDeleted)
        VALUES (@purchase, N'legacy-member', '20261128', '20261209', '20261128', '20261209', N'Approved', 7, 15,
            N'EarlyResume', N'Pending', '20261205', {{EarlyResumePayload}},
            @now, @now, @now, 0);
        """);
}
