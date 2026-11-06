# Trainer Recruitment and Profile API

Scope: Report 3 sections 3.1–3.4 and GBR-01–05, GBR-96/97. This backend implements recruitment and profiles, not PT booking/payment or frontend screens.

## Contract decisions

These are implementation limits where the SRS did not give a value: name/email 256 chars, bio 4,000, specialty 200, review/restriction note 2,000, commission tier 100; DOB from 1900 and before today; experience 0–80 years; gender Male/Female/Other; CCCD exactly 12 digits; phone optional leading + and 9–15 digits. Pages 1–10,000, size 1–100. Applicant consent is mandatory. API uses Pending, Interviewing, Hired, Rejected; old enum values remain readable for legacy data. New hires accept Junior/Senior/Master; legacy Expert remains supported in stored profiles.

Files use JSON `content` as base64, 1–5 certificates per submission, each <=5 MiB. Names cannot contain path separators. Extension and signature must match PDF/JPEG/PNG; avatars permit JPEG/PNG only. Request limit 36 MiB. SQL Server stores the binary in a separate table; list queries never return binary content. This avoids requiring Cloudinary credentials for the initial backend; replacement storage can be added later. Files are returned as downloads with nosniff and no-store. File signature validation is not antivirus scanning.

Application attachments are private to Manager; profile owner can inspect their credential-change files. Hiring copies verified certificates into the public profile; pending/rejected changes never replace published credentials. Public API never returns applicant CCCD, DOB, email, phone, commission tier or setup tokens. The 14-day availability response is only a display window and contains no member identities.

## Routes

All routes start with `/api/trainers`. JSON DTO definitions are in `VFMS.Contracts/Trainers/TrainerContracts.cs`. Successful bodies are DTOs (not a Result wrapper); failures use ProblemDetails with `code`. Input-model validation uses the existing ASP.NET validation response with field errors.

| Method/path | Access | Request / response |
|---|---|---|
| POST applications | Guest or Customer | SubmitTrainerApplicationRequest → 201 TrainerApplicationReceipt |
| GET applications | Manager | TrainerApplicationFilter → paged TrainerApplicationResponse |
| GET applications/{id} | Manager | TrainerApplicationResponse including certificate IDs |
| POST applications/{id}/review | Manager | ReviewTrainerApplicationRequest → TrainerApplicationResponse |
| POST applications/{id}/setup-email | Manager | Reissue expired/missing onboarding link for a pending hired account |
| PUT recruitment-restrictions | Manager | RecruitmentRestrictionRequest → boolean |
| POST setup-password | Guest | TrainerSetupRequest → boolean; single-use activation |
| GET / | Public | TrainerSearch → paged TrainerProfileResponse |
| GET {id} | Public | TrainerPublicDetail: profile, verified certificate IDs, availability, latest 20 visible approved reviews |
| GET me | PT | Own TrainerPublicDetail |
| PUT me | PT | UpdateTrainerProfileRequest → boolean |
| POST me/credential-changes | PT | TrainerCredentialRequest → 201 change ID |
| GET credential-changes | PT or Manager | page/pageSize; PT gets own changes, Manager gets all |
| POST credential-changes/{id}/review | Manager | ReviewTrainerCredentialRequest → boolean |
| GET documents/{id} | Public for published files; otherwise owner/Manager as applicable | File download; inaccessible IDs return 404 |

## Workflow and error handling

Application submission checks email OR CCCD cooldown over a rolling 30 days and independent recruitment restrictions. It does not create an account. Save application, documents and receipt email in one transaction. Manager records Interview before Hire, or rejects with a note. Hire requires `identityVerified=true`, level and commission tier. If contact information matches an existing active account, explicitly pass its `existingUserId`; ambiguous matches fail rather than merge. Preserve existing roles/history. Repeated Hire returns the recorded result. Blocked/inactive accounts are not reactivated by hiring.

New users get PendingVerification and an unguessable password hash until they redeem a 24-hour, single-use setup token. Store only the token hash; encrypt the email body with ASP.NET Data Protection. Setup cannot unlock administrative account locks. Reissue invalidates old tokens. Public profile queries require both an active profile and an active, non-banned account.

Credential changes replace the approved specialty/certificate set only after Manager approval. One pending request per profile is enforced by a filtered unique index. Approval rechecks banned words. Review and profile mutations record audit metadata; notifications target the profile owner or Managers. Email sends are queued and retried rather than performed inside recruitment transactions.

