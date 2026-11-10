using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.PersonalTraining;

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
            var purchase = await db.PTPurchases.Include(p => p.Member).Include(p => p.Sessions)
                .Include(p => p.Order).ThenInclude(o => o!.PaymentTransaction).ThenInclude(p => p!.Invoice)
                .AsSplitQuery().OrderBy(p => p.Id).FirstOrDefaultAsync(p => p.OrderId != null &&
                    p.Order!.OrderStatus == OrderStatus.Paid && p.ReceiptEmailSentAt == null &&
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
                await db.SaveChangesAsync(ct);
                return Result<PTEmail?>.Success(new(purchase.Id, true, purchase.Member.Email,
                    "Xác nhận thanh toán và lịch tập PT", body, claim));
            }

            var proposal = await db.PTSessions.Include(s => s.Member).OrderBy(s => s.Id)
                .FirstOrDefaultAsync(s => s.SessionStatus == PTSessionStatus.ReschedulePending &&
                    s.ProposalExpiresAt > now && s.ProposalEmailSentAt == null &&
                    (s.ProposalEmailRetryAt == null || s.ProposalEmailRetryAt <= now), ct);
            if (proposal is null)
                return Result<PTEmail?>.Success(null);
            proposal.ProposalEmailRetryAt = claim;
            await db.SaveChangesAsync(ct);
            var message = $"Lịch đề xuất: {proposal.ProposedDate:dd/MM/yyyy} {proposal.ProposedStartTime:HH:mm}–{proposal.ProposedEndTime:HH:mm}. " +
                "Vào lịch PT để xác nhận hoặc từ chối. Hủy trước ít nhất 4 giờ để giữ credit.";
            return Result<PTEmail?>.Success(new(proposal.Id, false, proposal.Member.Email, "Đề xuất đổi lịch PT", message, claim));
        }, ct);

        if (claimed.Data is not { } delivery)
            return;
        await email.SendAsync(delivery.Recipient, delivery.Subject, delivery.Body, ct);
        var sentAt = Now;
        if (delivery.IsReceipt)
            await db.PTPurchases.Where(p => p.Id == delivery.Id && p.ReceiptEmailRetryAt == delivery.Claim)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.ReceiptEmailSentAt, sentAt), ct);
        else
            await db.PTSessions.Where(s => s.Id == delivery.Id && s.ProposalEmailRetryAt == delivery.Claim)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ProposalEmailSentAt, sentAt), ct);
    }
}
