using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class LegacySchemaMigrationTests
{
    private static async Task<(int FreezeId, DateOnly MembershipEnd)> SeedLegacyDatabaseAsync(DisposableMigrationDatabase database)
    {
        await using (var context = database.Context())
            await context.GetService<IMigrator>().MigrateAsync(BeforeLifecycle);
        await database.ExecuteAsync("""
            CREATE TABLE dbo.MembershipFreezeDocuments
            (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_MembershipFreezeDocuments PRIMARY KEY,
                FileName nvarchar(256) NOT NULL,
                ContentType nvarchar(100) NOT NULL,
                Size bigint NOT NULL,
                Content varbinary(max) NOT NULL,
                CreatedAt datetime2 NOT NULL
            );
            ALTER TABLE dbo.MembershipFreezes ADD SupportingDocumentId uniqueidentifier NULL;
            """);
        // Compile FK/index DDL only after the legacy Guid column exists in SQL Server metadata.
        await database.ExecuteAsync("""
            ALTER TABLE dbo.MembershipFreezes ADD CONSTRAINT FK_MembershipFreezes_MembershipFreezeDocuments_SupportingDocumentId
                FOREIGN KEY (SupportingDocumentId) REFERENCES dbo.MembershipFreezeDocuments(Id);
            CREATE UNIQUE INDEX IX_MembershipFreezes_SupportingDocumentId
                ON dbo.MembershipFreezes(SupportingDocumentId) WHERE SupportingDocumentId IS NOT NULL;
            CREATE INDEX IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id
                ON dbo.MembershipFreezes(RequestStatus,RequestedEndDate,Id);
            CREATE INDEX IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id
                ON dbo.MembershipFreezes(RequestStatus,RequestedStartDate,Id);
            ALTER TABLE dbo.Packages ADD AccessLevel nvarchar(100) NULL, IncludedFeatures nvarchar(max) NULL;
            INSERT INTO dbo.__EFMigrationsHistory(MigrationId,ProductVersion)
                VALUES(N'20261113020305_CompleteMembershipFreezeLifecycle',N'8.0.10');
            """);
        await database.ExecuteAsync("""
            INSERT INTO dbo.MembershipFreezeDocuments(Id,FileName,ContentType,Size,Content,CreatedAt)
            VALUES(@id,N'legacy-certificate.pdf',N'application/pdf',@size,@content,'2026-11-12T08:00:00');
            """, new SqlParameter("@id", DocumentId), new SqlParameter("@size", DocumentBytes.LongLength),
            new SqlParameter("@content", DocumentBytes));

        await using var seed = new LegacySeedContext(new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        var gym = new Gym { GymName = "Migration regression gym" };
        var user = new ApplicationUser
        {
            UserId = "migration-member", Gym = gym, FullName = "Migration member",
            Email = "migration-member@example.test", PasswordHash = "test-only", AccountStatus = AccountStatus.Active
        };
        var package = new Package
        {
            Name = "Current package name", Price = 500000, PackageStatus = PackageStatus.Active,
            Components = [new PackageComponent { ComponentType = ComponentType.GYM, DurationDays = 30, AllowFreeze = true }]
        };
        var order = new Order
        {
            Purchaser = user, OrderNumber = "LEGACY-MIGRATION-ORDER", Channel = OrderChannel.Online,
            OrderStatus = OrderStatus.Paid, CompletedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
            PaymentTransaction = new PaymentTransaction
            {
                User = user, GatewayName = "SePay", GatewayTransactionReference = "legacy-migration-payment",
                PaymentMethod = PaymentMethod.VietQR, PaymentStatus = PaymentStatus.Paid, AmountDue = 500000,
                AmountReceived = 500000, PaidAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
                Invoice = new Invoice
                {
                    InvoiceNumber = "LEGACY-MIGRATION-INVOICE", IssuedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc)
                }
            }
        };
        var membership = new MemberMembership
        {
            Member = user, PackageComponent = package.Components.Single(), MembershipStatus = MembershipStatus.Active,
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 12, 7),
            TotalFrozenDays = 7, FreezeCount = 1,
            OrderItem = new OrderItem
            {
                Order = order, Package = package, Beneficiary = user, PackageNameSnapshot = "Legacy order snapshot",
                UnitPriceSnapshot = 500000, ComponentTermsSnapshot = "Legacy purchased terms"
            }
        };
        var freeze = new MembershipFreeze
        {
            MemberMembership = membership, RequestedByUser = user, ReviewedByUser = user,
            RequestedStartDate = new DateOnly(2026, 11, 3), RequestedEndDate = new DateOnly(2026, 11, 9),
            ActualStartDate = new DateOnly(2026, 11, 3), ActualEndDate = new DateOnly(2026, 11, 9),
            Reason = "Legacy medical leave", SupportingDocumentId = DocumentId,
            RequestStatus = FreezeRequestStatus.Approved, ReviewNote = "Approved before upgrade",
            ReviewedAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc), AppliedExpiryExtensionDays = 7,
            AppliedPolicySnapshot = "Policy captured before upgrade"
        };
        seed.MembershipFreezes.Add(freeze);
        await seed.SaveChangesAsync();
        seed.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user.UserId, AuditEntity = nameof(MembershipFreeze), EntityId = freeze.Id,
            ActionType = AuditActionType.Update, NewValue = "Existing audit must remain",
            OccurredAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc)
        });
        await seed.SaveChangesAsync();
        return (freeze.Id, membership.EndDate);
    }

    private sealed class LegacySeedContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // The deployed legacy layout has only the Guid FK; the three inline fields arrive during upgrade.
            modelBuilder.Entity<MembershipFreeze>().Ignore(row => row.SupportingDocumentFileName);
            modelBuilder.Entity<MembershipFreeze>().Ignore(row => row.SupportingDocumentContentType);
            modelBuilder.Entity<MembershipFreeze>().Ignore(row => row.SupportingDocumentContent);
        }
    }

    private sealed class DisposableMigrationDatabase : IAsyncDisposable
    {
        private const string Prefix = "VFMS_MigrationTest_";
        private readonly string _masterConnection;
        private readonly string _databaseName;
        public string ConnectionString { get; }

        private DisposableMigrationDatabase(string configuredConnection)
        {
            _databaseName = Prefix + Guid.NewGuid().ToString("N");
            var connection = new SqlConnectionStringBuilder(configuredConnection) { InitialCatalog = "master", Pooling = false };
            _masterConnection = connection.ConnectionString;
            connection.InitialCatalog = _databaseName;
            ConnectionString = connection.ConnectionString;
        }

        public static async Task<DisposableMigrationDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(SqlServerMigrationFactAttribute.ConnectionVariable)
                ?? throw new InvalidOperationException($"Set {SqlServerMigrationFactAttribute.ConnectionVariable} to run SQL Server migration tests.");
            var database = new DisposableMigrationDatabase(configured);
            await using var connection = new SqlConnection(database._masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{database.VerifiedName()}]";
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public VfmsDbContext Context() => new(new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer(ConnectionString).Options);

        public async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var name = VerifiedName();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }

        private string VerifiedName()
        {
            if (!_databaseName.StartsWith(Prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(_databaseName[Prefix.Length..], "N", out _))
                throw new InvalidOperationException("Refusing to modify a database outside the migration-test namespace.");
            return _databaseName;
        }
    }
}

public sealed class SqlServerMigrationFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "VFMS_TEST_SQLSERVER_CONNECTION";

    public SqlServerMigrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            Skip = $"Set {ConnectionVariable} to run disposable SQL Server migration regressions.";
    }
}
