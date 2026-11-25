using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Consultations;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class ConsultationPersistenceTests : IAsyncLifetime
{
    private const string CustomerUserId = "consultation-customer";
    private const string ManagerUserId = "consultation-manager";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Consultation test gym" };
        var customer = new ApplicationUser
        {
            UserId = CustomerUserId,
            Gym = gym,
            Email = "customer@example.com",
            PhoneNumber = "0901000001",
            FullName = "Nguyen Van A",
            PasswordHash = "test"
        };
        var manager = new ApplicationUser
        {
            UserId = ManagerUserId,
            Gym = gym,
            Email = "manager@example.com",
            PhoneNumber = "0901000002",
            FullName = "Manager",
            PasswordHash = "test"
        };
        db.ConsultationConversations.Add(new()
        {
            CustomerUserId = CustomerUserId,
            CustomerUser = customer,
            Subject = "Tu van",
            Messages =
            [
                new ConsultationMessage
                {
                    SenderUserId = CustomerUserId,
                    SenderUser = customer,
                    Content = "Can tu van",
                    IsRead = false
                }
            ]
        });
        db.ApplicationUsers.Add(manager);
        await db.SaveChangesAsync();

        customer.IsDeleted = true;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ManagerCanReplyAndCustomerNameRemainsAvailableForSoftDeletedAccount()
    {
        await using var db = CreateContext();
        var service = new ConsultationService(new ConsultationRepository(db), new AllowedBannedWordService());

        var result = await service.SendAsync(
            1, new ConsultationSendRequest("Da gui loi nhan"), ManagerUserId, isManager: true);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.Equal("Nguyen Van A", result.Data.CustomerName);
        Assert.Equal(2, result.Data.Messages.Count);
        Assert.Contains(result.Data.Messages, message =>
            message.SenderUserId == ManagerUserId && message.Content == "Da gui loi nhan");
    }

    private SqliteTestContext CreateContext() => new(_options);

    private sealed class AllowedBannedWordService : IBannedWordService
    {
        public Task<IReadOnlyList<string>> FindBannedWordsAsync(
            string? text, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<Result<PagedResponse<BannedWordResponse>>> GetWordsAsync(
            BannedWordListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<BannedWordResponse>> GetWordAsync(
            int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<BannedWordResponse>> CreateAsync(
            BannedWordRequest request, string actorUserId, string? ipAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<BannedWordResponse>> UpdateAsync(
            int id, BannedWordRequest request, string actorUserId, string? ipAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<BannedWordResponse>> SetActiveAsync(
            int id, bool isActive, string actorUserId, string? ipAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
