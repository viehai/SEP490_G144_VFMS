using VFMS.Contracts.Yoga;
using VFMS.Contracts.Yoga.Validators;

namespace VFMS.Client.Components.Memberships;

/// <summary>What a timetable card offers a visitor for a class they have not booked yet.</summary>
public enum YogaCardAction
{
    /// <summary>Their yoga plan covers the class: "Đặt chỗ" / "Vào danh sách chờ".</summary>
    Book,
    /// <summary>They have a yoga plan, but of a lower level: note plus "Mua gói".</summary>
    Upgrade,
    /// <summary>Not signed in, or no usable yoga plan: "Mua gói và đăng ký".</summary>
    BuyAndBook,
}

public static class YogaPlanAccess
{
    /// <summary>Booking needs the Member role and a usable yoga plan whose level covers the class.</summary>
    public static YogaCardAction For(bool isMember, YogaMyPlanResponse? plan, string classLevel) =>
        plan is not { HasPlan: true } || !isMember ? YogaCardAction.BuyAndBook
        : YogaRules.PlanCovers(plan.MaxLevel, classLevel) ? YogaCardAction.Book
        : YogaCardAction.Upgrade;
}
