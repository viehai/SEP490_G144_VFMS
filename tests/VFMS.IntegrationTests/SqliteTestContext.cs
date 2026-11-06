using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

/// <summary>The real VFMS model on SQLite for relational tests.</summary>
internal sealed class SqliteTestContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Only this SQL Server-specific binary column needs a provider mapping in relational tests.
        modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
    }
}
