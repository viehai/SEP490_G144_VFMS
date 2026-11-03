using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Master Data: Loại hình lớp Yoga (Hatha, Vinyasa, Ashtanga, Yin, Power, ...) được cấu hình linh hoạt.
/// Bảng thực thể tương ứng với YogaClassType trên sơ đồ ERD.
/// </summary>
public class YogaClassType : BaseEntity
{
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<YogaClass> YogaClasses { get; set; } = [];
    public ICollection<PackageComponent> PackageComponents { get; set; } = [];
    public ICollection<YogaWaitlist> YogaWaitlists { get; set; } = [];
}

