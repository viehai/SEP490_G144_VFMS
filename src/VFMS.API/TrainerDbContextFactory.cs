using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VFMS.Infrastructure.Data;

namespace VFMS.API;

// EF tooling must not start Program.cs, migrate or seed accounts while scaffolding a migration.
public sealed class TrainerDbContextFactory : IDesignTimeDbContextFactory<VfmsDbContext>
{
    public VfmsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=VfmsDb;Integrated Security=true;TrustServerCertificate=true";
        return new VfmsDbContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlServer(connection).Options);
    }
}
