using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileLegacyMembershipSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
                return;

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.MembershipFreezeDocuments', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.MembershipFreezeDocuments', N'Id') IS NULL OR
                       COL_LENGTH(N'dbo.MembershipFreezeDocuments', N'FileName') IS NULL OR
                       COL_LENGTH(N'dbo.MembershipFreezeDocuments', N'ContentType') IS NULL OR
                       COL_LENGTH(N'dbo.MembershipFreezeDocuments', N'Content') IS NULL OR
                       COL_LENGTH(N'dbo.MembershipFreezeDocuments', N'Size') IS NULL
                        THROW 51000, 'Legacy freeze document schema is incompatible; no attachments were removed.', 1;

                    IF EXISTS (
                        SELECT 1 FROM sys.foreign_keys fk
                        WHERE fk.[referenced_object_id] = OBJECT_ID(N'dbo.MembershipFreezeDocuments') AND (
                            fk.[parent_object_id] <> OBJECT_ID(N'dbo.MembershipFreezes') OR
                            (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                                WHERE fc.[constraint_object_id] = fk.[object_id]) <> 1 OR
                            NOT EXISTS (
                                SELECT 1 FROM sys.foreign_key_columns fc
                                JOIN sys.columns pc ON pc.[object_id] = fc.[parent_object_id]
                                    AND pc.[column_id] = fc.[parent_column_id]
                                JOIN sys.columns rc ON rc.[object_id] = fc.[referenced_object_id]
                                    AND rc.[column_id] = fc.[referenced_column_id]
                                WHERE fc.[constraint_object_id] = fk.[object_id]
                                    AND pc.[name] = N'SupportingDocumentId' AND rc.[name] = N'Id')))
                        THROW 51000, 'Legacy freeze documents have unexpected references; no attachments were removed.', 1;

                    -- Dynamic SQL is necessary: the legacy table is absent on a fresh database.
                    -- Validate every payload before copying or dropping anything.
                    EXEC(N'
                        IF EXISTS (SELECT 1 FROM [dbo].[MembershipFreezeDocuments] d
                            WHERE NOT EXISTS (SELECT 1 FROM [dbo].[MembershipFreezes] f WHERE f.[SupportingDocumentId] = d.[Id]))
                            THROW 51001, ''Legacy freeze documents contain unreferenced attachments; preserve or associate them before retrying the migration.'', 1;

                        IF EXISTS (SELECT 1 FROM [dbo].[MembershipFreezeDocuments]
                            WHERE [Size] <> DATALENGTH([Content]))
                            THROW 51000, ''Legacy freeze document size does not match its payload; no attachments were removed.'', 1;

                        IF EXISTS (SELECT 1 FROM [dbo].[MembershipFreezes] f
                            JOIN [dbo].[MembershipFreezeDocuments] d ON d.[Id] = f.[SupportingDocumentId]
                            WHERE
                                (f.[SupportingDocumentFileName] IS NOT NULL AND
                                    CONVERT(varbinary(max), f.[SupportingDocumentFileName]) <> CONVERT(varbinary(max), d.[FileName])) OR
                                (f.[SupportingDocumentContentType] IS NOT NULL AND
                                    CONVERT(varbinary(max), f.[SupportingDocumentContentType]) <> CONVERT(varbinary(max), d.[ContentType])) OR
                                (f.[SupportingDocumentContent] IS NOT NULL AND
                                    (DATALENGTH(f.[SupportingDocumentContent]) <> DATALENGTH(d.[Content]) OR
                                     f.[SupportingDocumentContent] <> d.[Content])))
                            THROW 51002, ''Legacy freeze documents have conflicting inline attachment data; resolve the conflict before retrying the migration.'', 1;

                        IF EXISTS (SELECT [SupportingDocumentId] FROM [dbo].[MembershipFreezes]
                            WHERE [SupportingDocumentId] IS NOT NULL GROUP BY [SupportingDocumentId] HAVING COUNT(*) > 1)
                            THROW 51003, ''A legacy freeze document is referenced by multiple requests; resolve the duplicate document identity before retrying the migration.'', 1;

                        IF EXISTS (SELECT 1 FROM [dbo].[MembershipFreezes] f
                            WHERE f.[SupportingDocumentId] IS NOT NULL AND
                                (f.[SupportingDocumentFileName] IS NULL OR f.[SupportingDocumentContentType] IS NULL OR f.[SupportingDocumentContent] IS NULL) AND
                                NOT EXISTS (SELECT 1 FROM [dbo].[MembershipFreezeDocuments] d WHERE d.[Id] = f.[SupportingDocumentId]))
                            THROW 51004, ''A freeze request has an incomplete attachment with no matching legacy document; no attachments were removed.'', 1;

                        UPDATE f SET
                            [SupportingDocumentFileName] = COALESCE(f.[SupportingDocumentFileName], d.[FileName]),
                            [SupportingDocumentContentType] = COALESCE(f.[SupportingDocumentContentType], d.[ContentType]),
                            [SupportingDocumentContent] = COALESCE(f.[SupportingDocumentContent], d.[Content])
                        FROM [dbo].[MembershipFreezes] f
                        JOIN [dbo].[MembershipFreezeDocuments] d ON d.[Id] = f.[SupportingDocumentId];
                    ');

                    DECLARE @dropLegacyReferences nvarchar(max) = N'';
                    SELECT @dropLegacyReferences = @dropLegacyReferences +
                        N'ALTER TABLE [dbo].[MembershipFreezes] DROP CONSTRAINT ' + QUOTENAME(fk.[name]) + N';'
                    FROM sys.foreign_keys fk
                    WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.MembershipFreezes')
                        AND fk.[referenced_object_id] = OBJECT_ID(N'dbo.MembershipFreezeDocuments');

                    -- Only retire the old document-reference lookup indexes. Keep the Id,
                    -- lifecycle indexes and all unrelated indexes intact.
                    SELECT @dropLegacyReferences = @dropLegacyReferences +
                        CASE WHEN i.[is_unique_constraint] = 1
                            THEN N'ALTER TABLE [dbo].[MembershipFreezes] DROP CONSTRAINT ' + QUOTENAME(i.[name]) + N';'
                            ELSE N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[MembershipFreezes];' END
                    FROM sys.indexes i
                    WHERE i.[object_id] = OBJECT_ID(N'dbo.MembershipFreezes') AND i.[is_primary_key] = 0
                        AND i.[type] > 0 AND
                        (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.[object_id] = i.[object_id]
                            AND ic.[index_id] = i.[index_id]) = 1 AND
                        EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c
                            ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                                AND ic.[key_ordinal] = 1 AND c.[name] = N'SupportingDocumentId');
                    IF LEN(@dropLegacyReferences) > 0 EXEC(@dropLegacyReferences);

                    DROP TABLE [dbo].[MembershipFreezeDocuments];
                END

                EXEC(N'
                    IF EXISTS (SELECT 1 FROM [dbo].[MembershipFreezes]
                        WHERE [SupportingDocumentId] IS NOT NULL AND
                            ([SupportingDocumentFileName] IS NULL OR [SupportingDocumentContentType] IS NULL OR [SupportingDocumentContent] IS NULL))
                        THROW 51004, ''A freeze request has an incomplete inline attachment; no attachment data was discarded.'', 1;
                ');

                DECLARE @dropPackageDefaults nvarchar(max) = N'';
                SELECT @dropPackageDefaults = @dropPackageDefaults +
                    N'ALTER TABLE [dbo].[Packages] DROP CONSTRAINT ' + QUOTENAME(dc.[name]) + N';'
                FROM sys.default_constraints dc JOIN sys.columns c
                    ON c.[object_id] = dc.[parent_object_id] AND c.[column_id] = dc.[parent_column_id]
                WHERE dc.[parent_object_id] = OBJECT_ID(N'dbo.Packages')
                    AND c.[name] IN (N'AccessLevel', N'IncludedFeatures');
                IF LEN(@dropPackageDefaults) > 0 EXEC(@dropPackageDefaults);
                IF COL_LENGTH(N'dbo.Packages', N'AccessLevel') IS NOT NULL
                    ALTER TABLE [dbo].[Packages] DROP COLUMN [AccessLevel];
                IF COL_LENGTH(N'dbo.Packages', N'IncludedFeatures') IS NOT NULL
                    ALTER TABLE [dbo].[Packages] DROP COLUMN [IncludedFeatures];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only reconciliation of artifacts absent from the preceding EF model.
            // Retain the authoritative inline attachments on downgrade; do not recreate
            // duplicate document storage or pretend removed legacy package metadata is recoverable.
        }
    }
}
