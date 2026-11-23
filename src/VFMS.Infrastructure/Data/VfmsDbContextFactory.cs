using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VFMS.Infrastructure.Data;

// EF tooling must not start Program.cs, migrate or seed accounts while scaffolding a migration.
public sealed class VfmsDbContextFactory : IDesignTimeDbContextFactory<VfmsDbContext>
{
    public VfmsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=VfmsDb;Integrated Security=true;TrustServerCertificate=true";
        return new VfmsDbContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlServer(connection).Options);
    }
}
