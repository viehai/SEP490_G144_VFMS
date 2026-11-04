namespace VFMS.Contracts.SystemAdministration;

/// <summary>
/// Audit log filters (UC-87). Dates are UTC. A <see cref="To"/> value with no time part
/// includes the whole day.
/// </summary>
public sealed record AuditLogListRequest(
    DateTime? From = null,
    DateTime? To = null,
    string? ActorUserId = null,
    string? ActionType = null,
    string? Entity = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record AuditLogResponse(
    int Id,
    DateTime OccurredAt,
    string? ActorUserId,
    string? ActorName,
    string ActionType,
    string AuditEntity,
    int? EntityId,
    string? OldValue,
    string? NewValue,
    string? IpAddress
);
