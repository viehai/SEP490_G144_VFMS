# Trainer Recruitment and Profile API — revised 2026-11-07

Report 3 UC 3.1–3.4. Uses existing TrainerApplications, TrainerProfiles, ApplicationUsers, Notifications and AuditLogs. The five module-specific tables introduced in the first implementation are removed by a forward migration. See [schema decision](TRAINER_SCHEMA_DECISION.md).

## Routes

All paths below start with `/api/trainers`. Success returns the DTO directly; errors use ProblemDetails `code`/`detail`, or ASP.NET field-validation `errors`. Authorization middleware may return 401/403 without an application error code.

| Method / path | Access | Behavior |
|---|---|---|
| POST applications | Guest/Customer for Recruitment; PT for ProfileUpdate | Submit full application; returns 201 receipt with trackingCode/status/submissionAttempt |
| GET applications | Manager | Filter by applicationType, status, specialty, from/to, page/pageSize |
| GET applications/{id} | Manager | Application details and private certificate download URLs |
| GET me/applications | PT | Own submitted/hired applications, same filters; includes review result/history |
| POST applications/{id}/review | Manager | Recruitment: Interview/Hire/Reject; ProfileUpdate: Approve/Reject |
| POST applications/{id}/setup-email | Manager | Reissue onboarding link only for a pending hired account |
| POST setup-password | Guest | Redeem token, set password and activate pending account |
| GET / | Public | Search active trainers by search/specialty/gender/minimumRating/page/pageSize |
| GET {id} | Public | Published profile, certificates, 14-day availability, latest 20 visible approved reviews |
| GET me | PT | Own active published profile |
| PUT me | PT | Direct moderated bio/avatar update |
| GET applications/{id}/documents/{fileId} | Manager or owning PT | Private application file; Guest gets 401, unauthorized role 403, other PT 404 |
| GET {id}/documents/{fileId} | Public for active published certificate; otherwise Manager/owner | Certificate download |
| GET {id}/avatar | Public for active profile; otherwise Manager/owner | Current avatar download |

Old `credential-changes` and global `documents/{id}` routes are removed. Document IDs are strings and scoped to application/profile. Use `downloadUrl` returned with each certificate, rather than constructing the old route. Profile DTO now has `avatarUrl` rather than `avatarFileId`. All file responses are attachments with `nosniff` and `private, no-store`.

## Submission and review

SubmitTrainerApplicationRequest retains fullName, email, phoneNumber, citizenId, dateOfBirth, gender, specialty, experienceYears, bio, consent and certificates, and adds optional `applicationType` (default `Recruitment`). Each certificate is `{ "fileName": "proof.pdf", "content": "<base64>" }`.

Recruitment checks a rolling 30-day cooldown by email OR citizen ID. Submission creates no account. Manager records Interview before Hire and explicitly confirms identity (`identityVerified=true`, level, commissionTier). If contact details match an existing account, supply its verified existingUserId; ambiguity/locked accounts fail. Existing active accounts keep passwords, roles and refresh tokens and receive PT role/profile. Sign in again to obtain an access token with PT role. New accounts remain PendingVerification until setup. Repeated Hire returns the existing outcome without duplicates.

A PT resubmits using the same POST with `applicationType: "ProfileUpdate"`. Email must match their current account, citizen ID must match the original recruitment application linked by HiredUserId. Historical application email may differ after a verified existing-account hire by phone. Only one update may be pending. The 30-day recruitment cooldown does not apply to ProfileUpdate (explicit implementation decision). Application row stores the proposed specialty/certificates and its own review history. Manager sends `{ "action": "Approve", "note": "Verified" }` or Reject to the same review endpoint. Approval replaces only published specialty/certificates; does not create users/profiles or overwrite more recent direct bio/avatar edits. Rejection retains the published profile. A further update is a new application row.

## Limits and error codes

Implementation limits: name/email 256 chars, bio 4,000, specialty 200, review note 2,000, commissionTier 100; DOB from 1900 and before today; experience 0–80; gender Male/Female/Other; citizenId 12 digits; phone optional + then 9–15 digits. Page 1–10,000, pageSize 1–100; minimumRating 0–5. Hire accepts Junior/Senior/Master; old Expert remains readable. Consent required.

Certificates: 1–5 files, each <=5 MiB, PDF/JPEG/PNG extension + signature; avatar JPEG/PNG only. Whole request limit 36 MiB. Validation does not decode image dimensions or perform antivirus scanning. New files use private disk storage; metadata JSON contains no client-controlled storage path. Old binary files are retained as legacy base64 JSON during migration and remain downloadable. Legacy URL columns are retained for compatibility, not populated by new uploads.

