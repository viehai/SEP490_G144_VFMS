namespace VFMS.Contracts.Users;

public sealed record UpdateUserRolesRequest(IReadOnlyCollection<string> Roles, IReadOnlyCollection<string>? PreviousRoles = null)
{
    public static IReadOnlyList<string> Assignable { get; } = Array.AsReadOnly<string>(["Staff", "Manager", "PT", "YogaInstructor", "Admin"]);
}
