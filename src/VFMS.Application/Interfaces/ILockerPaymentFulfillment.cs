using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface ILockerPaymentFulfillment
{
    Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct);
}
