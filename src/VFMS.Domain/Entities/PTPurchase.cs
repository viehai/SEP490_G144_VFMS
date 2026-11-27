using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A PT package purchase contract between a member and a personal trainer.
/// Purchasing PT and recording PT attendance do not require a Gym membership.
/// </summary>
public class PTPurchase : BaseEntity
{
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public PTPurchaseStatus Status { get; set; } = PTPurchaseStatus.Active;
    public TrainerLevel? TrainerLevelSnapshot { get; set; }
    public int FreezeMinimumDaysSnapshot { get; set; }
    public int FreezeMaxTotalDaysSnapshot { get; set; }
    public string? WeeklyPatternJson { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public int? MemberMembershipId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public string PTUserId { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    [NotMapped]
    public OrderItem PurchasedItem => Order?.Items.SingleOrDefault()
        ?? throw new InvalidOperationException($"PT purchase {Id} requires one purchased order item.");

    [NotMapped]
    public string PackageNameSnapshot => PurchasedItem.PackageNameSnapshot;

    [NotMapped]
    public int TotalSessions => ReadPurchasedSessionCount(PurchasedItem);

    [NotMapped]
    public decimal PurchasePrice => Order?.PaymentTransaction?.AmountDue
        ?? PurchasedItem.UnitPriceSnapshot - (Order?.VoucherRedemption?.DiscountAmount ?? 0);

    public static int ReadPurchasedSessionCount(OrderItem item)
    {
        var terms = item.ComponentTermsSnapshot is { } snapshot
            ? JsonSerializer.Deserialize<PurchasedComponent[]>(snapshot)
            : null;
        var sessionCount = terms?.SingleOrDefault(component => component.Type == ComponentType.PT)?.SessionCount;
        return sessionCount is >= 0
            ? sessionCount.Value
            : throw new InvalidOperationException($"PT order item {item.Id} requires purchased PT session terms.");
    }

    private sealed record PurchasedComponent(ComponentType Type, int? SessionCount);

    // ── Navigation Properties ──────────────────────────────────
    public MemberMembership? MemberMembership { get; set; }
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser PTUser { get; set; } = null!;
    public ICollection<PTSession> Sessions { get; set; } = [];
    public ICollection<PTFreeze> PTFreezes { get; set; } = [];
}

