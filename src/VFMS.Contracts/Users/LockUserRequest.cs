namespace VFMS.Contracts.Users;

public sealed record LockUserRequest(string Reason)
{
    public const int MaxReasonLength = 500;
}
