using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.CheckIn.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class CheckInTests
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero));
    private static float[] Descriptor(float v = 0.1f) => Enumerable.Repeat(v, 128).ToArray();

    [Fact]
    public void ValidatorRequiresConsentAnd128FiniteValues()
    {
        var validator = new RegisterFaceRequestValidator();
        Assert.True(validator.Validate(new RegisterFaceRequest(Descriptor(), true)).IsValid);
        Assert.False(validator.Validate(new RegisterFaceRequest(Descriptor(), false)).IsValid);
        Assert.False(validator.Validate(new RegisterFaceRequest(new float[64], true)).IsValid);
        var bad = Descriptor();
        bad[3] = float.NaN;
        Assert.False(validator.Validate(new RegisterFaceRequest(bad, true)).IsValid);
    }

    [Fact]
    public async Task RegisterStoresOnlyEncryptedTemplateWithConsentTime()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);

        var result = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);

        Assert.Equal(201, result.StatusCode);
        Assert.True(result.Data!.IsRegistered);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, repo.Face!.ConsentGrantedAt);
        Assert.Equal(ProtectorFake.Marker, repo.Face.FaceNetEmbedding[0]);
        Assert.DoesNotContain("0.1", Assert.Single(repo.Audits).NewValue);
    }

    [Fact]
    public async Task RegisterWithoutConsentIsRejected()
    {
        var repo = new RepoFake();
        var result = await new FaceProfileService(repo, new ProtectorFake(), Clock)
            .RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), false), null);
        Assert.Equal("FACE_CONSENT_REQUIRED", result.ErrorCode);
        Assert.Null(repo.Face);
    }

    [Fact]
    public async Task DeleteErasesTemplateAndReRegisterReusesRecord()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);
        await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);

        var deleted = await service.DeleteAsync("u1", null);

        Assert.False(deleted.Data!.IsRegistered);
        Assert.Empty(repo.Face!.FaceNetEmbedding);
        Assert.False(repo.Face.IsActive);
        Assert.Equal(404, (await service.DeleteAsync("u1", null)).StatusCode);

        var again = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(0.2f), true), null);
        Assert.Equal(200, again.StatusCode);
        Assert.True(repo.Face.IsActive);
        Assert.False(repo.Face.IsDeleted);
    }

    [Fact]
    public async Task ActiveCheckInsShowOccupancyDurationAndSearch()
    {
        var repo = new RepoFake();
        repo.Active.Add(new CheckIn
        {
            Id = 1, MemberUserId = "m1", Member = new ApplicationUser { FullName = "An Nguyen", PhoneNumber = "0901" },
            CheckinTime = Clock.GetUtcNow().UtcDateTime.AddMinutes(-45), CheckinMethod = CheckInMethod.FaceRecognition, Status = CheckInStatus.Active
        });
        repo.Active.Add(new CheckIn
        {
            Id = 2, MemberUserId = "m2", Member = new ApplicationUser { FullName = "Binh Tran", PhoneNumber = "0902" },
            CheckinTime = Clock.GetUtcNow().UtcDateTime.AddMinutes(-10), CheckinMethod = CheckInMethod.ManualStaff,
            HandledByUser = new ApplicationUser { FullName = "Staff A" }, Status = CheckInStatus.Active
        });
        repo.TodayCount = 7;
        var service = new ActiveCheckInService(repo, Clock, peakThreshold: 2);

        var all = (await service.GetActiveAsync(null)).Data!;
        var filtered = (await service.GetActiveAsync("binh")).Data!;

        Assert.Equal(2, all.CurrentOccupancy);
        Assert.Equal(7, all.CheckInsToday);
        Assert.Equal(45, all.Items[0].MinutesInside);
        Assert.Equal("Staff A", Assert.Single(filtered.Items).HandledByName);
        Assert.Equal(2, filtered.CurrentOccupancy);
        Assert.True(all.IsPeak);   // 2 inside, threshold 2
        Assert.False((await new ActiveCheckInService(repo, Clock, peakThreshold: 3).GetActiveAsync(null)).Data!.IsPeak);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ProtectorFake : IFaceTemplateProtector
    {
        public const byte Marker = 0xEE;
        public byte[] Protect(IReadOnlyList<float> descriptor) => [Marker, .. descriptor.SelectMany(BitConverter.GetBytes)];
        public float[] Unprotect(byte[] protectedTemplate) => [];
    }

    private sealed class RepoFake : ICheckInRepository
    {
        public FaceData? Face { get; private set; }
        public List<AuditLog> Audits { get; } = [];
        public List<CheckIn> Active { get; } = [];
        public int TodayCount { get; set; }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is FaceData face) Face = face;
            if (entity is AuditLog audit) Audits.Add(audit);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<FaceData?> GetFaceDataAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Face?.UserId == userId ? Face : null);
        public Task<IReadOnlyList<CheckIn>> GetActiveCheckInsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CheckIn>>(Active);
        public Task<int> CountCheckInsSinceAsync(DateTime utcFrom, CancellationToken cancellationToken = default) =>
            Task.FromResult(TodayCount);
    }
}
