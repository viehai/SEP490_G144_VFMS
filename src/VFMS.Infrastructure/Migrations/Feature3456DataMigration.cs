namespace VFMS.Infrastructure.Migrations;

// Frozen migration SQL: deliberately independent of the current domain model.
internal static class Feature3456DataMigration
{
    internal const string LockSources = """
        IF @@TRANCOUNT = 0 THROW 51100, 'Schema simplification requires a transaction.', 1;
        DECLARE @lockResult int, @rows bigint;
        EXEC @lockResult = sys.sp_getapplock @Resource=N'VFMS.Feature3456Schema',
            @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
        IF @lockResult < 0 THROW 51100, 'Could not lock schema simplification sources.', 1;
        SELECT @rows=COUNT_BIG(*) FROM TrainerProfiles WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM TrainerApplications WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM TrainerReviews WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM ApplicationUsers WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PhysicalProfiles WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PTPurchases WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PTSessions WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PTFreezes WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PTAvailabilities WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM Orders WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM OrderItems WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM MemberMemberships WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM VoucherRedemptions WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PackageComponents WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM PaymentTransactions WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM Lockers WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM LockerPricingPolicies WITH(TABLOCKX,HOLDLOCK);
        SELECT @rows=COUNT_BIG(*) FROM LockerRentalContracts WITH(TABLOCKX,HOLDLOCK);
        """;

