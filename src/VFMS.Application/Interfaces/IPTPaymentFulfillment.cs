using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IPTPaymentFulfillment
{
    // Null means the order belongs to the existing membership workflow.
    Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct);
}
