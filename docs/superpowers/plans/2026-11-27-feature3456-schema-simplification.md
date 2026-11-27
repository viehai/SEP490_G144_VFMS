# Feature 3–6 schema simplification implementation plan

> **For agentic workers:** Implement the approved scope below with regression tests and a final independent review. Commit only when requested by the user; do not run a migration against the application database.

**Goal:** Remove duplicate/dead persistence while retaining behavior and public contracts inside and outside features 3–6.

**Architecture:** Keep existing tables. Resolve shared identity through ApplicationUser/PhysicalProfile, purchased terms through immutable OrderItem snapshots, and locker prices through the referenced immutable policy version. Keep per-payment checkout/quote/refund facts. Retain old DTO names as projections where necessary.

**Tech stack:** .NET 8, EF Core 8, SQL Server, Blazor; xUnit/SQLite and isolated SQL test databases.

**Spec:** The user approved the schema audit in this chat and instructed implementation on 2026-11-27, explicitly requiring that other features keep working.

## Global constraints

- Preserve the existing applicant name filter and staff eligibility changes; commit these separately first with the user-selected message.
- No push, shared-database migration execution, new tables, payment deadline changes, attendance accounting changes, or Gym/Yoga policy changes.
- Source columns on shared tables remain available to their existing consumers. Remove a duplicate only after moving every caller.
- Keep Notification and GymAnnouncement persistence unchanged; announcements have no implemented runtime service/UI yet.
- Migration must reject conflicting, unmappable historical data rather than silently discard or fabricate it. Do not edit historical migrations.
- Do not remove MemberMembershipId, attendance evidence, actual session trainer, original requested freeze dates, payment quote/deadline/refund facts, locker occupancy dates, or declared zone capacity.

## Review focus

- Legacy PT purchases missing Order/OrderItem or snapshots; migration resolves an existing valid link or stops.
- Avatar URLs with two nonempty conflicting sources; retained upload metadata and original access checks; URL rendering in non-trainer pages.
- Nullable physical profiles and AI survey fields; no wiping unrelated survey data or Gym/Yoga fields.
- Prices before and after voucher choice, cash before payment creation, late/duplicate transfers and partial deposit returns.
- Pending cancellation statuses retain occupied intervals and credit accounting; preserve unsupported legacy double-session handling.

## Tasks

- [x] 0. Commit the eight pre-existing trainer changes after focused checks.
- [x] 1. Add persistence shape/behavior regressions and observe failures against the current model. Record baseline suite results.
- [x] 2. PT and component terms: remove purchase duplicates/dead fields, session member/proposal duplicates, freeze duplicates, and availability duplicates. Keep public DTO fields projected from canonical data; merge change type/status into one enum and cancellation type into session status. Move PT component minimum/maximum onto the existing generic component limits, scoped to PT only. Update dashboard, feedback and staff-order consumers.
- [x] 3. Trainer identity: canonical ApplicationUser avatar URL, PhysicalProfile gender, one readiness transition date; retire old attachment/commission fields and accidental profile FKs. Preserve file access, existing metadata and recruitment snapshots. Update account/client URL handling without changing other profile behavior.
- [x] 4. Lockers: use the linked policy version for initial terms/prices; retain quote facts on each payment, closed timestamps and history. Retire dead catalog/fee/email fields; derive policy rental type and template hour end. Update voucher and reporting consumers as required. Do not reinterpret zone capacity or incident history.
- [x] 5. Shared payment owner: resolve UserId through Order.PurchaserUserId and remove dead KeyReplacementFee. Preserve source payment links, refunds, ownership filters and external payment data.
- [x] 6. Scaffold one new migration and snapshot; add transactional preflight/backfill guards, preserving valid historical values before drops. Add migration tests against isolated databases only. Recount actual model properties.
- [x] 7. Run full unit/client/API regression suites, review the final diff independently, address actionable issues, and report actual table counts and unresolved migration preconditions.

## Intended mapped column counts

TrainerApplication 29→28; TrainerProfile 22→17; TrainerRole 7→7; TrainerReview 16→15; PTPurchase 26→16; PTSession 38→31; PTFreeze 29→22; PTAvailability 11→8; Locker 11→7; LockerZone 10→10; LockerPricingPolicy 12→10; LockerRentalContract 35→25; LockerIncidentLog 15→15; GymAnnouncement 11→11; Notification 13→13. Shared PaymentTransaction 31→29 and PackageComponent 19→17. Counts include base/audit/shadow properties and are verified from the final EF model, not forced by adding JSON fields.

## Progress and rulings

- Initial branch: dev/vinhloc at 07d6003; eight pre-existing changed files contain applicant-name filtering and staff eligibility handling.
- Focused baseline: 21 readiness/PT rule unit tests passed; 19 trainer API client tests passed. Trainer workflow/model/pagination baseline: 29 passed.
- Ruling: Work in the existing feature checkout to preserve the user's eight pre-existing changes; do not create or switch worktrees during this task.
- Ruling: The user's instruction to implement the agreed removals supersedes the old additive-only rule in NeedRead/CLAUDE.md. Generate the migration for review but do not apply it to the application database.
- Ruling: Nonmapped domain projections may preserve existing caller-facing names when backed solely by canonical mapped data; no second persisted copy or hidden mutable fallback is allowed.

- Pre-existing changes committed as e78b6f1 with the user-selected message; not pushed.
- Actual final model assertions: 24 passed. Full unit: 787 passed. Full client: 631 passed; the same seven OperationsValidationTextTests failures exist in the pre-change baseline.
- Independent review caught and fixed loading of existing PhysicalProfile during guest-linked Hire, conflicting uploaded avatars during migration, missing approved-freeze dates, and invalid captured JSON value types.
- Final SQL verification: 10/10 passed (nine migration regressions and one SQL voucher regression), exit 0. This also rejects generic PT limits that would silently replace previously null PT-specific limits.
- API excluding WalkIn: 806/816 passed. Seven old migration fixture failures use LockerRules against a schema predating that column; one representative failed identically using the pre-change binary. Consultation identity fixture and SQLite invoice precision failures exist in the baseline. A voucher SQL deadlock passed on the isolated rerun. WalkIn defaults to SQLite and passed 25/25 separately.
- EF reports no pending model changes. The actual selected-table total is 335 to 281 columns, a reduction of 54. See the result document for the per-table count and validation limitations. The user requested a separate schema commit after reviewing the build/test results. No application-database migration was executed.