    internal const string Consolidate = """
        -- Identity is shared; conflicting nonempty values require an explicit data decision.
        IF EXISTS(SELECT 1 FROM TrainerProfiles p JOIN ApplicationUsers u ON u.UserId=p.PTUserId
            WHERE NULLIF(LTRIM(RTRIM(p.AvatarUrl)),N'') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM(u.AvatarUrl)),N'') IS NOT NULL
              AND p.AvatarUrl COLLATE Latin1_General_100_BIN2 <> u.AvatarUrl COLLATE Latin1_General_100_BIN2)
            THROW 51101, 'Conflicting trainer and user avatar URLs; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM TrainerProfiles p JOIN PhysicalProfiles f ON f.MemberUserId=p.PTUserId
            WHERE NULLIF(LTRIM(RTRIM(p.Gender)),N'') IS NOT NULL AND
              (f.IsDeleted=1 OR (NULLIF(LTRIM(RTRIM(f.Gender)),N'') IS NOT NULL AND f.Gender<>p.Gender)))
            THROW 51102, 'Conflicting trainer and physical-profile gender; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM TrainerProfiles WHERE (AcceptingSince IS NOT NULL AND AcceptingNewMembers=0)
            OR (NotAcceptingSince IS NOT NULL AND AcceptingNewMembers=1))
            THROW 51103, 'Trainer has two readiness transition dates; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM TrainerProfiles WHERE (NULLIF(LTRIM(RTRIM(AvatarJson)),N'') IS NOT NULL AND ISJSON(AvatarJson)<>1)
            OR ISJSON(CertificatesJson)<>1)
            OR EXISTS(SELECT 1 FROM TrainerApplications WHERE ISJSON(CertificatesJson)<>1)
            THROW 51104, 'Invalid trainer file metadata; repair before migration.', 1;
        IF EXISTS(SELECT 1 FROM TrainerProfiles p JOIN ApplicationUsers u ON u.UserId=p.PTUserId
            WHERE NULLIF(LTRIM(RTRIM(p.AvatarJson)),N'') IS NOT NULL
              AND ISNULL(JSON_VALUE(NULLIF(LTRIM(RTRIM(p.AvatarJson)),N''),'$.IsDeleted'),'false')<>'true'
              AND ((NULLIF(LTRIM(RTRIM(u.AvatarUrl)),N'') IS NOT NULL
                    AND u.AvatarUrl<>CONCAT(N'/api/trainers/',p.Id,N'/avatar'))
                OR (NULLIF(LTRIM(RTRIM(p.AvatarUrl)),N'') IS NOT NULL
                    AND p.AvatarUrl<>CONCAT(N'/api/trainers/',p.Id,N'/avatar'))))
            THROW 51101, 'Conflicting uploaded trainer avatar and URL; resolve before migration.', 1;
        UPDATE u SET AvatarUrl=COALESCE(NULLIF(LTRIM(RTRIM(p.AvatarUrl)),N''),
            CASE WHEN NULLIF(LTRIM(RTRIM(p.AvatarJson)),N'') IS NOT NULL AND ISNULL(JSON_VALUE(NULLIF(LTRIM(RTRIM(p.AvatarJson)),N''),'$.IsDeleted'),'false')<>'true'
                THEN CONCAT(N'/api/trainers/',p.Id,N'/avatar') END)
        FROM ApplicationUsers u JOIN TrainerProfiles p ON p.PTUserId=u.UserId
        WHERE NULLIF(LTRIM(RTRIM(u.AvatarUrl)),N'') IS NULL;
        INSERT INTO PhysicalProfiles(MemberUserId,Gender,CreatedAt,UpdatedAt,IsDeleted)
        SELECT p.PTUserId,p.Gender,SYSUTCDATETIME(),SYSUTCDATETIME(),0 FROM TrainerProfiles p
        WHERE NULLIF(LTRIM(RTRIM(p.Gender)),N'') IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM PhysicalProfiles f WHERE f.MemberUserId=p.PTUserId);
        UPDATE f SET Gender=p.Gender FROM PhysicalProfiles f JOIN TrainerProfiles p ON p.PTUserId=f.MemberUserId
        WHERE NULLIF(LTRIM(RTRIM(f.Gender)),N'') IS NULL AND NULLIF(LTRIM(RTRIM(p.Gender)),N'') IS NOT NULL;
        UPDATE TrainerProfiles SET NotAcceptingSince=CASE WHEN AcceptingNewMembers=1 THEN AcceptingSince ELSE NotAcceptingSince END;

        -- Retain legacy attachments in the existing metadata, with the same visibility.
        UPDATE a SET CertificatesJson=JSON_MODIFY(a.CertificatesJson,'append $',JSON_QUERY((
            SELECT CONCAT(N'legacy-application-',a.Id) AS Id,N'Application attachment' AS FileName,
                N'text/uri-list' AS ContentType,a.ApplicationFileUrl AS ExternalUrl,
                CAST(0 AS bit) AS IsPublic,CAST(0 AS bit) AS IsAvatar,CAST(0 AS bit) AS IsDeleted
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)))
        FROM TrainerApplications a WHERE NULLIF(LTRIM(RTRIM(a.ApplicationFileUrl)),N'') IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM OPENJSON(a.CertificatesJson) WITH(ExternalUrl nvarchar(max) '$.ExternalUrl') d
              WHERE d.ExternalUrl=a.ApplicationFileUrl);
        UPDATE p SET CertificatesJson=JSON_MODIFY(p.CertificatesJson,'append $',JSON_QUERY((
            SELECT CONCAT(N'legacy-certificate-',p.Id) AS Id,N'Certificate' AS FileName,
                N'text/uri-list' AS ContentType,p.CertificateUrl AS ExternalUrl,
                CAST(CASE WHEN NOT EXISTS(SELECT 1 FROM OPENJSON(p.CertificatesJson)) THEN 1 ELSE 0 END AS bit) AS IsPublic,
                CAST(0 AS bit) AS IsAvatar,CAST(0 AS bit) AS IsDeleted
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)))
        FROM TrainerProfiles p WHERE NULLIF(LTRIM(RTRIM(p.CertificateUrl)),N'') IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM OPENJSON(p.CertificatesJson) WITH(ExternalUrl nvarchar(max) '$.ExternalUrl') d
              WHERE d.ExternalUrl=p.CertificateUrl);

        -- Remove accidental EF relationships only if they agree with the real trainer key.
        IF EXISTS(SELECT 1 FROM PTPurchases x JOIN TrainerProfiles p ON p.Id=x.TrainerProfileId WHERE x.PTUserId<>p.PTUserId)
          OR EXISTS(SELECT 1 FROM PTAvailabilities x JOIN TrainerProfiles p ON p.Id=x.TrainerProfileId WHERE x.PTUserId<>p.PTUserId)
          OR EXISTS(SELECT 1 FROM TrainerReviews x JOIN TrainerProfiles p ON p.Id=x.TrainerProfileId WHERE x.TrainerUserId<>p.PTUserId)
            THROW 51105, 'Conflicting redundant trainer relationship; resolve before migration.', 1;

        UPDATE p SET OrderId=i.OrderId FROM PTPurchases p
        JOIN MemberMemberships m ON m.Id=p.MemberMembershipId JOIN OrderItems i ON i.Id=m.OrderItemId
        JOIN PackageComponents c ON c.Id=m.PackageComponentId
        WHERE p.OrderId IS NULL AND c.ComponentType=N'PT' AND i.BeneficiaryUserId=p.MemberUserId;
        IF EXISTS(SELECT 1 FROM PTPurchases p WHERE p.OrderId IS NULL
            OR (SELECT COUNT(*) FROM OrderItems i WHERE i.OrderId=p.OrderId)<>1)
            THROW 51110, 'PT purchase requires exactly one existing order item; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTPurchases p JOIN OrderItems i ON i.OrderId=p.OrderId
            WHERE i.BeneficiaryUserId<>p.MemberUserId OR
              (NULLIF(p.PackageNameSnapshot,N'') IS NOT NULL AND NULLIF(i.PackageNameSnapshot,N'') IS NOT NULL
               AND p.PackageNameSnapshot COLLATE Latin1_General_100_BIN2 <> i.PackageNameSnapshot COLLATE Latin1_General_100_BIN2))
            THROW 51111, 'Conflicting PT beneficiary or captured package name; resolve before migration.', 1;
        UPDATE i SET PackageNameSnapshot=p.PackageNameSnapshot FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
            WHERE NULLIF(i.PackageNameSnapshot,N'') IS NULL;
        -- A missing snapshot can be rebuilt only for an unambiguous PT-only item, using purchased counts.
        UPDATE i SET ComponentTermsSnapshot=(SELECT c.Id,2 AS Type,c.DurationDays AS Days,c.DurationMonths AS Months,
            p.TotalSessions AS SessionCount FROM PackageComponents c WHERE c.PackageId=i.PackageId FOR JSON PATH)
        FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
        WHERE i.ComponentTermsSnapshot IS NULL AND (SELECT COUNT(*) FROM PackageComponents c WHERE c.PackageId=i.PackageId)=1
            AND EXISTS(SELECT 1 FROM PackageComponents c WHERE c.PackageId=i.PackageId AND c.ComponentType=N'PT');
        IF EXISTS(SELECT 1 FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
            WHERE i.ComponentTermsSnapshot IS NULL OR ISJSON(i.ComponentTermsSnapshot)<>1)
            THROW 51112, 'PT purchase has missing or invalid captured component terms.', 1;
        IF EXISTS(SELECT 1 FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
            WHERE (SELECT COUNT(*) FROM OPENJSON(i.ComponentTermsSnapshot) WITH(Type int '$.Type') c WHERE c.Type=2)<>1)
            THROW 51112, 'PT purchase requires one captured PT component.', 1;
        IF EXISTS(SELECT 1 FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
            CROSS APPLY OPENJSON(i.ComponentTermsSnapshot) c CROSS APPLY OPENJSON(c.value) field
            WHERE TRY_CONVERT(int,JSON_VALUE(c.value,'$.Type'))=2 AND field.[key]=N'SessionCount' AND field.[type] NOT IN(0,2))
            THROW 51113, 'Captured PT session count must be a number or null; resolve before migration.', 1;
        UPDATE i SET ComponentTermsSnapshot=JSON_MODIFY(i.ComponentTermsSnapshot,
            CONCAT(N'$[',c.[key],N'].SessionCount'),p.TotalSessions)
        FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
        CROSS APPLY OPENJSON(i.ComponentTermsSnapshot) c
        WHERE TRY_CONVERT(int,JSON_VALUE(c.value,'$.Type'))=2 AND JSON_VALUE(c.value,'$.SessionCount') IS NULL;
        IF EXISTS(SELECT 1 FROM OrderItems i JOIN PTPurchases p ON p.OrderId=i.OrderId
            CROSS APPLY OPENJSON(i.ComponentTermsSnapshot) WITH(Type int '$.Type',SessionCount int '$.SessionCount') c
            WHERE c.Type=2 AND (c.SessionCount IS NULL OR c.SessionCount<>p.TotalSessions OR c.SessionCount<0))
            THROW 51113, 'Conflicting PT purchased session count; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTPurchases p JOIN OrderItems i ON i.OrderId=p.OrderId
            LEFT JOIN PaymentTransactions t ON t.OrderId=p.OrderId LEFT JOIN VoucherRedemptions v ON v.OrderId=p.OrderId
            WHERE p.PurchasePrice<>COALESCE(t.AmountDue,i.UnitPriceSnapshot-ISNULL(v.DiscountAmount,0)))
            THROW 51114, 'Conflicting PT purchase price and order payment; resolve before migration.', 1;
        UPDATE PTPurchases SET Status=N'Completed' WHERE IsCompleted=1;
        IF EXISTS(SELECT 1 FROM PaymentTransactions t JOIN Orders o ON o.Id=t.OrderId WHERE t.UserId<>o.PurchaserUserId)
            THROW 51115, 'Conflicting payment owner and order purchaser; resolve before migration.', 1;

        IF EXISTS(SELECT 1 FROM PTSessions s JOIN PTPurchases p ON p.Id=s.PTPurchaseId WHERE s.MemberUserId<>p.MemberUserId)
            THROW 51120, 'Conflicting PT session member; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTSessions WHERE ProposedAdjustmentType=N'DoubleSession')
            THROW 51121, 'Legacy PT double-session proposal must be resolved before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTSessions WHERE ProposedEndTime IS NOT NULL AND (ProposedStartTime IS NULL
            OR DATEDIFF_BIG(nanosecond,ProposedStartTime,ProposedEndTime)<>DATEDIFF_BIG(nanosecond,StartTime,EndTime)))
            THROW 51122, 'PT proposal duration differs from its original session; resolve before migration.', 1;
        UPDATE PTSessions SET SessionStatus=CASE SessionStatus WHEN N'ReschedulePending' THEN N'CancellationPending'
            WHEN N'RescheduleRequested' THEN N'CancellationRequested' ELSE SessionStatus END
            WHERE ProposedAdjustmentType=N'Cancellation';
        IF EXISTS(SELECT 1 FROM PTAvailabilities WHERE EndTime<>CONVERT(time(7),DATEADD(hour,1,CONVERT(datetime2(7),StartTime))))
            THROW 51123, 'PT availability is not one hour; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTFreezes f JOIN PTPurchases p ON p.Id=f.PTPurchaseId
            WHERE f.RequestedByUserId<>p.MemberUserId
              OR (f.MinimumDaysSnapshot IS NOT NULL AND f.MinimumDaysSnapshot<>p.FreezeMinimumDaysSnapshot)
              OR (f.MaxDaysSnapshot IS NOT NULL AND f.MaxDaysSnapshot<>p.FreezeMaxTotalDaysSnapshot)
              OR (f.ActualStartDate IS NULL AND f.RequestStatus IN(N'Approved',N'Active',N'Completed'))
              OR (f.ActualStartDate IS NOT NULL AND (f.ActualStartDate<>f.RequestedStartDate
                  OR f.RequestStatus NOT IN(N'Approved',N'Active',N'Completed'))))
            THROW 51124, 'Conflicting PT freeze owner, limits or actual start; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTFreezes WHERE NOT
            ((ChangeRequestType IS NULL AND ChangeRequestStatus IS NULL) OR
             (ChangeRequestType IN(N'Withdraw',N'EarlyResume') AND ChangeRequestStatus IN(N'Pending',N'Approved',N'Rejected')))
            OR (ChangeRequestType IS NULL AND ChangeRequestStatus IS NOT NULL)
            OR (ChangeRequestType IS NOT NULL AND ChangeRequestStatus IS NULL))
            THROW 51125, 'Invalid PT freeze change state; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM PTFreezes WHERE ChangeRequestType=N'EarlyResume'
            AND (ChangePayloadJson IS NULL OR ISJSON(ChangePayloadJson)<>1))
            THROW 51126, 'PT early-resume change has no valid confirmed payload.', 1;
        IF EXISTS(SELECT 1 FROM PTFreezes WHERE ChangeRequestType=N'EarlyResume' AND
            (TRY_CONVERT(date,JSON_VALUE(ChangePayloadJson,'$.ResumeDate')) IS NULL OR
             (ChangeResumeDate IS NOT NULL AND ChangeResumeDate<>TRY_CONVERT(date,JSON_VALUE(ChangePayloadJson,'$.ResumeDate')))))
            THROW 51126, 'Conflicting PT early-resume date and confirmed payload.', 1;
        UPDATE PTFreezes SET ChangeState=CASE WHEN ChangeRequestType IS NULL THEN N'None' ELSE ChangeRequestType+ChangeRequestStatus END;
        IF EXISTS(SELECT 1 FROM PackageComponents WHERE ComponentType=N'PT' AND
            ((MinFreezeDaysPerRequest IS NOT NULL AND (PTFreezeMinimumDays IS NULL OR PTFreezeMinimumDays<>MinFreezeDaysPerRequest))
             OR (MaxFreezeDaysPerRequest IS NOT NULL AND (PTFreezeMaxTotalDays IS NULL OR PTFreezeMaxTotalDays<>MaxFreezeDaysPerRequest))))
            THROW 51127, 'Conflicting PT component freeze limits; resolve before migration.', 1;
        UPDATE PackageComponents SET MinFreezeDaysPerRequest=COALESCE(MinFreezeDaysPerRequest,PTFreezeMinimumDays),
            MaxFreezeDaysPerRequest=COALESCE(MaxFreezeDaysPerRequest,PTFreezeMaxTotalDays) WHERE ComponentType=N'PT';

        IF EXISTS(SELECT 1 FROM LockerPricingPolicies WHERE RentalType NOT IN(N'Daily',N'Monthly',N'Quarterly',N'HalfYearly',N'Yearly'))
            THROW 51130, 'Unknown locker rental type; resolve before migration.', 1;
        UPDATE LockerPricingPolicies SET DurationMonths=CASE RentalType WHEN N'Daily' THEN 0 WHEN N'Quarterly' THEN 3
            WHEN N'HalfYearly' THEN 6 WHEN N'Yearly' THEN 12 ELSE 1 END WHERE DurationMonths IS NULL;
        IF EXISTS(SELECT 1 FROM LockerPricingPolicies WHERE RentalType<>CASE DurationMonths
            WHEN 0 THEN N'Daily' WHEN 3 THEN N'Quarterly' WHEN 6 THEN N'HalfYearly' WHEN 12 THEN N'Yearly' ELSE N'Monthly' END)
            THROW 51130, 'Locker policy type conflicts with duration; resolve before migration.', 1;
        IF EXISTS(SELECT 1 FROM LockerRentalContracts r JOIN LockerPricingPolicies p ON p.Id=r.LockerPricingPolicyId
            WHERE r.DepositAmount<>p.DepositAmount OR r.RentalPrice<>CASE WHEN p.DurationMonths=0 THEN 0 ELSE p.RentalPrice END
              OR (r.RentalTypeSnapshot IS NOT NULL AND r.RentalTypeSnapshot<>p.RentalType)
              OR (r.TermMonthsSnapshot IS NOT NULL AND r.TermMonthsSnapshot<>p.DurationMonths)
              OR (r.OverdueDailyFeeSnapshot IS NOT NULL AND r.OverdueDailyFeeSnapshot<>p.OverdueDailyFee))
            THROW 51131, 'Locker contract terms differ from their policy version; preserve the historical policy before migration.', 1;
        IF EXISTS(SELECT 1 FROM LockerRentalContracts WHERE SettledAt IS NOT NULL AND
            (ContractStatus NOT IN(N'Terminated',N'Abandoned') OR (CancelledAt IS NOT NULL AND CancelledAt<>SettledAt)))
            THROW 51132, 'Conflicting locker settlement timestamp; resolve before migration.', 1;
        UPDATE LockerRentalContracts SET CancelledAt=SettledAt WHERE SettledAt IS NOT NULL AND CancelledAt IS NULL;
        """;

    internal const string GuardDowngrade = """
        -- Retired historical fields cannot be reconstructed by filling defaults.
        IF EXISTS(SELECT 1 FROM TrainerProfiles) OR EXISTS(SELECT 1 FROM TrainerApplications)
          OR EXISTS(SELECT 1 FROM PTPurchases) OR EXISTS(SELECT 1 FROM PTAvailabilities)
          OR EXISTS(SELECT 1 FROM LockerRentalContracts) OR EXISTS(SELECT 1 FROM Lockers)
          OR EXISTS(SELECT 1 FROM PaymentTransactions) OR EXISTS(SELECT 1 FROM PackageComponents WHERE ComponentType=N'PT')
            THROW 51190, 'Cannot downgrade populated feature tables: restore a database backup to recover retired historical columns.', 1;
        """;
}