| Code | HTTP | Meaning |
|---|---|---|
| TRAINER_VALIDATION | 400 | Invalid fields, filters or file |
| TRAINER_CONTENT_REJECTED | 400 | Banned content |
| TRAINER_SETUP_INVALID | 400 | Expired/used/invalid token or ineligible account |
| TRAINER_FORBIDDEN | 403 | Current database role does not authorize action |
| RECRUITMENT_RESTRICTED | 403/409 | Recruitment restriction prevents submission/hiring |
| TRAINER_NOT_FOUND | 404 | Missing or inaccessible record |
| RECRUITMENT_COOLDOWN | 409 | Submission within 30 days; message gives next date |
| TRAINER_IDENTITY_CONFLICT | 409 | Identity requires resolution |
| TRAINER_INVALID_STATE / TRAINER_INTERVIEW_REQUIRED | 409 | Invalid state transition |
| TRAINER_PROFILE_EXISTS / TRAINER_CHANGE_PENDING | 409 | Duplicate profile/pending change |
| TRAINER_CONFLICT | 409 | Concurrent/data conflict |
| TRAINER_CONFIGURATION | 503 | Gym/PT role missing |

Submission, setup and resend have per-IP rate limits: 10 requests per 10 minutes, 429 on excess. Deploy behind a trusted proxy with correct IP handling; do not trust arbitrary forwarded headers. Writes are serialized by a transaction-owned SQL application lock for this small recruitment module; no external email calls occur under that lock.

## Email configuration

Set environment variables (no secrets committed):

```text
TrainerMail__Enabled=true
TrainerMail__Host=<SMTP host>
TrainerMail__Port=587
TrainerMail__From=<sender mailbox>
TrainerMail__Username=<SMTP username>
TrainerMail__Password=<SMTP password>
TrainerMail__SetupPageUrl=https://<frontend>/auth/trainer-setup
```

SMTP requires STARTTLS. SetupPageUrl is a frontend URL to be implemented by the client; it receives `token` and submits token/password to POST setup-password. Backend does not include that screen. With mail disabled/unconfigured, records remain queued and onboarding is not delivered. Persist/share the Data Protection key ring across restarts/instances, or queued bodies cannot be decrypted. Retries use bounded exponential delay; SMTP is at-least-once (a crash after delivery but before commit can repeat an email). Stable Message-Id supports deduplication; exactly-once external email is not promised.

## Migration

Migration `20261106152017_TrainerRecruitmentAndProfiles` changes ApplicantUserId from unique to non-unique, backfills unique TrackingCode for old rows, adds profile/application attributes and five supporting tables. No user/application rows are deleted. A preflight rejects oversized existing bio/commission/review text before narrowing columns. Review those rows manually rather than truncate.

EF design-time factory avoids starting API/seed during scaffolding. For an actual database operation set `ConnectionStrings__DefaultConnection` explicitly; do not rely on the factory's integrated-auth fallback.

```powershell
$env:ConnectionStrings__DefaultConnection='Server=localhost;Database=VfmsDb;Integrated Security=true;TrustServerCertificate=true'
dotnet ef database update --project src/VFMS.Infrastructure --startup-project src/VFMS.API
dotnet ef migrations has-pending-model-changes --project src/VFMS.Infrastructure --startup-project src/VFMS.API
```

Down migration drops new module data and attempts to restore the old unique applicant relationship. It cannot safely roll back after repeat applications have been created; use backup/restore or a forward fix for populated deployments.

## Verification

Unit tests cover validation and file boundaries. Model test checks non-unique applicant FK and tracking uniqueness. Optional SQL test (`VFMS_TEST_SQL`) creates a uniquely named disposable database, migrates old rows, exercises hiring/setup/profile approval and privacy, and drops only that test database. SMTP and a frontend setup page require environment-specific verification.

Verified locally on 2026-11-06: 40 tests passed (20 unit, 17 client regression, 3 integration), including the SQL workflow. Migration applied to localhost/VfmsDb using Windows authentication. The existing SQL-login configuration could not authenticate; set the connection environment variable above in the shell used to run the API. No connection secrets were changed or committed.

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/VFMS.API --no-launch-profile --urls http://localhost:5188
# Swagger: http://localhost:5188/swagger
```

HTTP smoke checks: public search 200; invalid pagination/submission 400; anonymous Manager/PT endpoints 401; inaccessible document 404. Startup uses the existing seed for Gym, roles and Admin. Assign Manager through the existing account-management flow before testing Manager endpoints; Admin does not automatically count as Manager.
