using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.MySql;

/// <summary>
/// MySQL flavour of <see cref="VfmsDbContext"/>.
/// <para>
/// The shared context describes the model in a provider-neutral way; the few places where the
/// SQL Server and MySQL dialects genuinely differ are overridden here so that
/// VFMS.Infrastructure/Data/VfmsDbContext.cs stays identical to the original and the original
/// SQL Server migration files in VFMS.Infrastructure/Migrations never need to change.
/// </para>
/// </summary>
public sealed class VfmsMySqlDbContext : VfmsDbContext
{
    public VfmsMySqlDbContext(DbContextOptions<VfmsMySqlDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQL Server stores the face embedding as VARBINARY(MAX); MySQL's equivalent is LONGBLOB.
        modelBuilder.Entity<FaceData>()
            .Property(e => e.FaceNetEmbedding)
            .HasColumnType("LONGBLOB");
    }
}
