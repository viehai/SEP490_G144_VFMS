using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;

namespace VFMS.Application.Services;

public sealed class MembershipInvoiceExportService(
    IMembershipTransactionService transactions, IMembershipInvoicePdfRenderer renderer) : IMembershipInvoiceExportService
{
    public async Task<Result<MembershipInvoicePdfFile>> ExportAsync(string purchaserId, int orderId, CancellationToken ct)
    {
        // Reuse UC-20's identity scope, owned query, financial mapping, and completion eligibility.
        var result = await transactions.GetAsync(purchaserId, orderId, ct);
        if (!result.IsSuccess)
            return Result<MembershipInvoicePdfFile>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);

        var transaction = result.Data!;
        if (!transaction.CanExportInvoicePdf || transaction.Invoice is null)
            return Result<MembershipInvoicePdfFile>.Failure(
                "Chỉ có thể tải hóa đơn đã phát hành của giao dịch đã thanh toán hoàn tất.", "INVOICE_EXPORT_UNAVAILABLE", 409);

        ct.ThrowIfCancellationRequested();
        var content = renderer.Render(transaction, ct);
        var safeNumber = new string(transaction.Invoice.InvoiceNumber
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .Take(80).ToArray()).Trim('-');
        if (safeNumber.Length == 0)
            safeNumber = $"Order-{orderId}";
        return Result<MembershipInvoicePdfFile>.Success(new(content, $"VFMS-Invoice-{safeNumber}.pdf"));
    }
}
