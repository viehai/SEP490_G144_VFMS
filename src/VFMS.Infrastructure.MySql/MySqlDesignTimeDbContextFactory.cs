using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.MySql;

/// <summary>
/// Design-time factory used by the EF Core tools to scaffold the MySQL migration chain.
/// <para>
/// The MySQL chain lives in its own assembly so that the original SQL Server migration files
/// in VFMS.Infrastructure/Migrations are never modified. Each chain keeps its own
/// model snapshot and its own migrations history table, so both can coexist in the repository.
/// </para>
/// </summary>
public sealed class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<VfmsDbContext>
{
    internal const string ConnectionString =
        "Server=localhost;Port=3306;Database=VfmsDb;User Id=vfms;Password=Vfms@2026;" +
        "TreatTinyAsBoolean=true;AllowUserVariables=true;UseAffectedRows=false;" +
        "Default Command Timeout=300;Connection Timeout=60;CharSet=utf8mb4";

    public VfmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VfmsMySqlDbContext>()
            .UseMySql(
                ConnectionString,
                new MySqlServerVersion(new Version(8, 0, 39)),
                mysqlOptions =>
                {
                    mysqlOptions.MigrationsAssembly(typeof(MySqlDesignTimeDbContextFactory).Assembly.FullName);
                    mysqlOptions.MigrationsHistoryTable("__EFMigrationsHistoryMySql");
                })
            .Options;

        return new VfmsMySqlDbContext(options);
    }
}
