using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations;

public partial class EnforceApplicationUserIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @whitespace nvarchar(32) = NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) +
                NCHAR(32) + NCHAR(133) + NCHAR(160) + NCHAR(5760) + NCHAR(8192) + NCHAR(8193) + NCHAR(8194) +
                NCHAR(8195) + NCHAR(8196) + NCHAR(8197) + NCHAR(8198) + NCHAR(8199) + NCHAR(8200) + NCHAR(8201) +
                NCHAR(8202) + NCHAR(8232) + NCHAR(8233) + NCHAR(8239) + NCHAR(8287) + NCHAR(12288);

            SELECT a.[UserId],
                LOWER(TRIM(@whitespace FROM a.[Email]) COLLATE Latin1_General_100_CI_AS_KS_WS_SC) AS [Email],
                REPLACE(REPLACE(REPLACE(REPLACE(raw.[Phone], N' ', N''), N'-', N''), N'(', N''), N')', N'') AS [Phone],
                CASE WHEN LEFT(raw.[Phone], 1) = N'+' THEN SUBSTRING(raw.[Phone], 2, 20) ELSE raw.[Phone] END
                    COLLATE Latin1_General_100_BIN2 AS [PhoneBody]
            INTO #CanonicalUserIdentity
            FROM [ApplicationUsers] AS a WITH (TABLOCKX, HOLDLOCK)
            CROSS APPLY (VALUES (TRIM(@whitespace FROM a.[PhoneNumber]))) AS raw([Phone])
            WHERE a.[IsDeleted] = 0;

            IF EXISTS (
                SELECT 1 FROM #CanonicalUserIdentity AS candidate
                WHERE CHARINDEX(N'@', candidate.[Email]) <= 1 OR RIGHT(candidate.[Email], 1) = N'@'
                    OR LEN(candidate.[Phone]) - CASE WHEN LEFT(candidate.[Phone], 1) = N'+' THEN 1 ELSE 0 END < 7
                    OR LEFT(candidate.[PhoneBody], 1) NOT LIKE N'[0-9(]'
                    OR RIGHT(candidate.[PhoneBody], 1) NOT LIKE N'[0-9)]'
                    OR LEN(candidate.[PhoneBody]) - LEN(REPLACE(candidate.[PhoneBody], N'(', N'')) <>
                       LEN(candidate.[PhoneBody]) - LEN(REPLACE(candidate.[PhoneBody], N')', N''))
                    OR EXISTS (
                        SELECT 1 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),
                            (11),(12),(13),(14),(15),(16),(17),(18),(19),(20)) AS positions([Position])
                        CROSS APPLY (VALUES (
                            LEFT(candidate.[PhoneBody], positions.[Position]),
                            SUBSTRING(candidate.[PhoneBody], positions.[Position], 1))) AS segment([Prefix], [Character])
                        CROSS APPLY (VALUES (LEN(REPLACE(segment.[Prefix], N')', N'')) -
                            LEN(REPLACE(segment.[Prefix], N'(', N'')))) AS parens([Depth])
                        WHERE positions.[Position] <= LEN(candidate.[PhoneBody]) AND (
                            (UNICODE(segment.[Character]) NOT IN (32, 40, 41, 45) AND UNICODE(segment.[Character]) NOT BETWEEN 48 AND 57)
                            OR (segment.[Character] IN (N' ', N'-') AND positions.[Position] < LEN(candidate.[PhoneBody]) AND
                                SUBSTRING(candidate.[PhoneBody], positions.[Position] + 1, 1) IN (N' ', N'-'))
                            OR parens.[Depth] NOT BETWEEN 0 AND 1
                            OR (parens.[Depth] = 1 AND segment.[Character] IN (N' ', N'-'))
                            OR (segment.[Character] = N'(' AND SUBSTRING(candidate.[PhoneBody], positions.[Position] + 1, 1) NOT LIKE N'[0-9]')
                            OR (segment.[Character] = N')' AND SUBSTRING(candidate.[PhoneBody], CASE WHEN positions.[Position] = 1 THEN 1 ELSE positions.[Position] - 1 END, 1) NOT LIKE N'[0-9]'))))
                THROW 51021, 'Cannot enforce application user identity. Correct invalid live email or phone values and rerun the migration.', 1;

            IF EXISTS (SELECT [Email] FROM #CanonicalUserIdentity GROUP BY [Email] HAVING COUNT(*) > 1)
                THROW 51022, 'Cannot enforce application user identity. Resolve duplicate canonical email addresses on nondeleted users.', 1;

            IF EXISTS (SELECT [Phone] FROM #CanonicalUserIdentity GROUP BY [Phone] HAVING COUNT(*) > 1)
                THROW 51023, 'Cannot enforce application user identity. Resolve duplicate canonical phone numbers on nondeleted users.', 1;

            DROP INDEX [IX_ApplicationUsers_Email] ON [ApplicationUsers];

            UPDATE a SET a.[Email] = candidate.[Email], a.[PhoneNumber] = candidate.[Phone]
            FROM [ApplicationUsers] AS a JOIN #CanonicalUserIdentity AS candidate ON a.[UserId] = candidate.[UserId]
            WHERE a.[Email] COLLATE Latin1_General_100_BIN2 <> candidate.[Email] COLLATE Latin1_General_100_BIN2
                OR DATALENGTH(a.[Email]) <> DATALENGTH(candidate.[Email])
                OR a.[PhoneNumber] COLLATE Latin1_General_100_BIN2 <> candidate.[Phone] COLLATE Latin1_General_100_BIN2
                OR DATALENGTH(a.[PhoneNumber]) <> DATALENGTH(candidate.[Phone]);

            DROP TABLE #CanonicalUserIdentity;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "Email", table: "ApplicationUsers", type: "nvarchar(256)", maxLength: 256, nullable: false,
            collation: "Latin1_General_100_CI_AS_KS_WS_SC", oldClrType: typeof(string), oldType: "nvarchar(256)", oldMaxLength: 256);

        migrationBuilder.AddColumn<string>(
            name: "AccountStatusBeforeLock", table: "ApplicationUsers", type: "nvarchar(50)", maxLength: 50, nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ApplicationUsers_CanonicalEmail", table: "ApplicationUsers", sql: """
                [IsDeleted] = 1 OR (DATALENGTH([Email]) > 0 AND
                    UNICODE(LEFT([Email], 1)) NOT IN (9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288) AND
                    UNICODE(RIGHT([Email], 1)) NOT IN (9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288))
                """.ReplaceLineEndings("\n"));

        migrationBuilder.AddCheckConstraint(
            name: "CK_ApplicationUsers_CanonicalPhoneNumber", table: "ApplicationUsers", sql: """
                [IsDeleted] = 1 OR (DATALENGTH([PhoneNumber]) = 2 * LEN([PhoneNumber]) AND
                    LEN([PhoneNumber]) - CASE WHEN LEFT([PhoneNumber], 1) = N'+' THEN 1 ELSE 0 END >= 7 AND
                    (CASE WHEN LEFT([PhoneNumber], 1) = N'+' THEN SUBSTRING([PhoneNumber], 2, 20) ELSE [PhoneNumber] END)
                        COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')
                """.ReplaceLineEndings("\n"));

        migrationBuilder.CreateIndex(name: "IX_ApplicationUsers_Email", table: "ApplicationUsers",
            column: "Email", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_ApplicationUsers_PhoneNumber", table: "ApplicationUsers",
            column: "PhoneNumber", unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT [Email] COLLATE DATABASE_DEFAULT FROM [ApplicationUsers] WITH (TABLOCKX, HOLDLOCK)
                GROUP BY [Email] COLLATE DATABASE_DEFAULT HAVING COUNT(*) > 1)
                THROW 51024, 'Cannot restore the legacy unfiltered email index while archived email addresses have been reused.', 1;
            """);

        migrationBuilder.DropIndex(name: "IX_ApplicationUsers_PhoneNumber", table: "ApplicationUsers");
        migrationBuilder.DropIndex(name: "IX_ApplicationUsers_Email", table: "ApplicationUsers");
        migrationBuilder.DropCheckConstraint(name: "CK_ApplicationUsers_CanonicalEmail", table: "ApplicationUsers");
        migrationBuilder.DropCheckConstraint(name: "CK_ApplicationUsers_CanonicalPhoneNumber", table: "ApplicationUsers");
        migrationBuilder.DropColumn(name: "AccountStatusBeforeLock", table: "ApplicationUsers");

        migrationBuilder.AlterColumn<string>(
            name: "Email", table: "ApplicationUsers", type: "nvarchar(256)", maxLength: 256, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(256)", oldMaxLength: 256, oldCollation: "Latin1_General_100_CI_AS_KS_WS_SC");

        migrationBuilder.CreateIndex(name: "IX_ApplicationUsers_Email", table: "ApplicationUsers", column: "Email", unique: true);
    }
}
