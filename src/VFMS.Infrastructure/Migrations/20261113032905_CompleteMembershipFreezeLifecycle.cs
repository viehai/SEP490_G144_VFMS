using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMembershipFreezeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // An earlier local build recorded this feature under 20261113020305 and
                // stored documents in a separate table. Adopt compatible existing columns
                // and worker indexes without deleting their data or rewriting EF history.
                migrationBuilder.Sql("""
                    IF COL_LENGTH(N'dbo.MembershipFreezes', N'SupportingDocumentContent') IS NULL
                        ALTER TABLE [dbo].[MembershipFreezes] ADD [SupportingDocumentContent] varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.MembershipFreezes', N'SupportingDocumentContentType') IS NULL
                        ALTER TABLE [dbo].[MembershipFreezes] ADD [SupportingDocumentContentType] nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.MembershipFreezes', N'SupportingDocumentFileName') IS NULL
                        ALTER TABLE [dbo].[MembershipFreezes] ADD [SupportingDocumentFileName] nvarchar(256) NULL;
                    IF COL_LENGTH(N'dbo.MembershipFreezes', N'SupportingDocumentId') IS NULL
                        ALTER TABLE [dbo].[MembershipFreezes] ADD [SupportingDocumentId] uniqueidentifier NULL;

                    IF EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE [object_id] = OBJECT_ID(N'dbo.MembershipFreezes') AND (
                            ([name] = N'SupportingDocumentContent' AND (TYPE_NAME([user_type_id]) <> N'varbinary' OR [max_length] <> -1 OR [is_nullable] <> 1)) OR
                            ([name] = N'SupportingDocumentContentType' AND (TYPE_NAME([user_type_id]) <> N'nvarchar' OR [max_length] <> 200 OR [is_nullable] <> 1)) OR
                            ([name] = N'SupportingDocumentFileName' AND (TYPE_NAME([user_type_id]) <> N'nvarchar' OR [max_length] <> 512 OR [is_nullable] <> 1)) OR
                            ([name] = N'SupportingDocumentId' AND (TYPE_NAME([user_type_id]) <> N'uniqueidentifier' OR [is_nullable] <> 1))))
                        THROW 51000, 'Existing membership freeze document columns have incompatible types; migration stopped without discarding data.', 1;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.MembershipFreezes')
                        AND [name] = N'IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id')
                        CREATE INDEX [IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id]
                            ON [dbo].[MembershipFreezes] ([RequestStatus], [RequestedEndDate], [Id]);
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.MembershipFreezes')
                        AND [name] = N'IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id')
                        CREATE INDEX [IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id]
                            ON [dbo].[MembershipFreezes] ([RequestStatus], [RequestedStartDate], [Id]);

                    IF EXISTS (
                        SELECT 1 FROM sys.indexes i
                        WHERE i.[object_id] = OBJECT_ID(N'dbo.MembershipFreezes') AND i.[name] IN (
                            N'IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id',
                            N'IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id') AND (
                            i.[is_disabled] = 1 OR i.[has_filter] = 1 OR
                            (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.[object_id] = i.[object_id]
                                AND ic.[index_id] = i.[index_id] AND ic.[key_ordinal] > 0) <> 3 OR
                            NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c
                                ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                                WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                                    AND ic.[key_ordinal] = 1 AND c.[name] = N'RequestStatus') OR
                            NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c
                                ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                                WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                                    AND ic.[key_ordinal] = 2 AND c.[name] = CASE
                                        WHEN i.[name] = N'IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id'
                                        THEN N'RequestedEndDate' ELSE N'RequestedStartDate' END) OR
                            NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c
                                ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                                WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                                    AND ic.[key_ordinal] = 3 AND c.[name] = N'Id')))
                        THROW 51000, 'Existing membership freeze lifecycle indexes are incompatible; migration stopped without replacing them.', 1;
                    """);
                return;
            }

            migrationBuilder.AddColumn<byte[]>(
                name: "SupportingDocumentContent",
                table: "MembershipFreezes",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentContentType",
                table: "MembershipFreezes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportingDocumentFileName",
                table: "MembershipFreezes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupportingDocumentId",
                table: "MembershipFreezes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id",
                table: "MembershipFreezes",
                columns: new[] { "RequestStatus", "RequestedEndDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id",
                table: "MembershipFreezes",
                columns: new[] { "RequestStatus", "RequestedStartDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedEndDate_Id",
                table: "MembershipFreezes");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFreezes_RequestStatus_RequestedStartDate_Id",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentContent",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentContentType",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentFileName",
                table: "MembershipFreezes");

            migrationBuilder.DropColumn(
                name: "SupportingDocumentId",
                table: "MembershipFreezes");
        }
    }
}
