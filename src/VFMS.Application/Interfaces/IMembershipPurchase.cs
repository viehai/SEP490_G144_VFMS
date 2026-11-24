using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IMembershipPurchaseService
{
    Task<Result<MembershipPurchaseResponse>> CreateAsync(string purchaserId, MembershipPurchaseRequest request, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> GetAsync(string purchaserId, string orderNumber, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> ApplyVoucherAsync(
        string purchaserId, string orderNumber, ApplyMembershipVoucherRequest request, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> RemoveVoucherAsync(string purchaserId, string orderNumber, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> CompleteNoChargeAsync(string purchaserId, string orderNumber, CancellationToken ct);
    Task<Result<IReadOnlyList<MembershipSummaryResponse>>> ListOwnedMembershipsAsync(string memberId, CancellationToken ct);
    Task<Result<MembershipDetailResponse>> GetOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct);
    Task<Result<MembershipDetailResponse>> ScheduleActivationAsync(
        string memberId, int membershipId, ScheduleMembershipActivationRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<MembershipRenewalOption>>> ListRenewalsAsync(string memberId, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> RenewAsync(string memberId, int membershipId, CancellationToken ct);
    Task<Result<MembershipPurchaseResponse>> ConfirmCashAsync(
        string staffId, string orderNumber, decimal amount, string? ipAddress, CancellationToken ct);
    Task<Result<PagedResponse<StaffPackageOrderResponse>>> ListStaffOrdersAsync(
        string staffId, StaffPackageOrderQuery query, CancellationToken ct);
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
    /// <summary>Whether any yoga class can be booked now (YogaClassRules.Bookable): yoga-only plans are sold only then.</summary>
    Task<bool> AnyBookableYogaClassAsync(DateTime nowUtc, CancellationToken ct);
    /// <summary>Whether a class a plan of this level covers can be booked now. The default ignores the level so existing test fakes still compile.</summary>
    Task<bool> AnyBookableYogaClassAsync(DateTime nowUtc, string maxLevel, CancellationToken ct) => AnyBookableYogaClassAsync(nowUtc, ct);
    Task<List<ApplicationUser>> LoadCheckoutUsersAsync(string purchaserId, string[] emails, CancellationToken ct);
    Task<List<ApplicationUser>> LockUsersAsync(string[] userIds, CancellationToken ct);
    Task<ApplicationRole?> GetMemberRoleAsync(CancellationToken ct);
    Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct);
    Task<PagedResponse<StaffPackageOrderResponse>> ListStaffOrdersAsync(int gymId, StaffPackageOrderFilter filter,
        DateTime nowUtc, CancellationToken ct);
    Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct);
    Task<Voucher?> FindVoucherForUpdateAsync(string normalizedCode, CancellationToken ct);
    Task<(int Confirmed, int Reserved)> CountVoucherCapacityAsync(int voucherId, CancellationToken ct);
    Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct);
    Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct);
    Task<List<OwnedMembership>> ListOwnedMembershipsAsync(string memberId, CancellationToken ct);
    Task<OwnedMembershipDetail?> FindOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct);
    Task<Gym?> GetRenewalPolicyGymAsync(CancellationToken ct);
    Task<List<MemberMembership>> LoadRenewableMembershipsAsync(string memberId, int? membershipId, CancellationToken ct);
    Task<List<Order>> LoadRenewalOrdersAsync(int[] membershipIds, bool forUpdate, CancellationToken ct);
    Task<string?> FindRenewalPurchaserAsync(string orderNumber, CancellationToken ct);
    Task<string[]> FindMembershipUsersDueAsync(DateOnly today, DateTime activationCutoffUtc, CancellationToken ct);
    Task<List<MemberMembership>> LoadDueMembershipsAsync(
        string[] userIds, DateOnly today, DateTime activationCutoffUtc, CancellationToken ct);
    Task<List<MemberMembership>> LoadOwnedOrderMembershipsAsync(string memberId, int membershipId, CancellationToken ct);
    void Add(Order order);
    void Add(AuditLog audit);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed record StaffPackageOrderFilter(
    string? Search, OrderStatus? Status, ComponentType? ServiceType, PaymentMethod? Method,
    PaymentStatus? PaymentStatus, bool WithoutMethod, bool WithoutPaymentStatus,
    DateTime? FromUtc, DateTime? BeforeUtc, int Page, int PageSize);
