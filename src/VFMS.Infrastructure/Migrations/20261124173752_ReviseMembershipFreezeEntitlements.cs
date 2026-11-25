using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviseMembershipFreezeEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FreezeAllowanceOverrideDays",
                table: "Packages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreezeAllowanceOverrideUnit",
                table: "Packages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAudience",
                table: "Packages",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedFreezeAllowanceDays",
                table: "MemberMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultFreezeAllowanceDays",
                table: "FreezePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultFreezeAllowanceUnit",
                table: "FreezePolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EligibilityPaidDaysUnit",
                table: "FreezePolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaximumFreezeDaysUnit",
                table: "FreezePolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumFreezeDaysUnit",
                table: "FreezePolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumRequestDaysUnit",
                table: "FreezePolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Packages_FreezeAllowanceOverride",
                table: "Packages",
                sql: "([FreezeAllowanceOverrideDays] IS NULL AND [FreezeAllowanceOverrideUnit] IS NULL) OR ([FreezeAllowanceOverrideDays] IS NOT NULL AND [FreezeAllowanceOverrideUnit] IS NOT NULL AND [FreezeAllowanceOverrideDays] >= 0 AND ([FreezeAllowanceOverrideUnit] = 0 OR ([FreezeAllowanceOverrideUnit] = 1 AND [FreezeAllowanceOverrideDays] % 30 = 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberMemberships_GrantedFreezeAllowanceDays",
                table: "MemberMemberships",
                sql: "[GrantedFreezeAllowanceDays] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FreezePolicies_DurationUnits",
                table: "FreezePolicies",
                sql: "([EligibilityPaidDaysUnit] = 0 OR ([EligibilityPaidDaysUnit] = 1 AND [EligibilityPaidDays] % 30 = 0)) AND ([MinimumRequestDaysUnit] = 0 OR ([MinimumRequestDaysUnit] = 1 AND [MinimumRequestDays] % 30 = 0)) AND ([MinimumFreezeDaysUnit] = 0 OR ([MinimumFreezeDaysUnit] = 1 AND [MinFreezeDaysPerRequest] IS NOT NULL AND [MinFreezeDaysPerRequest] % 30 = 0)) AND ([MaximumFreezeDaysUnit] = 0 OR ([MaximumFreezeDaysUnit] = 1 AND [DefaultMaxFreezeDays] % 30 = 0)) AND ([DefaultFreezeAllowanceUnit] = 0 OR ([DefaultFreezeAllowanceUnit] = 1 AND [DefaultFreezeAllowanceDays] IS NOT NULL AND [DefaultFreezeAllowanceDays] % 30 = 0)) AND ([DefaultFreezeAllowanceDays] IS NULL OR [DefaultFreezeAllowanceDays] >= 0)");

            migrationBuilder.Sql("""
                DECLARE @policyId int = (
                    SELECT [Id] FROM [dbo].[FreezePolicies]
                    WHERE [IsGlobal] = 1 AND [IsDeleted] = 0 AND [PolicyStatus] = 'Active'
                        AND [MinFreezeDaysPerRequest] > 0 AND [DefaultMaxFreezeDays] >= [MinFreezeDaysPerRequest]
                        AND [EligibilityPaidDays] >= 0 AND [MinimumRequestDays] >= 0
                );

                -- Plan overrides can reproduce an exact legacy duration match. A composite plan
                -- can have only one override, so conflicting component rights require preflight.
                SELECT pc.[PackageId], MIN(q.[MaximumCumulativeFreezeDays]) AS [Allowance],
                    MAX(q.[MaximumCumulativeFreezeDays]) AS [LargestAllowance],
                    COUNT(q.[Duration]) AS [MatchedComponents], COUNT(*) AS [EligibleComponents]
                INTO #LegacyPlanAllowances
                FROM [dbo].[PackageComponents] pc
                JOIN [dbo].[Packages] p ON p.[Id] = pc.[PackageId] AND p.[IsDeleted] = 0
                LEFT JOIN [dbo].[FreezePolicyTierQuotas] q ON q.[FreezePolicyId] = @policyId
                    AND q.[Unit] = CASE WHEN pc.[DurationDays] > 0 THEN 'Days' ELSE 'Months' END
                    AND q.[Duration] = CASE WHEN pc.[DurationDays] > 0 THEN pc.[DurationDays] ELSE pc.[DurationMonths] END
                    AND q.[MaximumCumulativeFreezeDays] >= 0
                WHERE pc.[IsDeleted] = 0 AND pc.[ComponentType] IN ('GYM', 'YOGA')
                    AND (pc.[ComponentType] = 'GYM' OR pc.[DurationDays] IS NULL OR pc.[DurationDays] <> 7)
                GROUP BY pc.[PackageId]
                HAVING COUNT(q.[Duration]) > 0;

                IF EXISTS (SELECT 1 FROM #LegacyPlanAllowances
                    WHERE [Allowance] <> [LargestAllowance] OR [MatchedComponents] <> [EligibleComponents])
                BEGIN
                    DECLARE @ambiguousPlan int = (SELECT MIN([PackageId]) FROM #LegacyPlanAllowances
                        WHERE [Allowance] <> [LargestAllowance] OR [MatchedComponents] <> [EligibleComponents]);
                    DECLARE @message nvarchar(2048) = CONCAT('Freeze allowance preflight: package ', @ambiguousPlan,
                        ' has different or unmatched legacy allowances across its GYM/YOGA components.');
                    THROW 51020, @message, 1;
                END;

                UPDATE p SET [FreezeAllowanceOverrideDays] = resolved.[Allowance], [FreezeAllowanceOverrideUnit] = 0
                FROM [dbo].[Packages] p JOIN #LegacyPlanAllowances resolved ON resolved.[PackageId] = p.[Id];
                DROP TABLE #LegacyPlanAllowances;

                -- Purchased terms previously used the component only when the purchase snapshot was NULL.
                -- Malformed, duplicate or missing terms have no provable entitlement and remain NULL.
                -- With a verified term and configured policy, an absent tier had no freeze entitlement.
                UPDATE m SET [GrantedFreezeAllowanceDays] =
                    CASE WHEN pc.[ComponentType] = 'YOGA' AND duration.[Days] = 7 THEN 0
                        WHEN @policyId IS NOT NULL AND (duration.[Days] > 0 OR duration.[Months] > 0)
                            THEN COALESCE(q.[MaximumCumulativeFreezeDays], 0)
                        ELSE NULL END
                FROM [dbo].[MemberMemberships] m
                JOIN [dbo].[OrderItems] oi ON oi.[Id] = m.[OrderItemId]
                JOIN [dbo].[PackageComponents] pc ON pc.[Id] = m.[PackageComponentId]
                OUTER APPLY (
                    SELECT MAX(COALESCE(TRY_CONVERT(int, terms.[Days]), TRY_CONVERT(int, terms.[OldDays]))) AS [Days],
                        MAX(COALESCE(TRY_CONVERT(int, terms.[Months]), TRY_CONVERT(int, terms.[OldMonths]))) AS [Months]
                    FROM OPENJSON(CASE WHEN ISJSON(oi.[ComponentTermsSnapshot]) = 1 AND
                        LEFT(LTRIM(oi.[ComponentTermsSnapshot]), 1) = '[' THEN oi.[ComponentTermsSnapshot] ELSE N'[]' END)
                        WITH ([Id] nvarchar(30) '$.Id', [Days] nvarchar(30) '$.Days', [Months] nvarchar(30) '$.Months',
                            [OldDays] nvarchar(30) '$.DurationDays', [OldMonths] nvarchar(30) '$.DurationMonths') terms
                    WHERE TRY_CONVERT(int, terms.[Id]) = m.[PackageComponentId]
                    HAVING COUNT(*) = 1
                ) purchased
                CROSS APPLY (VALUES (
                    CASE WHEN oi.[ComponentTermsSnapshot] IS NULL THEN pc.[DurationDays] ELSE purchased.[Days] END,
                    CASE WHEN oi.[ComponentTermsSnapshot] IS NULL THEN pc.[DurationMonths] ELSE purchased.[Months] END
                )) duration([Days], [Months])
                LEFT JOIN [dbo].[FreezePolicyTierQuotas] q ON q.[FreezePolicyId] = @policyId
                    AND q.[Unit] = CASE WHEN duration.[Days] > 0 THEN 'Days' ELSE 'Months' END
                    AND q.[Duration] = CASE WHEN duration.[Days] > 0 THEN duration.[Days] ELSE duration.[Months] END
                    AND q.[MaximumCumulativeFreezeDays] >= 0
                WHERE pc.[ComponentType] IN ('GYM', 'YOGA');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Legacy duration quotas cannot represent purchased freeze allowances or per-plan overrides. Restore the pre-migration backup to roll back.");
        }
    }
}
