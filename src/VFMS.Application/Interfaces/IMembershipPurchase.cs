using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IMembershipPurchaseService
{
    Task<Result<MembershipPurchaseResponse>> CreateAsync(string purchaserId, MembershipPurchaseRequest request, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> GetAsync(string purchaserId, string orderNumber, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> ConfirmCashAsync(
        string staffId, string orderNumber, decimal amount, string? ipAddress, CancellationToken ct);
    Task<Result<SepayWebhookResponse>> ConfirmSepayAsync(
        string authorization, string timestamp, string signature, byte[] rawBody, CancellationToken ct);
    Task ExpireOrdersAsync(CancellationToken ct);
    Task AdvanceMembershipsAsync(CancellationToken ct);
}

public interface IMembershipPaymentGateway
{
    VietQrPaymentResponse? CreateVietQr(string orderNumber, decimal amount);
    Result<SepayTransfer> Verify(string authorization, string timestamp, string signature, byte[] rawBody, DateTimeOffset now);
}

public sealed record SepayTransfer(string OrderNumber, string TransactionId, decimal Amount, DateTimeOffset TransferredAt);

public interface IMembershipPurchaseRepository
{
    Task<T> ExecuteInSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct);
    Task<List<Package>> LoadPlansAsync(int[] ids, CancellationToken ct);
    Task<List<ApplicationUser>> LoadCheckoutUsersAsync(string purchaserId, string[] emails, CancellationToken ct);
    Task<List<ApplicationUser>> LockUsersAsync(string[] userIds, CancellationToken ct);
    Task<ApplicationRole?> GetMemberRoleAsync(CancellationToken ct);
    Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct);
    Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct);
    Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct);
    Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct);
    Task<List<MemberMembership>> LoadDueMembershipsAsync(DateOnly today, CancellationToken ct);
    void Add(Order order);
    void Add(AuditLog audit);
    Task SaveChangesAsync(CancellationToken ct);
}
