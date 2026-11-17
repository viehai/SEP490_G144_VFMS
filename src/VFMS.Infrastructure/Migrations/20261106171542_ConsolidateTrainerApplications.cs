using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateTrainerApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hold source and destination locks through EF's migration transaction.
            migrationBuilder.Sql("""
                IF @@TRANCOUNT = 0 THROW 51020, 'Consolidation requires a transaction; do not use --no-transactions.', 1;
                DECLARE @lockResult int, @rows bigint;
                EXEC @lockResult = sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment',
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
                IF @lockResult < 0 THROW 51020, 'Trainer consolidation could not acquire its transaction lock.', 1;
                SELECT @rows = COUNT_BIG(*) FROM TrainerApplications WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerProfiles WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM ApplicationUsers WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerDocument WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerCredentialChange WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerRecruitmentRestriction WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerSetupToken WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM TrainerEmail WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM AuditLogs WITH (TABLOCKX, HOLDLOCK);
                SELECT @rows = COUNT_BIG(*) FROM Notifications WITH (TABLOCKX, HOLDLOCK);
                """);

            migrationBuilder.AddColumn<string>(
                name: "AvatarJson",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificatesJson",
                table: "TrainerProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationType",
                table: "TrainerApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Recruitment");

            migrationBuilder.AddColumn<string>(
                name: "CertificatesJson",
                table: "TrainerApplications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "EmailQueueJson",
                table: "TrainerApplications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IsRecruitmentRestricted",
                table: "TrainerApplications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestrictionChangedAt",
                table: "TrainerApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionChangedByUserId",
                table: "TrainerApplications",
                type: "nvarchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionReason",
                table: "TrainerApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordSetupExpiresAt",
                table: "ApplicationUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordSetupTokenHash",
                table: "ApplicationUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME(), @bad int, @message nvarchar(2048);
                DECLARE @changeMap TABLE (OldId int PRIMARY KEY, SourceId int NOT NULL, NewId int NULL);
                INSERT @changeMap (OldId, SourceId)
                SELECT c.Id, a.Id
                FROM TrainerCredentialChange c
                JOIN TrainerProfiles p ON p.Id = c.TrainerProfileId
                CROSS APPLY (
                    SELECT TOP (1) a.Id FROM TrainerApplications a
                    WHERE a.HiredUserId = p.PTUserId AND a.ApplicationStatus = 'Hired'
                      AND a.ApplicationType = 'Recruitment'
                    ORDER BY a.SubmittedAt DESC, a.Id DESC
                ) a;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT c.Id FROM TrainerCredentialChange c WHERE NOT EXISTS (SELECT 1 FROM @changeMap m WHERE m.OldId = c.Id)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Credential change has no originating hired application for its profile account; TrainerCredentialChange.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT Id FROM TrainerCredentialChange WHERE Status NOT IN ('Approved', 'Rejected', 'Pending')) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Unsupported credential status; TrainerCredentialChange.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT c.Id FROM TrainerCredentialChange c JOIN TrainerApplications a ON a.TrackingCode = CONCAT('LEGACY-CHANGE-', c.Id)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Legacy credential tracking code already exists; TrainerCredentialChange.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT MIN(c.Id) AS Id FROM TrainerCredentialChange c JOIN TrainerProfiles p ON p.Id = c.TrainerProfileId WHERE c.Status = 'Pending' AND c.IsDeleted = 0 GROUP BY p.PTUserId HAVING COUNT(*) > 1) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Multiple pending credential changes for one account; first TrainerCredentialChange.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT r.Id FROM TrainerRecruitmentRestriction r WHERE NOT EXISTS (SELECT 1 FROM TrainerApplications a WHERE a.ApplicationType = 'Recruitment' AND a.Email = r.Email AND a.CitizenId = r.CitizenId)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Restriction has no application matching BOTH email and citizen ID; TrainerRecruitmentRestriction.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT r.Id FROM TrainerRecruitmentRestriction r WHERE r.IsActive = 1 AND r.IsDeleted = 0 AND NOT EXISTS (SELECT 1 FROM TrainerApplications a WHERE a.ApplicationType = 'Recruitment' AND a.Email = r.Email AND a.CitizenId = r.CitizenId AND a.IsDeleted = 0)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Active restriction only matches deleted applications; TrainerRecruitmentRestriction.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT t.Id FROM TrainerSetupToken t WHERE NOT EXISTS (SELECT 1 FROM ApplicationUsers u WHERE u.UserId = t.UserId)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Setup token has no account; TrainerSetupToken.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT p.Id FROM TrainerProfiles p LEFT JOIN TrainerDocument d ON d.Id = p.AvatarFileId WHERE p.AvatarFileId IS NOT NULL AND (d.Id IS NULL OR d.IsAvatar = 0 OR (d.TrainerProfileId IS NOT NULL AND d.TrainerProfileId <> p.Id) OR d.TrainerCredentialChangeId IS NOT NULL)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Avatar pointer is missing or has conflicting ownership/type; TrainerProfiles.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT MIN(Id) AS Id FROM TrainerProfiles WHERE AvatarFileId IS NOT NULL GROUP BY AvatarFileId HAVING COUNT(*) > 1) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'An avatar is referenced by multiple profiles; first TrainerProfiles.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT d.Id FROM TrainerDocument d WHERE (d.TrainerApplicationId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM TrainerApplications a WHERE a.Id = d.TrainerApplicationId)) OR (d.TrainerProfileId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM TrainerProfiles p WHERE p.Id = d.TrainerProfileId)) OR (d.TrainerCredentialChangeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM TrainerCredentialChange c WHERE c.Id = d.TrainerCredentialChangeId))) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Document has a dangling owner reference; TrainerDocument.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT d.Id FROM TrainerDocument d LEFT JOIN TrainerCredentialChange c ON c.Id = d.TrainerCredentialChangeId WHERE (d.TrainerApplicationId IS NOT NULL AND d.TrainerCredentialChangeId IS NOT NULL) OR (d.TrainerProfileId IS NOT NULL AND c.TrainerProfileId <> d.TrainerProfileId)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Document has conflicting application/change/profile ownership; TrainerDocument.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT a.Id FROM AuditLogs a WHERE a.AuditEntity = 'TrainerCredentialChange' AND (a.EntityId IS NULL OR NOT EXISTS (SELECT 1 FROM @changeMap m WHERE m.OldId = a.EntityId))) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Audit credential reference cannot be remapped; AuditLogs.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT n.Id FROM Notifications n WHERE n.EntityType = 'TrainerCredentialChange' AND (n.EntityId IS NULL OR NOT EXISTS (SELECT 1 FROM @changeMap m WHERE m.OldId = n.EntityId))) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Notification credential reference cannot be remapped; Notifications.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                DECLARE @emailMap TABLE (OldId int PRIMARY KEY, ApplicationId int NOT NULL);
                INSERT @emailMap (OldId, ApplicationId)
                SELECT e.Id, a.Id FROM TrainerEmail e
                CROSS APPLY (
                    SELECT TOP (1) a.Id FROM TrainerApplications a
                    LEFT JOIN ApplicationUsers u ON u.UserId = a.HiredUserId
                    WHERE a.ApplicationType = 'Recruitment' AND (a.Email = e.Recipient OR u.Email = e.Recipient)
                      AND (e.IsDeleted = 1 OR e.SentAt IS NOT NULL OR a.IsDeleted = 0)
                    ORDER BY CASE WHEN a.SubmittedAt <= e.CreatedAt THEN 0 ELSE 1 END,
                             a.SubmittedAt DESC, a.Id DESC
                ) a;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT e.Id FROM TrainerEmail e WHERE NOT EXISTS (SELECT 1 FROM @emailMap m WHERE m.OldId = e.Id)) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Email has no matching applicant/hired-account email (live unsent mail requires a live application); TrainerEmail.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                DECLARE @documentMap TABLE (OldId int PRIMARY KEY, ProfileId int NULL);
                INSERT @documentMap (OldId, ProfileId)
                SELECT d.Id, COALESCE(d.TrainerProfileId, c.TrainerProfileId, avatarProfile.Id, publicProfile.Id)
                FROM TrainerDocument d
                LEFT JOIN TrainerCredentialChange c ON c.Id = d.TrainerCredentialChangeId
                LEFT JOIN TrainerProfiles avatarProfile ON avatarProfile.AvatarFileId = d.Id
                OUTER APPLY (
                    SELECT TOP (1) p.Id FROM TrainerProfiles p
                    JOIN TrainerApplications a ON a.HiredUserId = p.PTUserId
                    WHERE a.Id = d.TrainerApplicationId AND d.IsPublic = 1
                    ORDER BY p.Id DESC
                ) publicProfile;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT d.Id FROM TrainerDocument d JOIN @documentMap m ON m.OldId = d.Id WHERE d.TrainerApplicationId IS NULL AND d.TrainerCredentialChangeId IS NULL AND m.ProfileId IS NULL) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Document has no supported application/profile/change/avatar owner; TrainerDocument.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT d.Id FROM TrainerDocument d JOIN @documentMap m ON m.OldId = d.Id WHERE d.IsPublic = 1 AND m.ProfileId IS NULL) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Public document has no profile destination; TrainerDocument.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                SET @bad = NULL;
                SELECT TOP (1) @bad = problem.Id FROM (SELECT d.Id FROM TrainerDocument d JOIN @documentMap m ON m.OldId = d.Id JOIN TrainerProfiles p ON p.Id = m.ProfileId JOIN TrainerApplications a ON a.Id = d.TrainerApplicationId WHERE a.HiredUserId IS NULL OR a.HiredUserId <> p.PTUserId) problem ORDER BY problem.Id;
                IF @bad IS NOT NULL
                BEGIN
                    SET @message = CONCAT(N'Document application and profile belong to different accounts; TrainerDocument.Id=', @bad, N'. Resolve the legacy row and retry; no source tables were dropped.');
                    THROW 51021, @message, 1;
                END;

                -- Keep every credential change, including deleted changes, as a reviewable application.
                INSERT TrainerApplications
                    (ApplicationType, CertificatesJson, EmailQueueJson, IsRecruitmentRestricted,
                     TrackingCode, ApplicantUserId, HiredUserId, FullName, Email, PhoneNumber, CitizenId,
                     ApplicationFileUrl, ApplicationStatus, SubmittedAt, SubmissionAttempt,
                     ReviewedByUserId, ReviewedAt, ReviewNote, DateOfBirth, Gender, Specialty,
                     ExperienceYears, Bio, ConsentAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT 'ProfileUpdate', '[]', '[]', 0,
                       CONCAT('LEGACY-CHANGE-', c.Id), p.PTUserId, p.PTUserId,
                       a.FullName, a.Email, a.PhoneNumber, a.CitizenId, a.ApplicationFileUrl,
                       CASE c.Status WHEN 'Approved' THEN 'Approved' WHEN 'Rejected' THEN 'Rejected' ELSE 'Pending' END,
                       c.CreatedAt, 1, c.ReviewedByUserId, c.ReviewedAt, c.ReviewNote,
                       a.DateOfBirth, p.Gender, c.Specialty, p.ExperienceYears, COALESCE(p.Bio, ''), a.ConsentAt,
                       c.CreatedAt, c.UpdatedAt, c.IsDeleted
                FROM TrainerCredentialChange c
                JOIN @changeMap m ON m.OldId = c.Id
                JOIN TrainerApplications a ON a.Id = m.SourceId
                JOIN TrainerProfiles p ON p.Id = c.TrainerProfileId;

                UPDATE m SET NewId = a.Id FROM @changeMap m
                JOIN TrainerApplications a ON a.TrackingCode = CONCAT('LEGACY-CHANGE-', m.OldId);
                IF EXISTS (SELECT 1 FROM @changeMap WHERE NewId IS NULL)
                    THROW 51022, 'Credential application transfer was incomplete.', 1;

                -- FOR JSON encodes varbinary as base64; IDs remain strings, including legacy numeric IDs.
                UPDATE a SET CertificatesJson = COALESCE((
                    SELECT CONVERT(varchar(11), d.Id) AS Id, d.FileName, d.ContentType,
                           '' AS StorageKey, d.Content AS LegacyContentBase64,
                           CAST(0 AS bit) AS IsPublic, d.IsAvatar, d.IsDeleted
                    FROM TrainerDocument d
                    LEFT JOIN @changeMap m ON m.OldId = d.TrainerCredentialChangeId
                    WHERE d.TrainerApplicationId = a.Id OR m.NewId = a.Id
                    ORDER BY d.Id FOR JSON PATH
                ), '[]') FROM TrainerApplications a;

                -- Preserve old, private and deleted profile documents without publishing pending changes or old avatars.
                UPDATE p SET CertificatesJson = COALESCE((
                    SELECT CONVERT(varchar(11), d.Id) AS Id, d.FileName, d.ContentType,
                           '' AS StorageKey, d.Content AS LegacyContentBase64,
                           CAST(CASE WHEN d.IsPublic = 1 AND d.IsDeleted = 0 AND p.IsDeleted = 0
                                     AND d.IsAvatar = 0 AND (c.Id IS NULL OR (c.Status = 'Approved' AND c.IsDeleted = 0))
                                THEN 1 ELSE 0 END AS bit) AS IsPublic, d.IsAvatar, d.IsDeleted
                    FROM TrainerDocument d
                    JOIN @documentMap m ON m.OldId = d.Id
                    LEFT JOIN TrainerCredentialChange c ON c.Id = d.TrainerCredentialChangeId
                    WHERE m.ProfileId = p.Id
                    ORDER BY d.Id FOR JSON PATH
                ), '[]'), AvatarJson = NULLIF((
                    SELECT CONVERT(varchar(11), d.Id) AS Id, d.FileName, d.ContentType,
                           '' AS StorageKey, d.Content AS LegacyContentBase64,
                           d.IsPublic, d.IsAvatar, d.IsDeleted
                    FROM TrainerDocument d WHERE d.Id = p.AvatarFileId AND d.IsDeleted = 0
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                ), '') FROM TrainerProfiles p;

                -- Exact identity pair only; preserve inactive reasons too. Deleted restrictions stay inactive.
                UPDATE a SET IsRecruitmentRestricted = CASE WHEN r.IsDeleted = 0 THEN r.IsActive ELSE 0 END,
                             RestrictionReason = r.Reason, RestrictionChangedByUserId = r.ChangedByUserId,
                             RestrictionChangedAt = r.UpdatedAt
                FROM TrainerApplications a
                JOIN TrainerRecruitmentRestriction r ON r.Email = a.Email AND r.CitizenId = a.CitizenId
                WHERE a.ApplicationType = 'Recruitment';

                UPDATE a SET EmailQueueJson = COALESCE((
                    SELECT CONVERT(varchar(11), e.Id) AS Id, e.Recipient, e.Subject, e.ProtectedBody,
                           e.Attempts, e.NextAttemptAt, e.SentAt, e.IsDeleted
                    FROM TrainerEmail e JOIN @emailMap m ON m.OldId = e.Id
                    WHERE m.ApplicationId = a.Id
                    ORDER BY e.Id FOR JSON PATH, INCLUDE_NULL_VALUES
                ), '[]') FROM TrainerApplications a;

                -- Only the latest unused, unexpired, nondeleted token survives; older tokens are intentionally invalidated.
                UPDATE u SET PasswordSetupTokenHash = t.TokenHash, PasswordSetupExpiresAt = t.ExpiresAt
                FROM ApplicationUsers u CROSS APPLY (
                    SELECT TOP (1) t.TokenHash, t.ExpiresAt FROM TrainerSetupToken t
                    WHERE t.UserId = u.UserId AND t.IsDeleted = 0 AND t.UsedAt IS NULL AND t.ExpiresAt > @now
                    ORDER BY t.CreatedAt DESC, t.Id DESC
                ) t;

                -- Remap references without rewriting historical action/value/timestamp fields.
                UPDATE a SET AuditEntity = 'TrainerApplication', EntityId = m.NewId
                FROM AuditLogs a JOIN @changeMap m ON m.OldId = a.EntityId
                WHERE a.AuditEntity = 'TrainerCredentialChange';
                UPDATE n SET EntityType = 'TrainerApplication', EntityId = m.NewId
                FROM Notifications n JOIN @changeMap m ON m.OldId = n.EntityId
                WHERE n.EntityType = 'TrainerCredentialChange';

                -- Archive only source IDs, states and timestamps; sensitive payloads stay in their owning records.
                -- IsDeleted in the destination JSON keeps deleted files private and deleted mail out of delivery.

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerApplication', m.NewId,
                       'ConsolidateTrainerApplications: legacy TrainerCredentialChange',
                       (SELECT s.Id, s.TrainerProfileId, s.Status, s.ReviewedAt, s.CreatedAt, s.UpdatedAt, s.IsDeleted FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerCredentialChange s JOIN @changeMap m ON m.OldId = s.Id;

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerRecruitmentRestriction', s.Id,
                       'ConsolidateTrainerApplications: legacy TrainerRecruitmentRestriction',
                       (SELECT s.Id, s.IsActive, s.CreatedAt, s.UpdatedAt, s.IsDeleted FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerRecruitmentRestriction s ;

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerSetupToken', s.Id,
                       'ConsolidateTrainerApplications: legacy TrainerSetupToken',
                       (SELECT s.Id, s.ExpiresAt, s.UsedAt, s.CreatedAt, s.UpdatedAt, s.IsDeleted FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerSetupToken s ;

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerApplication', m.ApplicationId,
                       'ConsolidateTrainerApplications: legacy TrainerEmail',
                       (SELECT s.Id, s.Attempts, s.NextAttemptAt, s.SentAt, s.CreatedAt, s.UpdatedAt, s.IsDeleted FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerEmail s JOIN @emailMap m ON m.OldId = s.Id;

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerDocument', s.Id,
                       'ConsolidateTrainerApplications: legacy TrainerDocument',
                       (SELECT s.Id, s.TrainerApplicationId, s.TrainerProfileId, s.TrainerCredentialChangeId, s.IsPublic, s.IsAvatar, s.CreatedAt, s.UpdatedAt, s.IsDeleted FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerDocument s ;

                INSERT AuditLogs (ActorUserId, ActionType, AuditEntity, EntityId, OldValue, NewValue,
                                 OccurredAt, CreatedAt, UpdatedAt, IsDeleted)
                SELECT NULL, 'Create', 'TrainerProfile', p.Id,
                       'ConsolidateTrainerApplications: legacy AvatarFileId',
                       (SELECT p.Id, p.AvatarFileId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                       @now, @now, @now, 0
                FROM TrainerProfiles p WHERE p.AvatarFileId IS NOT NULL;

                -- Check coverage before any destructive DDL; JSON arrays can contain duplicate copies but never lose a source ID.
                IF EXISTS (
                    SELECT 1 FROM TrainerDocument d WHERE NOT EXISTS (
                        SELECT 1 FROM TrainerApplications a CROSS APPLY OPENJSON(a.CertificatesJson)
                            WITH (Id nvarchar(40) '$.Id', LegacyContentBase64 nvarchar(max) '$.LegacyContentBase64') j
                        WHERE j.Id = CONVERT(varchar(11), d.Id) AND j.LegacyContentBase64 IS NOT NULL
                    ) AND NOT EXISTS (
                        SELECT 1 FROM TrainerProfiles p CROSS APPLY OPENJSON(p.CertificatesJson)
                            WITH (Id nvarchar(40) '$.Id', LegacyContentBase64 nvarchar(max) '$.LegacyContentBase64') j
                        WHERE j.Id = CONVERT(varchar(11), d.Id) AND j.LegacyContentBase64 IS NOT NULL
                    )
                ) THROW 51022, 'Document transfer coverage check failed; source tables retained.', 1;
                IF (SELECT COUNT_BIG(*) FROM TrainerEmail) <>
                   (SELECT COUNT_BIG(*) FROM TrainerApplications a CROSS APPLY OPENJSON(a.EmailQueueJson))
                    THROW 51022, 'Email transfer coverage check failed; source tables retained.', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_PendingProfileUpdate",
                table: "TrainerApplications",
                column: "ApplicantUserId",
                unique: true,
                filter: "[ApplicationType] = 'ProfileUpdate' AND [ApplicationStatus] = 'Pending' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_RestrictionChangedByUserId",
                table: "TrainerApplications",
                column: "RestrictionChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_PasswordSetupTokenHash",
                table: "ApplicationUsers",
                column: "PasswordSetupTokenHash",
                unique: true,
                filter: "[PasswordSetupTokenHash] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerApplications_ApplicationUsers_RestrictionChangedByUserId",
                table: "TrainerApplications",
                column: "RestrictionChangedByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            // Destructive DDL is last, after all mappings, archives and coverage checks succeed.
            migrationBuilder.DropTable(name: "TrainerDocument");
            migrationBuilder.DropTable(name: "TrainerEmail");
            migrationBuilder.DropTable(name: "TrainerRecruitmentRestriction");
            migrationBuilder.DropTable(name: "TrainerSetupToken");
            migrationBuilder.DropTable(name: "TrainerCredentialChange");
            migrationBuilder.DropColumn(name: "AvatarFileId", table: "TrainerProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("THROW 51020, 'ConsolidateTrainerApplications is forward-only. Restore a pre-migration backup to roll back.', 1;");
        }
    }
}
