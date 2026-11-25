namespace VFMS.Contracts.Memberships;

public enum MembershipDurationUnit
{
    Day = 0,
    Month = 1
}

public static class MembershipFreezeDuration
{
    public const int DaysPerMonth = 30;

    public static bool TryToDays(int value, MembershipDurationUnit unit, out int days)
    {
        days = 0;
        if (value < 0 || !Enum.IsDefined(unit) ||
            (unit == MembershipDurationUnit.Month && value > int.MaxValue / DaysPerMonth))
            return false;

        days = unit == MembershipDurationUnit.Month ? value * DaysPerMonth : value;
        return true;
    }
}
