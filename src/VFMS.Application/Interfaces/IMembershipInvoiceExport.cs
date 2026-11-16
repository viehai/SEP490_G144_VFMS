using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Application.Interfaces;

public interface IMembershipInvoiceExportService
{
    Task<Result<MembershipInvoicePdfFile>> ExportAsync(string purchaserId, int orderId, CancellationToken ct);
}

public interface IMembershipInvoicePdfRenderer
{
    byte[] Render(MembershipTransactionDetailResponse transaction, CancellationToken ct);
}

public sealed record MembershipInvoicePdfFile(byte[] Content, string FileName);
