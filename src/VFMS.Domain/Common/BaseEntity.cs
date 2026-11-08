namespace VFMS.Domain.Common;

/// <summary>
/// Base class for domain entities with audit timestamps and a soft-delete flag.
/// Use soft deletion by default and preserve records protected by GBR-33.
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
    /// Soft-delete flag. Global query filters exclude these records from normal reads.
    /// </summary>
    public bool IsDeleted { get; set; } = false;
}

