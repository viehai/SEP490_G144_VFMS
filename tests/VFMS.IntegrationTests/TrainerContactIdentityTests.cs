using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public class TrainerContactIdentityTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private ContactContext db = null!;
    private TrainerService service = null!;
    private Gym gym = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        gym = new Gym { GymName = "Contact identity tests", IsAcceptingTrainerApplications = true };
        db.Add(gym);
        db.AddRange(new[] { "Manager", "Customer", "PT", "Staff", "YogaInstructor" }.Select(role => new ApplicationRole { RoleName = role }));
        await db.SaveChangesAsync();
        db.Add(new TrainerRole { RoleName = "PT", RoleId = await db.ApplicationRoles.Where(r => r.RoleName == "PT").Select(r => r.Id).SingleAsync() });
        await AddUser("manager", "manager@example.test", "0900000099", "Manager");
        var repository = new TrainerRepository(db, new EphemeralDataProtectionProvider());
        var proxy = DispatchProxy.Create<ITrainerRepository, SqliteRecruitmentRepository>();
        ((SqliteRecruitmentRepository)(object)proxy).Repository = repository;
        ((SqliteRecruitmentRepository)(object)proxy).Db = db;
        var config = new TypeAdapterConfig();
        config.Scan(typeof(TrainerMapping).Assembly);
        service = new(proxy, new PasswordHasher(), new Mapper(config));
    }

    [Fact]
    public async Task ChangedEmailCannotBypassCooldownWithTheSameNormalizedPhone()
    {
        Success(await service.Submit(Request("first@example.test", "0900000010"), null, default));
        var result = await service.Submit(Request("different@example.test", "(0900) 000-010"), null, default);
        Assert.Equal("RECRUITMENT_COOLDOWN", result.ErrorCode);
        Assert.Equal(1, await db.TrainerApplications.CountAsync());
    }

    [Fact]
    public async Task SameEmailCannotBypassCooldownWithAnotherPhone()
    {
        Success(await service.Submit(Request("first@example.test", "0900000010"), null, default));
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(
            Request("FIRST@example.test", "0900000011"), null, default)).ErrorCode);
    }

    [Fact]
    public async Task DifferentContactsCanSubmitAsDifferentApplicants()
    {
        Success(await service.Submit(Request("first@example.test", "0900000010"), null, default));
        Success(await service.Submit(Request("different@example.test", "0900000011"), null, default));
        Assert.Equal(2, await db.TrainerApplications.CountAsync());
    }

    [Fact]
    public async Task KnownApplicantKeepsCooldownWhenBothSubmittedContactsChange()
    {
        await AddUser("applicant", "first@example.test", "0900000010", "Customer");
        Success(await service.Submit(Request("first@example.test", "0900000010"), "applicant", default));
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(
            Request("different@example.test", "0900000011"), "applicant", default)).ErrorCode);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Manager")]
    [InlineData("PT")]
    [InlineData("YogaInstructor")]
    public async Task EmployeePhoneCannotBeReusedWithADifferentEmail(string role)
    {
        await AddUser("employee", "staff@example.test", "0900000010", role);
        Assert.Equal("TRAINER_NOT_ELIGIBLE", (await service.Submit(
            Request("different@example.test", "(0900) 000-010"), null, default)).ErrorCode);
        Assert.Empty(await db.TrainerApplications.ToListAsync());
    }

    [Fact]
    public async Task GmailAliasesCannotBypassCooldownWithAnotherPhone()
    {
        Success(await service.Submit(Request("trainer.test@gmail.com", "0900000010"), null, default));
        Assert.Equal("RECRUITMENT_COOLDOWN", (await service.Submit(
            Request("TRAINERTEST@gmail.com", "0900000011"), null, default)).ErrorCode);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task ContactMatchesKeepExplicitManagerSelection(bool conflictingEmail, int expectedCandidates)
    {
        await AddUser("first", "first@example.test", "0900000010", "Customer");
        await AddUser("second", "second@example.test", "0900000011", "Customer");
        Success(await service.Submit(Request(conflictingEmail ? "first@example.test" : "unmatched@example.test", "0900000011"), null, default));
        var application = await db.TrainerApplications.SingleAsync();
        var detail = await service.Application(application.Id, "manager", default);
        Success(detail);
        Assert.Equal("Ambiguous", detail.Data!.AccountMatch!.Status);
        Assert.Equal(expectedCandidates, detail.Data.AccountMatch.Matches!.Count);
        Success(await service.Review(application.Id, new("Interview", "Interview done"), "manager", default));
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(application.Id,
            new("Hire", null, "Junior", IdentityVerified: true), "manager", default)).ErrorCode);
        Success(await service.Review(application.Id, new("Hire", null, "Junior", "second", true), "manager", default));
        Assert.Equal("second", (await db.TrainerProfiles.SingleAsync()).PTUserId);
        Assert.Equal(3, await db.ApplicationUsers.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedApplicantCannotBeReplacedByAnotherContactMatch(bool contactsReassigned)
    {
        var owner = await AddUser("owner", "first@example.test", "0900000010", "Customer");
        var other = await AddUser("other", "second@example.test", "0900000011", "Customer");
        Success(await service.Submit(Request(owner.Email, contactsReassigned ? owner.PhoneNumber : other.PhoneNumber), owner.UserId, default));
        var application = await db.TrainerApplications.SingleAsync();
        if (contactsReassigned)
        {
            owner.Email = "changed@example.test";
            owner.PhoneNumber = "0900000012";
            await db.SaveChangesAsync();
            other.Email = "first@example.test";
            other.PhoneNumber = "0900000010";
            await db.SaveChangesAsync();
        }
        var detail = await service.Application(application.Id, "manager", default);
        Success(detail);
        Assert.Equal(("Matched", "owner"), (detail.Data!.AccountMatch!.Status, detail.Data.AccountMatch.UserId));
        Success(await service.Review(application.Id, new("Interview", "Interview done"), "manager", default));
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(application.Id,
            new("Hire", null, "Junior", "other", true), "manager", default)).ErrorCode);
        Success(await service.Review(application.Id, new("Hire", null, "Junior", "owner", true), "manager", default));
        Assert.Equal("owner", (await db.TrainerProfiles.SingleAsync()).PTUserId);
    }

    [Fact]
    public async Task DeletedLinkedApplicantCannotBeHiredAsANewAccount()
    {
        var owner = await AddUser("owner", "first@example.test", "0900000010", "Customer");
        Success(await service.Submit(Request(owner.Email, owner.PhoneNumber), owner.UserId, default));
        var application = await db.TrainerApplications.SingleAsync();
        owner.IsDeleted = true;
        await db.SaveChangesAsync();
        var detail = await service.Application(application.Id, "manager", default);
        Success(detail);
        Assert.Equal("Ineligible", detail.Data!.AccountMatch!.Status);
        Success(await service.Review(application.Id, new("Interview", "Interview done"), "manager", default));
        Assert.Equal("TRAINER_IDENTITY_CONFLICT", (await service.Review(application.Id,
            new("Hire", null, "Junior", IdentityVerified: true), "manager", default)).ErrorCode);
        Assert.Empty(await db.TrainerProfiles.ToListAsync());
    }

    [Fact]
    public async Task HiringDoesNotCloseAnotherKnownUsersContactMatchedApplication()
    {
        await AddUser("owner", "first@example.test", "0900000010", "Customer");
        await AddUser("other", "second@example.test", "0900000011", "Customer");
        Success(await service.Submit(Request("second@example.test", "0900000011"), "other", default));
        var otherApplication = await db.TrainerApplications.SingleAsync();
        otherApplication.SubmittedAt = DateTime.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();
        Success(await service.Submit(Request("first@example.test", "0900000011"), "owner", default));
        var ownApplication = await db.TrainerApplications.SingleAsync(a => a.ApplicantUserId == "owner");
        Success(await service.Review(ownApplication.Id, new("Interview", "Interview done"), "manager", default));
        Success(await service.Review(ownApplication.Id, new("Hire", null, "Junior", "owner", true), "manager", default));
        Assert.Equal(TrainerApplicationStatus.Pending, (await db.TrainerApplications.AsNoTracking()
            .SingleAsync(a => a.Id == otherApplication.Id)).ApplicationStatus);
    }

    [Fact]
    public async Task ProfileUpdateUsesUserIdWithoutAnIdentityDocument()
    {
        await AddUser("trainer", "trainer@example.test", "0900000010", "PT");
        db.Add(new TrainerProfile { PTUserId = "trainer", TrainerRoleId = await db.TrainerRoles.Select(r => r.Id).SingleAsync() });
        await db.SaveChangesAsync();
        Success(await service.Submit(Request("", "", "ProfileUpdate"), "trainer", default));
        var application = await db.TrainerApplications.SingleAsync();
        Assert.Equal(("trainer", "trainer@example.test", "0900000010"),
            (application.ApplicantUserId, application.Email, application.PhoneNumber));
    }

    [Fact]
    public async Task RehireUsesTheSameUserIdWhenAccountContactsChangeAfterSubmission()
    {
        var user = await AddUser("former", "first@example.test", "0900000010", "Customer");
        var profile = new TrainerProfile { PTUserId = user.UserId, IsActive = false };
        db.Add(profile);
        await db.SaveChangesAsync();
        var profileId = profile.Id;
        Success(await service.Submit(Request(user.Email, user.PhoneNumber), user.UserId, default));
        var application = await db.TrainerApplications.SingleAsync();
        user.Email = "different@example.test";
        user.PhoneNumber = "0900000011";
        await db.SaveChangesAsync();
        Success(await service.Review(application.Id, new("Interview", "Interview done"), "manager", default));
        Success(await service.Review(application.Id, new("Hire", null, "Junior", user.UserId, true), "manager", default));
        var reactivated = await db.TrainerProfiles.SingleAsync();
        Assert.Equal((profileId, user.UserId, true), (reactivated.Id, reactivated.PTUserId, reactivated.IsActive));
        Assert.Equal(2, await db.ApplicationUsers.CountAsync());
    }

    [Fact]
    public async Task HiringClosesOtherApplicationsWithTheSamePhone()
    {
        Success(await service.Submit(Request("first@example.test", "0900000010"), null, default));
        var first = await db.TrainerApplications.SingleAsync();
        first.SubmittedAt = DateTime.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();
        Success(await service.Submit(Request("different@example.test", "0900000010"), null, default));
        var second = await db.TrainerApplications.OrderBy(a => a.Id).LastAsync();
        Assert.Equal(2, second.SubmissionAttempt);
        Success(await service.Review(second.Id, new("Interview", "Interview done"), "manager", default));
        Success(await service.Review(second.Id, new("Hire", null, "Junior", IdentityVerified: true), "manager", default));
        Assert.Equal(TrainerApplicationStatus.Rejected, (await db.TrainerApplications.AsNoTracking().SingleAsync(a => a.Id == first.Id)).ApplicationStatus);
    }

    private async Task<ApplicationUser> AddUser(string id, string email, string phone, string role)
    {
        var user = new ApplicationUser { UserId = id, Gym = gym, FullName = id, Email = email, PhoneNumber = phone,
            AccountStatus = AccountStatus.Active, PasswordHash = "Synthetic test password hash" };
        db.Add(user);
        db.Add(new ApplicationUserRole { User = user, RoleId = await db.ApplicationRoles.Where(r => r.RoleName == role).Select(r => r.Id).SingleAsync() });
        await db.SaveChangesAsync();
        return user;
    }

    private static SubmitTrainerApplicationRequest Request(string email, string phone, string type = "Recruitment") =>
        new("Contact Applicant", email, phone, new DateOnly(2000, 1, 1), "Other", "Strength", 3,
            "Strength coach", true, [], type,
            CertificateLinks: [new("https://certificates.example.test/verified")], TrainerRole: "PT");

    private static void Success<T>(Result<T> result) => Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.ErrorMessage}");

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    private sealed class ContactContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<FaceData>().Property(f => f.FaceNetEmbedding).HasColumnType("BLOB");
            builder.Entity<Voucher>().Property(v => v.Code).UseCollation("NOCASE");
        }
    }

    // Retain real queries, persistence and transactions; only SQL Server's sp_getapplock is unavailable in SQLite.
    public class SqliteRecruitmentRepository : DispatchProxy
    {
        public ITrainerRepository Repository { get; set; } = null!;
        public VfmsDbContext Db { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "RunExclusiveAsync"
            ? typeof(SqliteRecruitmentRepository).GetMethod(nameof(Execute))!.MakeGenericMethod(method.GetGenericArguments()).Invoke(this, args)
            : method.Invoke(Repository, args);
        public async Task<Result<T>> Execute<T>(Func<Task<Result<T>>> work, CancellationToken ct)
        {
            await using var transaction = await Db.Database.BeginTransactionAsync(ct);
            var result = await work();
            if (result.IsSuccess) { await Db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            else { await transaction.RollbackAsync(ct); Db.ChangeTracker.Clear(); }
            return result;
        }
    }
}
