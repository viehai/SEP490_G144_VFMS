using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.IntegrationTests;

public sealed partial class LegacySchemaMigrationTests
{
    private const string BeforeUserIdentity = "20261121132212_AddTrainerInactivityAndDisputeReviewer";
    private const string UserIdentity = "20261121210000_EnforceApplicationUserIdentity";

    [SqlServerMigrationFact]
    public async Task ApplicationUserIdentityAppliedBeforeIncomingMigrationsStillUpgrades()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await using var context = database.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261118155730_ConsolidateMySqlSchemaChanges");

        // Reproduce a developer database that applied UC-06 before merging the intervening main migrations.
        var script = migrator.GenerateScript(BeforeUserIdentity, UserIdentity);
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var batch in Regex.Split(script, @"^[ \t]*GO[ \t]*\r?$", RegexOptions.Multiline))
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }

        Assert.Contains(UserIdentity, await context.Database.GetAppliedMigrationsAsync());
        Assert.Contains(BeforeUserIdentity, await context.Database.GetPendingMigrationsAsync());

        const string userId = "out-of-order-identity";
        context.ApplicationUsers.Add(new ApplicationUser
        {
            UserId = userId,
            Gym = new Gym { GymName = "Out-of-order migration fixture" },
            FullName = "Migration test",
            Email = "out-of-order-identity@example.test",
            PhoneNumber = "0900000103",
            PasswordHash = "test-only",
            AccountStatus = AccountStatus.Locked,
            AccountStatusBeforeLock = AccountStatus.Active
        });
        await context.SaveChangesAsync();

        await migrator.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var account = await context.ApplicationUsers.AsNoTracking().SingleAsync(user => user.UserId == userId);
        Assert.Equal(AccountStatus.Active, account.AccountStatusBeforeLock);
        Assert.Equal("0900000103", account.PhoneNumber);
        Assert.Equal(2, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.ApplicationUsers')
              AND name IN (N'IX_ApplicationUsers_Email', N'IX_ApplicationUsers_PhoneNumber')
              AND is_unique = 1 AND has_filter = 1
            """));
    }

    [SqlServerMigrationFact]
    public async Task ApplicationUserIdentityMigrationGuardsDuplicatesAndSupportsReconciledUpgradeAndDowngrade()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await using var context = database.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeUserIdentity);
        var gym = new Gym { GymName = "Identity migration fixture" };
        context.Add(gym);
        await context.SaveChangesAsync();

        await InsertUser("identity-a", " ALPHA@example.test ", "(0912) 345-678");
        await InsertUser("identity-b", "beta@example.test", "0912345678");

        var conflict = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync());
        Assert.Equal(51023, conflict.Number);
        Assert.DoesNotContain(UserIdentity, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(" ALPHA@example.test ", await database.ScalarAsync<string>(
            "SELECT Email FROM dbo.ApplicationUsers WHERE UserId = 'identity-a'"));

        await database.ExecuteAsync("UPDATE dbo.ApplicationUsers SET PhoneNumber = '0912345679' WHERE UserId = 'identity-b'");
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        var canonical = await context.ApplicationUsers.AsNoTracking().SingleAsync(user => user.UserId == "identity-a");
        Assert.Equal("alpha@example.test", canonical.Email);
        Assert.Equal("0912345678", canonical.PhoneNumber);
        Assert.Null(canonical.AccountStatusBeforeLock);

        var duplicateEmail = await Assert.ThrowsAsync<SqlException>(() => InsertUser("email-duplicate", "ALPHA@example.test", "0912555678"));
        Assert.Equal(2601, duplicateEmail.Number);
        var duplicatePhone = await Assert.ThrowsAsync<SqlException>(() => InsertUser("phone-duplicate", "other@example.test", "0912345678"));
        Assert.Equal(2601, duplicatePhone.Number);
        var formattedPhone = await Assert.ThrowsAsync<SqlException>(() => InsertUser("raw-formatted", "formatted@example.test", "0912-345-678"));
        Assert.Equal(547, formattedPhone.Number);

        await database.ExecuteAsync("UPDATE dbo.ApplicationUsers SET IsDeleted = 1 WHERE UserId = 'identity-a'");
        await InsertUser("identity-reuse", "alpha@example.test", "0912345678");
        Assert.Equal(2, await context.ApplicationUsers.CountAsync());
        Assert.Equal(3, await context.ApplicationUsers.IgnoreQueryFilters().CountAsync());

        var blockedDowngrade = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(BeforeUserIdentity));
        Assert.Equal(51024, blockedDowngrade.Number);
        await database.ExecuteAsync("UPDATE dbo.ApplicationUsers SET Email = 'archived@example.test' WHERE UserId = 'identity-a'");
        await migrator.MigrateAsync(BeforeUserIdentity);
        Assert.DoesNotContain(UserIdentity, await context.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        Assert.Contains(UserIdentity, await context.Database.GetAppliedMigrationsAsync());

        Task InsertUser(string id, string email, string phone) => database.ExecuteAsync("""
            INSERT INTO dbo.ApplicationUsers
                (UserId, GymId, Id, FullName, Email, PhoneNumber, PasswordHash, AccountStatus, IsBanned, IsDeleted, CreatedAt, UpdatedAt)
            VALUES (@id, @gymId, 0, N'Synthetic Identity User', @email, @phone, N'test-only', N'Active', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
            """, new SqlParameter("@id", id), new SqlParameter("@gymId", gym.Id), new SqlParameter("@email", email), new SqlParameter("@phone", phone));
    }
}
