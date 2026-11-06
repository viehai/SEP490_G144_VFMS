# Trainer schema simplification — 2026-11-07

User decision: no additional tables for module 3. A trainer resubmits an application rather than creating a separate credential-change request. Keep the existing TrainerApplication, TrainerProfile, ApplicationUser, Notification and AuditLog tables.

## Data ownership

- TrainerApplication: immutable submission history (Recruitment or ProfileUpdate), certificate metadata JSON, encrypted email queue JSON, recruitment restriction fields. Each resubmission creates a new row. Existing approved profile remains published until an update is approved.
- TrainerProfile: published certificate metadata JSON and current avatar metadata JSON. New files live under private configured disk storage, never directly under wwwroot. File routes authorize the owning application/profile before loading bytes.
- ApplicationUser: nullable PasswordSetupTokenHash and PasswordSetupExpiresAt, used solely for pending trainer onboarding; clear both on successful setup. Existing login/register/refresh/logout services and contracts remain unchanged. Existing active accounts retain passwords, roles and refresh tokens when hired.
- AuditLog: actor, entity ID, operation and before/after states. Do not persist raw tokens, password/hash, file bytes, applicant identity or biography in audit payloads. Guests have no account actor; application submission timestamps and tracking code provide their submission history.

## Explicit rules and limits

- Recruitment keeps the 30-day email OR citizen-ID cooldown. ProfileUpdate allows one pending submission per PT; the 30-day recruitment rule is not applied to updates. This is an implementation decision where the user has not separately specified an update interval.
- Restriction management requires an existing application with the exact email/citizen-ID pair; no synthetic applications and no pre-application blacklist entries. Enforce active restrictions across history on submission/hiring.
- ProfileUpdate approval only publishes specialty and certificates; does not change account identity, password, level, commission, or overwrite a more recent direct bio/avatar edit.
- JSON simplifies the table count but makes email/file reporting less convenient. Disk storage must be persistent/backed up and shared if multiple API instances are deployed. SQL and filesystem changes cannot share a transaction: failed writes clean up their new files; process crashes may leave orphan files for maintenance.

## Forward migration

Keep the already-published migration unchanged. Add a new forward migration, transfer existing files/requests/restrictions/email/token data, then drop the five module-specific tables. Existing binary attachments are retained as legacy base64 JSON to avoid requiring filesystem access inside EF migration; new uploads use disk only. Unsupported orphan data must stop migration explicitly rather than be silently deleted. Down is deliberately blocked because restoring old application/update identities after new writes cannot be made lossless automatically.

## Execution and verification

1. Update SRS, changed-section report, API/Swagger instructions and guidelines.
2. Update entities/mapping, service, authorized file routes and mail worker; remove credential-change API.
3. Add migration and tests for populated legacy data, profile resubmission, private/public files, setup/reissue and existing auth regression.
4. Apply locally after tests, check no pending EF model changes, commit and push only loc.