| Error | HTTP |
|---|---|
| TRAINER_VALIDATION / TRAINER_CONTENT_REJECTED / TRAINER_SETUP_INVALID | 400 |
| TRAINER_FORBIDDEN | 403 |
| TRAINER_NOT_FOUND | 404 |
| RECRUITMENT_COOLDOWN / TRAINER_CHANGE_PENDING | 409 |
| TRAINER_IDENTITY_CONFLICT / TRAINER_PROFILE_EXISTS | 409 |
| TRAINER_INVALID_STATE / TRAINER_INTERVIEW_REQUIRED / TRAINER_CONFLICT | 409 |
| TRAINER_CONFIGURATION | 503 |

Submit/setup/resend share a per-IP quota of 10 requests/10 minutes (429). Public payloads exclude identity/contact details, email queue, token hashes and storage keys. Empty public search is 200 with empty items, not 404; no ratings means averageRating=0.

## Password setup, auth compatibility and audit

Only two nullable columns are added to ApplicationUsers: PasswordSetupTokenHash and PasswordSetupExpiresAt. Existing users get NULL. Login, register, refresh, logout DTOs/services and refresh-token table are unchanged. Setup validates hash+48-hour expiry and PendingVerification/non-banned status, sets the existing PasswordHash, activates the user and clears both setup fields. Reissue replaces the previous hash. Old/used links cannot reactivate locked or active accounts.

AuditLog records application submission (Guest actor null), Interview/Hire/Reject, ProfileUpdate Approve/Reject, profile update, resend and successful setup. Payloads contain operation and before/after states, linked actor/entity IDs and timestamp. No passwords/hashes/raw setup tokens, attachments or copied applicant PII. Review reasons stay on their application, not duplicated in the audit payload. This is an action log, not a full version history of every biography edit.

## Storage and email configuration

Set `TrainerFiles__Root` to an absolute persistent directory outside webroot. Default is `AppContext.BaseDirectory/App_Data/trainer-files` for local use; back it up together with SQL. Multiple API instances need the same private storage and shared Data Protection keys. Failed transactions remove newly written files; a process crash can leave orphan files for maintenance. Replaced avatar files are retained on disk, not automatically deleted.

Email delivery state lives in each application's EmailQueueJson. Body is encrypted with Data Protection (same purpose as legacy worker). Setup/reissue removes obsolete queued setup messages; audit records the operation. Worker sends one eligible message per 30-second tick under the module transaction lock to avoid losing concurrent queue edits. Failed sends retry with backoff. SMTP remains at-least-once; a crash after sending can duplicate delivery. Configure:

```text
TrainerMail__Enabled=true
TrainerMail__Host=<SMTP host>
TrainerMail__Port=587
TrainerMail__From=<sender>
TrainerMail__Username=<username>
TrainerMail__Password=<password>
TrainerMail__SetupPageUrl=https://<frontend>/auth/trainer-setup
TrainerFiles__Root=<persistent private directory>
```

STARTTLS required. Mail is disabled unless enabled; disabled/unconfigured delivery leaves messages queued. Setup frontend remains outside this backend scope; extract query token from email to test setup in Swagger. SMTP has not been verified against a real mailbox.

## Run and migration

```powershell
$env:ConnectionStrings__DefaultConnection='Server=localhost;Database=VfmsDb;Integrated Security=true;TrustServerCertificate=true'
dotnet ef database update --project src/VFMS.Infrastructure --startup-project src/VFMS.API
dotnet ef migrations has-pending-model-changes --project src/VFMS.Infrastructure --startup-project src/VFMS.API
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/VFMS.API --no-launch-profile --urls http://localhost:5188
```

Swagger: http://localhost:5188/swagger. The old SQL-login configuration did not authenticate locally; use an explicit connection environment variable. API startup uses the existing Gym/role/Admin seed; Admin is not automatically Manager.

Keep the previous migration in history. The new forward migration transfers old data before removing tables, and refuses unsupported orphan records rather than discarding them. Down is blocked; use backup/restore or a forward correction. Never remove migration history entries manually. Populated-legacy migration tests and auth regression are required before deployment.

SQL integration tests use `VFMS_TEST_SQL` with a SQL Server connection permitted to create/drop uniquely named disposable test databases. Test databases are separate from VfmsDb.
