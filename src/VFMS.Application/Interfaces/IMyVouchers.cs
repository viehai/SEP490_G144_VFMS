using VFMS.Contracts.Common;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public sealed record WalletVoucher(Voucher Definition, VoucherCapacity Capacity,
    IReadOnlyList<VoucherAwardResponse> Awards, IReadOnlyList<VoucherUseResponse> Uses);

public sealed record VoucherWalletReadModel(DateTime AccountCreatedAt, IReadOnlyList<WalletVoucher> Vouchers);

public interface IMyVoucherRepository
{
    Task<VoucherWalletReadModel?> ReadAsync(string userId, DateTime now, CancellationToken ct);
    Task<decimal?> GetCheckoutSubtotalAsync(string userId, string orderNumber, DateTime now, CancellationToken ct);
}

public interface IMyVoucherService
{
    Task<Result<IReadOnlyList<MyVoucherResponse>>> ListAsync(string userId, string? orderNumber, CancellationToken ct);
}
