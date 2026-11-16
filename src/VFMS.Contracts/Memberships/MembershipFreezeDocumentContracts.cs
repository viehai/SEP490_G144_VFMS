namespace VFMS.Contracts.Memberships;

public sealed record MembershipFreezeDocumentResponse(Guid DocumentId, string FileName, string ContentType, long Size);

public static class MembershipFreezeDocumentLimits
{
    // Technical upload limit, independent of membership freeze business policy.
    public const int MaximumBytes = 5 * 1024 * 1024;
}
