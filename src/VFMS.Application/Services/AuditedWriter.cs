using VFMS.Application.Interfaces;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// Saves an entity together with its audit log entry (GBR-56, GBR-65) and optional notifications.
/// New entities are saved first inside a transaction so the audit row can reference the generated ID.
/// </summary>
internal static class AuditedWriter
{
    public static AuditLog Audit(
        string actorUserId, AuditActionType action, string entity, string? ipAddress,
        string? oldValue = null, string? newValue = null) =>
        new()
        {
            ActorUserId = actorUserId,
            ActionType = action,
            AuditEntity = entity,
            OldValue = oldValue,
            NewValue = newValue,
            OccurredAt = DateTime.UtcNow,
            IpAddress = ipAddress
        };

    public static Notification Notify(
        string recipientUserId, string? senderUserId, string title, string message, string entityType,
        NotificationType type = NotificationType.EquipmentIssueUpdate) =>
        new()
        {
            RecipientUserId = recipientUserId,
            SenderUserId = senderUserId,
            NotificationType = type.ToString(),
            Title = title,
            Message = message,
            NotifiedAt = DateTime.UtcNow,
            EntityType = entityType
        };

    public static Task SaveNewAsync(
        IAuditedRepository repository, BaseEntity entity, AuditLog audit,
        IReadOnlyCollection<Notification>? notifications, CancellationToken cancellationToken) =>
        repository.ExecuteInTransactionAsync(async () =>
        {
            repository.Add(entity);
            await repository.SaveChangesAsync(cancellationToken);
            await SaveChangedAsync(repository, entity, audit, notifications, cancellationToken);
        }, cancellationToken);

    public static async Task SaveChangedAsync(
        IAuditedRepository repository, BaseEntity entity, AuditLog audit,
        IReadOnlyCollection<Notification>? notifications, CancellationToken cancellationToken)
    {
        audit.EntityId = entity.Id;
        repository.Add(audit);
        foreach (var notification in notifications ?? [])
        {
            notification.EntityId = entity.Id;
            repository.Add(notification);
        }
        await repository.SaveChangesAsync(cancellationToken);
    }
}
