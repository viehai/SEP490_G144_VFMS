using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    private sealed record PTEmail(int Id, bool IsReceipt, string Recipient, string Subject, string Body, DateTime Claim);

    public async Task SendPendingEmail(IEmailService email, CancellationToken ct)
    {
        if (!email.IsConfigured)
            return;

        var claimed = await Write(async () =>
        {
            var now = Now;
            var claim = now.AddMinutes(5);
            var purchase = await store.Query<PTPurchase>()
                .Including(store, "Member", "Sessions", "Order.PaymentTransaction.Invoice")
                .OrderBy(p => p.Id).FirstOrDefaultAsync(store, p => p.OrderId != null &&
                    p.Order!.OrderStatus == OrderStatus.Paid && p.Order.PaymentTransaction!.GatewayName != DemoGateway &&
                    p.ReceiptEmailSentAt == null &&
                    (p.ReceiptEmailRetryAt == null || p.ReceiptEmailRetryAt <= now), ct);
            if (purchase is not null)
            {
                var invoice = purchase.Order!.PaymentTransaction!.Invoice!;
                var schedule = string.Join(Environment.NewLine, purchase.Sessions
                    .Where(s => s.SessionStatus != PTSessionStatus.Cancelled)
                    .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
                    .Select(s => $"{s.SessionDate:dd/MM/yyyy} {s.StartTime:HH:mm}–{s.EndTime:HH:mm}"));
                var body = $"Hóa đơn {invoice.InvoiceNumber}\nĐã thanh toán {purchase.PurchasePrice:N0} VND cho gói {purchase.PackageNameSnapshot}.\n{schedule}";
                purchase.ReceiptEmailRetryAt = claim;
                await store.Save(ct);
                return Result<PTEmail?>.Success(new(purchase.Id, true, purchase.Member.Email,
                    "Xác nhận thanh toán và lịch tập PT", body, claim));
            }

            var proposal = await store.Query<PTSession>().Including(store, "Member").OrderBy(s => s.Id)
                .FirstOrDefaultAsync(store, s => s.SessionStatus == PTSessionStatus.ReschedulePending &&
                    s.ProposalExpiresAt > now && s.ProposalEmailSentAt == null &&
                    (s.ProposalEmailRetryAt == null || s.ProposalEmailRetryAt <= now), ct);
            if (proposal is null)
                return Result<PTEmail?>.Success(null);
            proposal.ProposalEmailRetryAt = claim;
            await store.Save(ct);
            var message = $"Lịch đề xuất: {proposal.ProposedDate:dd/MM/yyyy} {proposal.ProposedStartTime:HH:mm}–{proposal.ProposedEndTime:HH:mm}. " +
                "Vào lịch PT để xác nhận hoặc từ chối.";
            return Result<PTEmail?>.Success(new(proposal.Id, false, proposal.Member.Email, "Đề xuất đổi lịch PT", message, claim));
        }, ct);

        if (claimed.Data is not { } delivery)
            return;
        await email.SendAsync(delivery.Recipient, delivery.Subject, delivery.Body, ct);
        var sentAt = Now;
        if (delivery.IsReceipt)
            await store.MarkReceiptEmailSent(delivery.Id, delivery.Claim, sentAt, ct);
        else
            await store.MarkProposalEmailSent(delivery.Id, delivery.Claim, sentAt, ct);
    }
}
