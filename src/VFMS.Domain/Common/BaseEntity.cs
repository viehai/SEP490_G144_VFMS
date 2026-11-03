namespace VFMS.Domain.Common;

/// <summary>
/// Base class for all domain entities. Enforces soft-delete (GBR-33) and audit timestamps.
/// Every entity must inherit from this class; hard-deletion is strictly forbidden.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Primary key – auto-incremented identity.</summary>
    public int Id { get; set; }

    /// <summary>UTC timestamp of record creation.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft-delete flag (GBR-33). Never set to <c>true</c> via a hard DELETE statement.
    /// All queries must filter on <c>IsDeleted == false</c> by default (global query filter).
    /// </summary>
    public bool IsDeleted { get; set; } = false;
}

