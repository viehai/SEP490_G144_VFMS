using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Contracts.Users;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class ApplicationUserIdentitySqlTests
{
    private const string Password = "Synthetic@2026Password";
    private const string Actor = "manager";

    [SqlTrainerFact]
    public async Task RegisterWalkInInternalAndProfileAgreeOnCanonicalIdentityAndLogin()
    {
        await using var fixture = await InitializeAsync();
        await using var db = fixture.CreateContext();
        var auth = Auth(fixture, db);
        var administration = Administration(db);
        var owner = await auth.RegisterAsync(new("Synthetic identity owner", " OWNER@Example.test ", "(0912) 345-678", Password, Password));
        Assert.True(owner.IsSuccess);
        Assert.Equal("owner@example.test", owner.Data!.Email);
        foreach (var identifier in new[] { " Owner@Example.TEST ", "0912345678", "0912-345-678", "0912 345 678" })
        {
            var login = await auth.LoginAsync(new(identifier, Password), null);
            Assert.True(login.IsSuccess);
            Assert.Equal(owner.Data.UserId, login.Data!.UserId);
        }

        Assert.Equal("AUTH_EMAIL_EXISTS", (await auth.RegisterAsync(new("Duplicate email", "Owner@example.TEST", "0912000001", Password, Password))).ErrorCode);
        Assert.Equal("USER_PHONE_EXISTS", (await auth.RegisterAsync(new("Duplicate phone", "second@example.test", "0912 345 678", Password, Password))).ErrorCode);
        Assert.Equal("AUTH_EMAIL_EXISTS", (await WalkIn(administration, " OWNER@example.test ", "0912000002")).ErrorCode);
        Assert.Equal("USER_PHONE_EXISTS", (await WalkIn(administration, "walkin@example.test", "0912-345-678")).ErrorCode);
        Assert.Equal("AUTH_EMAIL_EXISTS", (await administration.CreateStaffAsync(new("Synthetic staff", " OWNER@example.test ", "0912000003", Password, Password, "Manager"), Actor, null)).ErrorCode);
        Assert.Equal("USER_PHONE_EXISTS", (await administration.CreateStaffAsync(new("Synthetic staff", "staff@example.test", "0912-345-678", Password, Password, "Manager"), Actor, null)).ErrorCode);

        var profile = await auth.RegisterAsync(new("Synthetic profile", "profile@example.test", "0912000004", Password, Password));
        Assert.True(profile.IsSuccess);
        Assert.Equal("USER_PHONE_EXISTS", (await administration.UpdateMyProfileAsync(profile.Data!.UserId, new("Synthetic profile", "(0912) 345 678"))).ErrorCode);
        var updated = await administration.UpdateMyProfileAsync(profile.Data.UserId, new("Synthetic profile", "0912-000-005"));
        Assert.True(updated.IsSuccess);
        Assert.Equal("0912000005", updated.Data!.PhoneNumber);
        Assert.Equal(profile.Data.UserId, (await auth.LoginAsync(new("0912 000 005", Password), null)).Data!.UserId);
    }

    [SqlTrainerFact]
    public async Task RegisterAndWalkInRacingCanonicalEmailAndPhoneHaveOneWinnerPerIdentity()
    {
        await using var fixture = await InitializeAsync();
        foreach (var (email, registerPhone, walkInEmail, walkInPhone, errorCode) in new[]
        {
            ("register-email@example.test", "0912000010", " REGISTER-EMAIL@EXAMPLE.TEST ", "0912000011", "AUTH_EMAIL_EXISTS"),
            ("register-phone@example.test", "0912000012", "walkin-phone@example.test", "(0912) 000-012", "USER_PHONE_EXISTS")
        })
        {
            var gate = new ConcurrentSaveGate();
            await using var registerDb = ContextWithGate(fixture, gate);
            await using var walkInDb = ContextWithGate(fixture, gate);
            var register = Auth(fixture, registerDb).RegisterAsync(new("Synthetic register", email, registerPhone, Password, Password));
            var walkIn = WalkIn(Administration(walkInDb), walkInEmail, walkInPhone);
            await Task.WhenAll(register, walkIn);
            AssertOneIdentityWrite(register.Result, walkIn.Result, errorCode);
        }
    }

    [SqlTrainerFact]
    public async Task ProfileUpdateAndWalkInRacingCanonicalPhoneHaveOneWinner()
    {
        await using var fixture = await InitializeAsync();
        await using var seed = fixture.CreateContext();
        var registered = await Auth(fixture, seed).RegisterAsync(new("Synthetic profile", "profile-race@example.test", "0912000020", Password, Password));
        Assert.True(registered.IsSuccess);
        var gate = new ConcurrentSaveGate();
        await using var profileDb = ContextWithGate(fixture, gate);
        await using var walkInDb = ContextWithGate(fixture, gate);
        var profile = Administration(profileDb).UpdateMyProfileAsync(registered.Data!.UserId, new("Synthetic profile", "(0912) 000-021"));
        var walkIn = WalkIn(Administration(walkInDb), "walkin-race@example.test", "0912000021");
        await Task.WhenAll(profile, walkIn);
        AssertOneIdentityWrite(profile.Result, walkIn.Result, "USER_PHONE_EXISTS");
        Assert.Equal(1, await seed.ApplicationUsers.CountAsync(user => user.PhoneNumber == "0912000021"));
    }

    [SqlTrainerFact]
    public async Task TrainerHiringAndWalkInRacingCanonicalPhoneNeverCreateDuplicateAccounts()
    {
        await using var fixture = await InitializeAsync();
        var submitted = await fixture.Service.Submit(TrainerTestDatabase.Request with { PhoneNumber = "0912-000-030" }, null, default);
        Assert.True(submitted.IsSuccess);
        var application = await fixture.Db.TrainerApplications.AsNoTracking().SingleAsync();
        Assert.True((await fixture.Service.Review(application.Id, new("Interview", "Interview completed"), Actor, default)).IsSuccess);
        var gate = new ConcurrentSaveGate();
        await using var trainerDb = ContextWithGate(fixture, gate);
        await using var walkInDb = ContextWithGate(fixture, gate);
        var hire = fixture.CreateService(trainerDb).Review(application.Id, new("Hire", "Verified", "Junior", null, true), Actor, default);
        var walkIn = WalkIn(Administration(walkInDb), "trainer-race@example.test", "(0912) 000 030");
        await Task.WhenAll(hire, walkIn);
        Assert.NotEqual(hire.Result.IsSuccess, walkIn.Result.IsSuccess);
        Assert.Equal(409, hire.Result.IsSuccess ? walkIn.Result.StatusCode : hire.Result.StatusCode);
        Assert.Equal(1, await fixture.Db.ApplicationUsers.CountAsync(user => user.PhoneNumber == "0912000030"));

        await using var checkDb = fixture.CreateContext();
        var repeat = await fixture.CreateService(checkDb).Review(application.Id, new("Hire", "Verified", "Junior", null, true), Actor, default);
        if (walkIn.Result.IsSuccess)
            Assert.Equal("TRAINER_IDENTITY_CONFLICT", repeat.ErrorCode);
        else
            Assert.True(repeat.IsSuccess);
    }

    [SqlTrainerFact]
    public async Task TrainerHiringCannotCreateAnAccountForAPendingCustomerEmailOrPhone()
    {
        await using var fixture = await InitializeAsync();
        await using var db = fixture.CreateContext();
        var created = await WalkIn(Administration(db), "pending@example.test", "0912000040");
        Assert.True(created.IsSuccess);

        var requests = new[]
        {
            TrainerTestDatabase.Request with { Email = " PENDING@EXAMPLE.TEST ", PhoneNumber = "0912000041" },
            TrainerTestDatabase.Request with { Email = "new-trainer@example.test", PhoneNumber = "(0912) 000-040" }
        };
        foreach (var request in requests)
        {
            var submitted = await fixture.Service.Submit(request, null, default);
            Assert.True(submitted.IsSuccess);
            var application = await fixture.Db.TrainerApplications.AsNoTracking().SingleAsync(row => row.TrackingCode == submitted.Data!.TrackingCode);
            Assert.True((await fixture.Service.Review(application.Id, new("Interview", "Interview completed"), Actor, default)).IsSuccess);
            var hiring = await fixture.Service.Review(application.Id, new("Hire", "Verified", "Junior", null, true), Actor, default);
            Assert.Equal("TRAINER_IDENTITY_CONFLICT", hiring.ErrorCode);
        }

        Assert.Equal(2, await fixture.Db.ApplicationUsers.CountAsync());
    }

    private static async Task<TrainerTestDatabase> InitializeAsync()
    {
        var fixture = new TrainerTestDatabase();
        try
        {
            await fixture.Initialize();
            fixture.Db.Add(new ApplicationRole { RoleName = "Customer" });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static AuthService Auth(TrainerTestDatabase fixture, VfmsDbContext db) =>
        new(new AuthRepository(db), new PasswordHasher(), new JwtTokenService(fixture.Configuration, db));

    private static UserAdministrationService Administration(VfmsDbContext db)
    {
        var mapping = new TypeAdapterConfig();
        mapping.Scan(typeof(UserAdministrationMapping).Assembly);
        return new(new UserAdministrationRepository(db), new PasswordHasher(), new Mapper(mapping), new NoOpEmail(),
            CustomerPasswordSetupSettings.FromConfiguration("https://fitness.example.com/auth/setup-password", false));
    }

    private static VfmsDbContext ContextWithGate(TrainerTestDatabase fixture, ConcurrentSaveGate gate) =>
        new(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlServer(fixture.Db.Database.GetConnectionString())
            .AddInterceptors(gate).Options);

    private static Task<Result<WalkInCustomerResponse>> WalkIn(UserAdministrationService service, string email, string phone) =>
        service.CreateWalkInCustomerAsync(new("Synthetic customer", phone, email), Actor, null);

    private static void AssertOneIdentityWrite<TLeft, TRight>(Result<TLeft> left, Result<TRight> right, string code)
    {
        Assert.NotEqual(left.IsSuccess, right.IsSuccess);
        Assert.Equal(409, left.IsSuccess ? right.StatusCode : left.StatusCode);
        Assert.Equal(code, left.IsSuccess ? right.ErrorCode : left.ErrorCode);
    }

    private sealed class ConcurrentSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.SetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed class NoOpEmail : IEmailService
    {
        public bool IsConfigured => true;
        public Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
