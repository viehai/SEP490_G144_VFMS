using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Migrations;

namespace VFMS.IntegrationTests;

/// <summary>
/// SQL generation/model checks always run. Database cases require VFMS_TEST_SQLSERVER_CONNECTION
/// and create disposable VFMS_MigrationTest_* databases; the configured application database is never used.
/// </summary>
public sealed partial class LegacySchemaMigrationTests
{
    private const string BeforeLifecycle = "20261112125236_AddMembershipFreezeDecisions";
    private const string BeforeReconciliation = "20261113074717_AddVoucherTransactionSnapshots";
    private const string LegacyHistory = "20261113020305_CompleteMembershipFreezeLifecycle";
    private const string Lifecycle = "20261113032905_CompleteMembershipFreezeLifecycle";
    private static readonly Guid DocumentId = Guid.Parse("861ba8b8-a5f6-4728-a90f-415c9863570a");
    private static readonly byte[] DocumentBytes = Encoding.UTF8.GetBytes("%PDF-1.4\nlegacy private medical evidence\n%%EOF");

    [Fact]
    public void LifecycleUpgradeUsesMetadataGuardsForExistingColumnsAndIndexes()
    {
        var migration = new CompleteMembershipFreezeLifecycle { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        Assert.DoesNotContain(migration.UpOperations.OfType<AddColumnOperation>(),
            operation => operation.Table == "MembershipFreezes" && operation.Name == "SupportingDocumentId");
        Assert.DoesNotContain(migration.UpOperations.OfType<CreateIndexOperation>(),
            operation => operation.Name.StartsWith("IX_MembershipFreezes_RequestStatus_", StringComparison.Ordinal));

        using var context = ModelContext();
        var sql = context.GetService<IMigrator>().GenerateScript(BeforeLifecycle, Lifecycle);
        Assert.Contains("COL_LENGTH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SupportingDocumentId", sql, StringComparison.Ordinal);
        Assert.Contains("uniqueidentifier", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sys.indexes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id", sql, StringComparison.Ordinal);
        Assert.Contains("IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationIsGeneratedFromActualMigrationsAndCurrentModelHasNoLegacyConcepts()
    {
        using var context = ModelContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations, entry => entry.Key.EndsWith("_ReconcileLegacyMembershipSchema", StringComparison.Ordinal));
        Assert.NotEmpty(assembly.CreateMigration(metadata.Value, context.Database.ProviderName!).UpOperations);
        var sql = context.GetService<IMigrator>().GenerateScript(BeforeReconciliation);
        Assert.Contains("MembershipFreezeDocuments", sql, StringComparison.Ordinal);
        Assert.Contains("SupportingDocumentContent", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccessLevel", sql, StringComparison.Ordinal);
        Assert.Contains("IncludedFeatures", sql, StringComparison.Ordinal);
        Assert.Contains("THROW", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(context.Model.GetEntityTypes(), type => type.GetTableName() == "MembershipFreezeDocuments");
        var package = context.Model.FindEntityType(typeof(Package))!;
        Assert.Null(package.FindProperty("AccessLevel"));
        Assert.Null(package.FindProperty("IncludedFeatures"));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [SqlServerMigrationFact]
    public async Task FreshSqlServerDatabaseAppliesWholeChainAndRepeatedMigrationDoesNothing()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await using var context = database.Context();
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(context.Database.GetMigrations().ToArray(), applied);
        await AssertCurrentSchemaAsync(database);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [SqlServerMigrationFact]
    public async Task ExistingLegacyDatabaseUpgradesWithoutDuplicateColumnsAndPreservesPrivateAttachmentsAndHistory()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        var original = await SeedLegacyDatabaseAsync(database);
        await using var context = database.Context();
        Assert.Contains(LegacyHistory, await context.Database.GetAppliedMigrationsAsync());
        Assert.DoesNotContain(Lifecycle, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.MembershipFreezes') AND name=N'SupportingDocumentId'"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.MembershipFreezes') AND name=N'SupportingDocumentContent'"));

        await context.Database.MigrateAsync();

        await AssertCurrentSchemaAsync(database);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var freeze = await context.MembershipFreezes.AsNoTracking().SingleAsync();
        Assert.Equal(original.FreezeId, freeze.Id);
        Assert.Equal(DocumentId, freeze.SupportingDocumentId);
        Assert.Equal("legacy-certificate.pdf", freeze.SupportingDocumentFileName);
        Assert.Equal("application/pdf", freeze.SupportingDocumentContentType);
        Assert.Equal(DocumentBytes, freeze.SupportingDocumentContent);
        Assert.Equal(FreezeRequestStatus.Approved, freeze.RequestStatus);
        Assert.Equal("Approved before upgrade", freeze.ReviewNote);
        Assert.Equal("migration-member", freeze.ReviewedByUserId);
        Assert.Equal(7, freeze.AppliedExpiryExtensionDays);
        Assert.Equal("Policy captured before upgrade", freeze.AppliedPolicySnapshot);
        var membership = await context.MemberMemberships.AsNoTracking().SingleAsync();
        Assert.Equal(7, membership.TotalFrozenDays);
        Assert.Equal(1, membership.FreezeCount);
        Assert.Equal(original.MembershipEnd, membership.EndDate);
        var payment = await context.PaymentTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(500000m, payment.AmountDue);
        Assert.Equal(500000m, payment.AmountReceived);
        Assert.Equal(PaymentStatus.Paid, payment.PaymentStatus);
        Assert.Equal("Legacy order snapshot", (await context.OrderItems.AsNoTracking().SingleAsync()).PackageNameSnapshot);
        Assert.Equal("Existing audit must remain", (await context.AuditLogs.AsNoTracking().SingleAsync()).NewValue);
        Assert.Equal(InvoiceStatus.Issued, (await context.Invoices.AsNoTracking().SingleAsync()).InvoiceStatus);

        var count = (await context.Database.GetAppliedMigrationsAsync()).Count();
        await context.Database.MigrateAsync();
        Assert.Equal(count, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(DocumentBytes, (await context.MembershipFreezes.AsNoTracking().SingleAsync()).SupportingDocumentContent);
    }

    [SqlServerMigrationFact]
    public async Task ConflictingInlineAttachmentStopsCleanupWithoutDeletingEitherPayload()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await SeedLegacyDatabaseAsync(database);
        await using var context = database.Context();
        await context.GetService<IMigrator>().MigrateAsync(BeforeReconciliation);
        var conflicting = Encoding.UTF8.GetBytes("%PDF-1.4\ndifferent inline evidence");
        await database.ExecuteAsync("""
            UPDATE dbo.MembershipFreezes SET SupportingDocumentFileName=N'conflicting.pdf',
                SupportingDocumentContentType=N'application/pdf', SupportingDocumentContent=@content;
            """, new SqlParameter("@content", conflicting));
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());
        Assert.Equal(51002, error.Number);
        Assert.Contains("conflicting", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DocumentBytes, await database.ScalarAsync<byte[]>("SELECT Content FROM dbo.MembershipFreezeDocuments"));
        Assert.Equal(conflicting, await database.ScalarAsync<byte[]>("SELECT SupportingDocumentContent FROM dbo.MembershipFreezes"));
        await AssertLegacyArtifactsRemainAsync(database);
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());
    }

    [SqlServerMigrationFact]
    public async Task UnreferencedLegacyDocumentStopsCleanupInsteadOfDiscardingPrivateData()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await SeedLegacyDatabaseAsync(database);
        await database.ExecuteAsync("""
            INSERT INTO dbo.MembershipFreezeDocuments(Id,FileName,ContentType,Size,Content,CreatedAt)
            VALUES(NEWID(),N'orphan.pdf',N'application/pdf',@size,@content,SYSUTCDATETIME());
            """, new SqlParameter("@size", DocumentBytes.LongLength), new SqlParameter("@content", DocumentBytes));
        await using var context = database.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());
        Assert.Equal(51001, error.Number);
        Assert.Contains("unreferenced", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.MembershipFreezeDocuments"));
        await AssertLegacyArtifactsRemainAsync(database);
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());
    }

    private static VfmsDbContext ModelContext() => new(new DbContextOptionsBuilder<VfmsDbContext>()
        .UseSqlServer("Server=localhost;Database=VFMS_MigrationModelOnly;Integrated Security=True;TrustServerCertificate=True").Options);

    private static async Task AssertCurrentSchemaAsync(DisposableMigrationDatabase database)
    {
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name=N'MembershipFreezeDocuments'"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Packages') AND name IN(N'AccessLevel',N'IncludedFeatures')"));
        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.MembershipFreezes') AND name IN(N'SupportingDocumentId',N'SupportingDocumentFileName',N'SupportingDocumentContentType',N'SupportingDocumentContent')"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MembershipFreezes') AND name IN(N'IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id',N'IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id')"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'dbo.MembershipFreezeDocuments')"));
    }

    private static async Task AssertLegacyArtifactsRemainAsync(DisposableMigrationDatabase database)
    {
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name=N'MembershipFreezeDocuments'"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Packages') AND name IN(N'AccessLevel',N'IncludedFeatures')"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'dbo.MembershipFreezeDocuments')"));
    }
}
