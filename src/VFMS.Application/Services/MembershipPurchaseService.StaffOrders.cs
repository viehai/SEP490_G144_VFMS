using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class MembershipPurchaseService
{
    public async Task<Result<PagedResponse<StaffPackageOrderResponse>>> ListStaffOrdersAsync(
        string staffId, StaffPackageOrderQuery query, CancellationToken ct)
    {
        var staff = (await repository.LoadCheckoutUsersAsync(staffId, [], ct)).SingleOrDefault();
        if (!IsActive(staff) || !HasRole(staff!, UserRole.Staff))
            return Result<PagedResponse<StaffPackageOrderResponse>>.Failure(
                "Cần quyền nhân viên lễ tân.", "STAFF_PACKAGE_ORDERS_FORBIDDEN", 403);

        if (query.From > query.To)
            return InvalidOrderFilter("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");

        var search = query.Search?.Trim();
        var withoutMethod = string.Equals(query.Method?.Trim(), StaffPackageOrderQuery.UnselectedMethod, StringComparison.OrdinalIgnoreCase);
        var withoutPayment = string.Equals(query.PaymentStatus?.Trim(), StaffPackageOrderQuery.UnrecordedPayment, StringComparison.OrdinalIgnoreCase);
        if (search is { Length: > 100 } || query.Page < 1 || query.PageSize is < 1 or > 100 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue ||
            !TryOrderFilter<OrderStatus>(query.Status, out var status) ||
            !TryOrderFilter<ComponentType>(query.ServiceType, out var serviceType) ||
            !TryOrderFilter<PaymentMethod>(withoutMethod ? null : query.Method, out var method) ||
            !TryOrderFilter<PaymentStatus>(withoutPayment ? null : query.PaymentStatus, out var paymentStatus))
            return InvalidOrderFilter("Bộ lọc đơn gói tập không hợp lệ.");

        var fromUtc = query.From is { } from && from > DateOnly.MinValue ? StartOfGymDayUtc(from) : (DateTime?)null;
        var beforeUtc = query.To is { } to && to < DateOnly.MaxValue ? StartOfGymDayUtc(to.AddDays(1)) : (DateTime?)null;
        var filter = new StaffPackageOrderFilter(string.IsNullOrEmpty(search) ? null : search, status, serviceType, method,
            paymentStatus, withoutMethod, withoutPayment, fromUtc, beforeUtc, query.Page, query.PageSize);
        var orders = await repository.ListStaffOrdersAsync(staff!.GymId, filter, clock.GetUtcNow().UtcDateTime, ct);
        return Result<PagedResponse<StaffPackageOrderResponse>>.Success(orders);
    }

    private static bool TryOrderFilter<T>(string? input, out T? value) where T : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (!Enum.TryParse<T>(input, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return false;
        value = parsed;
        return true;
    }

    private static DateTime StartOfGymDayUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - VietnamOffset;

    private static Result<PagedResponse<StaffPackageOrderResponse>> InvalidOrderFilter(string message) =>
        Result<PagedResponse<StaffPackageOrderResponse>>.Failure(message, "STAFF_PACKAGE_ORDER_FILTER_INVALID");
}
