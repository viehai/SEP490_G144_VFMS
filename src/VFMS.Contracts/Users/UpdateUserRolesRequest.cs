namespace VFMS.Contracts.Users;

public sealed record UpdateUserRolesRequest(IReadOnlyCollection<string> Roles);
