using System.Text.Json;
using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class TrainerSetupEligibilityTests
{
    [Fact]
    public async Task ManagerDetailOffersResendOnlyWhileHiredAccountRemainsEligible()
    {
        await using var fixture = new SetupDatabase();
        await fixture.Initialize();
        var application = await fixture.Hire();
        var db = fixture.Db;
        var user = await db.ApplicationUsers.SingleAsync(x => x.UserId == application.HiredUserId);
        var assignment = await db.ApplicationUserRoles.SingleAsync(x => x.UserId == user.UserId);
        var trainerRole = await db.TrainerRoles.SingleAsync(x => x.Id == application.TrainerRoleId);
        var originalRoleId = assignment.RoleId;
        var managerRoleId = await db.ApplicationRoles.Where(x => x.RoleName == "Manager").Select(x => x.Id).SingleAsync();

        await AssertEligibility(fixture, application.Id, true);
        var list = TrainerTestDatabase.Success(await fixture.Service.Applications(new(), "manager", default));
        Assert.False(CanResend(Assert.Single(list.Items)));
        var tracking = TrainerTestDatabase.Success(await fixture.Service.Track(application.TrackingCode, default));
        Assert.False(JsonSerializer.SerializeToElement(tracking, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .TryGetProperty("canResendSetup", out _));

        foreach (var status in new[] { AccountStatus.Active, AccountStatus.Locked, AccountStatus.Inactive })
        {
            await db.ApplicationUsers.Where(x => x.UserId == user.UserId).ExecuteUpdateAsync(update => update.SetProperty(x => x.AccountStatus, status));
            db.ChangeTracker.Clear();
            await AssertEligibility(fixture, application.Id, false);
        }
        await db.ApplicationUsers.Where(x => x.UserId == user.UserId).ExecuteUpdateAsync(update => update
            .SetProperty(x => x.AccountStatus, AccountStatus.PendingVerification).SetProperty(x => x.IsBanned, true));
        await AssertEligibility(fixture, application.Id, false);
        await db.ApplicationUsers.Where(x => x.UserId == user.UserId).ExecuteUpdateAsync(update => update
            .SetProperty(x => x.IsBanned, false).SetProperty(x => x.IsDeleted, true));
        await AssertEligibility(fixture, application.Id, false);
        await db.ApplicationUsers.IgnoreQueryFilters().Where(x => x.UserId == user.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsDeleted, false));
        await db.ApplicationUserRoles.Where(x => x.Id == assignment.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, managerRoleId));
        await AssertEligibility(fixture, application.Id, false);
        await db.ApplicationUserRoles.Where(x => x.Id == assignment.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, originalRoleId));
        await db.TrainerRoles.Where(x => x.Id == trainerRole.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        await AssertEligibility(fixture, application.Id, false);
        await db.TrainerRoles.Where(x => x.Id == trainerRole.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, true));
        await db.TrainerApplications.Where(x => x.Id == application.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.ApplicationType, "ProfileUpdate"));
        await AssertEligibility(fixture, application.Id, false);
    }

    [Fact]
    public async Task PendingYogaAccountCanResendUntilPasswordSetupActivatesIt()
    {
        await using var fixture = new SetupDatabase();
        await fixture.Initialize();
        TrainerTestDatabase.Success(await fixture.Service.Submit(SetupDatabase.Request with { TrainerRole = "Yoga" }, null, default));
        var application = await fixture.Db.TrainerApplications.SingleAsync();
        TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Interview", "Checked"), "manager", default));
        var hired = TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Hire", "Verified", "Junior"), "manager", default));
        Assert.True(CanResend(hired));
        var repeated = TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Hire", "Verified", "Junior"), "manager", default));
        Assert.True(CanResend(repeated));

        await AssertEligibility(fixture, application.Id, true);
        // Account activation can happen after the Manager loaded an eligible detail.
        await fixture.Db.ApplicationUsers.Where(x => x.UserId == application.HiredUserId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.AccountStatus, AccountStatus.Active));
        fixture.Db.ChangeTracker.Clear();
        await AssertEligibility(fixture, application.Id, false);
    }

    [Fact]
    public async Task HiringLinkedActiveCustomerDoesNotOfferSetupResend()
    {
        await using var fixture = new SetupDatabase();
        await fixture.Initialize();
        var request = SetupDatabase.Request;
        var customerRole = await fixture.Db.ApplicationRoles.SingleAsync(x => x.RoleName == "Customer");
        var customer = new ApplicationUser
        {
            UserId = "linked-customer", FullName = "Existing customer", Email = request.Email,
            PhoneNumber = request.PhoneNumber, AccountStatus = AccountStatus.Active,
            GymId = await fixture.Db.Gyms.Select(x => x.Id).SingleAsync(), PasswordHash = "existing-password-hash"
        };
        fixture.Db.Add(customer);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(new ApplicationUserRole { UserId = customer.UserId, RoleId = customerRole.Id });
        await fixture.Db.SaveChangesAsync();

        TrainerTestDatabase.Success(await fixture.Service.Submit(request, null, default));
        var application = await fixture.Db.TrainerApplications.SingleAsync();
        TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Interview", "Checked"), "manager", default));
        var hired = TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Hire", "Verified", "Junior", customer.UserId), "manager", default));
        Assert.False(CanResend(hired));
        var repeated = TrainerTestDatabase.Success(await fixture.Service.Review(application.Id, new("Hire", "Verified", "Junior", customer.UserId), "manager", default));
        Assert.False(CanResend(repeated));

        Assert.Equal(customer.UserId, application.HiredUserId);
        await AssertEligibility(fixture, application.Id, false);
        var ownList = TrainerTestDatabase.Success(await fixture.Service.MyApplications(new(), customer.UserId, default));
        Assert.False(CanResend(Assert.Single(ownList.Items)));
    }

    private static bool CanResend(TrainerApplicationResponse response)
    {
        var json = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.TryGetProperty("canResendSetup", out var capability), "Manager response must include canResendSetup.");
        return capability.GetBoolean();
    }

    private static async Task AssertEligibility(SetupDatabase fixture, int applicationId, bool eligible)
    {
        var detail = TrainerTestDatabase.Success(await fixture.Service.Application(applicationId, "manager", default));
        Assert.Equal(eligible, CanResend(detail));
        var resend = await fixture.Service.ResendSetup(applicationId, "manager", default);
        Assert.Equal(eligible, resend.IsSuccess);
        if (!eligible) Assert.Equal("TRAINER_INVALID_STATE", resend.ErrorCode);
    }

    private sealed class SetupDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
        public VfmsDbContext Db { get; private set; } = null!;
        public TrainerService Service { get; private set; } = null!;
        public static SubmitTrainerApplicationRequest Request => TrainerTestDatabase.Request with
        {
            Certificates = [], CertificateLinks = [new("https://certificates.example.test/verified")]
        };

        public async Task Initialize()
        {
            await connection.OpenAsync();
            Db = new SetupContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connection).Options);
            await Db.Database.EnsureCreatedAsync();
            var gym = new Gym { GymName = "Setup eligibility", IsAcceptingTrainerApplications = true };
            Db.Add(gym);
            Db.AddRange(new[] { "Manager", "Customer", "PT", "YogaInstructor" }.Select(name => new ApplicationRole { RoleName = name }));
            await Db.SaveChangesAsync();
            foreach (var (name, login) in new[] { ("PT", "PT"), ("Yoga", "YogaInstructor") })
                Db.Add(new TrainerRole { RoleName = name, RoleId = await Db.ApplicationRoles.Where(x => x.RoleName == login).Select(x => x.Id).SingleAsync() });
            var manager = new ApplicationUser { UserId = "manager", GymId = gym.Id, FullName = "Manager", Email = "manager@example.test", PhoneNumber = "0900000099", AccountStatus = AccountStatus.Active };
            Db.Add(manager);
            Db.Add(new ApplicationUserRole { User = manager, RoleId = await Db.ApplicationRoles.Where(x => x.RoleName == "Manager").Select(x => x.Id).SingleAsync() });
            await Db.SaveChangesAsync();
            var proxy = DispatchProxy.Create<ITrainerRepository, TrainerContactIdentityTests.SqliteRecruitmentRepository>();
            var adapter = (TrainerContactIdentityTests.SqliteRecruitmentRepository)(object)proxy;
            adapter.Repository = new TrainerRepository(Db, new EphemeralDataProtectionProvider());
            adapter.Db = Db;
            var mapping = new TypeAdapterConfig();
            mapping.Scan(typeof(TrainerMapping).Assembly);
            Service = new(proxy, new PasswordHasher(), new Mapper(mapping));
        }

        public async Task<TrainerApplication> Hire()
        {
            TrainerTestDatabase.Success(await Service.Submit(Request, null, default));
            var app = await Db.TrainerApplications.SingleAsync();
            TrainerTestDatabase.Success(await Service.Review(app.Id, new("Interview", "Checked"), "manager", default));
            var hired = TrainerTestDatabase.Success(await Service.Review(app.Id, new("Hire", "Verified", "Junior"), "manager", default));
            Assert.True(CanResend(hired));
            var repeated = TrainerTestDatabase.Success(await Service.Review(app.Id, new("Hire", "Verified", "Junior"), "manager", default));
            Assert.True(CanResend(repeated));
            Db.ChangeTracker.Clear();
            return await Db.TrainerApplications.SingleAsync();
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class SetupContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<FaceData>().Property(x => x.FaceNetEmbedding).HasColumnType("BLOB");
            builder.Entity<Voucher>().Property(x => x.Code).UseCollation("NOCASE");
        }
    }
}
