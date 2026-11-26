# Trainer Contact Identity Implementation Plan

> **For agentic workers:** Use test-driven-development and verification-before-completion. UI and existing test-fixture updates use independent scoped agents; backend and integration verification stay with the primary agent.

**Goal:** Remove CCCD from trainer recruitment and profiles, using UserId for existing accounts and email or phone for applicant identity checks.

**Architecture:** Reuse account contact normalization, trainer serializable recruitment transactions and the existing manager identity-verification/account-selection flow. Keep UserId authoritative for profile ownership and rehire. Remove CitizenId fields, DTO members, validation, UI and database indexes/columns through a new migration.

**Tech Stack:** .NET 8, EF Core 8, SQL Server, Blazor, xUnit.

**Spec:** User instructions in this chat on 2026-11-23 approving removal of CCCD and replacement with UserId/email/phone.

## Global Constraints

- Match applicants by normalized email OR normalized phone; preserve existing Gmail dot-alias comparison.
- Use authenticated UserId for profile updates and existing account/profile identity, including after contact changes.
- Preserve ten-minute submission cooldown, employee/active-trainer restrictions, ambiguous account confirmation and closure of other open applications after hiring.
- Preserve all previous PT/locker changes. Kiosk/check-in/face recognition remain out of scope.
- Create but do not apply the schema migration. Do not edit historical migrations, commit, push or restart services.

## Review Focus

- Changing email while retaining phone must not bypass cooldown or active employee restrictions.
- Changing contacts must not unlink known users' applications/history or duplicate their trainer profiles.
- Email and phone matching different accounts must retain explicit manager verification and account selection.
- Legacy trainer profiles with no CCCD must support profile updates and rehire by UserId.
- Existing application/profile data apart from removed CCCD must survive the migration.

## Tasks

- [x] Add failing integration tests against real SQLite repository queries and trainer service (bypass only SQL Server's transaction lock in the test adapter).
- [x] Replace CCCD history/restriction/rehire logic with normalized email/phone and UserId, and remove obsolete domain/contract/validation fields.
- [x] Remove CCCD inputs, display, state and messages; adapt existing tests to contact identity rules.
- [x] Scaffold and inspect a migration dropping only CCCD columns and indexes and adding contact-history indexes as needed.
- [x] Run contact regressions, full unit/client/API suites, inspect failure differences against the documented existing test failures, and request independent review.

## Execution ledger

- Working in the existing checkout, preserving earlier authorized uncommitted changes.
- Earlier full API run had108 existing fixture/decimal failures and one intermittent unrelated SQLite onboarding-concurrency failure; unit677/client414 passed.

- RED confirmed phone cooldown/employee restrictions, stable account cooldown, missing-CCCD profile update, rehire and history auto-close gaps against the old implementation.
- Additional review RED: linked applications could target another contact-matched account or recreate a deleted owner; auto-close could reject a separate known user's application. Fixed and covered by four regression cases.
- Final contact suite:18 passed,0 failed. Unit suite677 passed; client suite415 passed. Full API suite514 passed,109 failed,42 SQL tests skipped. Existing108 fixture/precision failures plus unrelated onboarding concurrency failure are listed in the verification report.
- Migration20261123163628_ReplaceTrainerCitizenIdWithContacts generated and SQL inspected; model has no pending changes. Migration not applied and app not restarted.
- Independent review confirmed ownership and auto-close fixes; no remaining material findings.
