# PT and Locker Policy Implementation Plan

> **For agentic workers:** Execute the approved tasks with test-driven-development and verification-before-completion. Independent readiness and locker tasks use dispatching-parallel-agents; the primary agent integrates and verifies the solution.

**Goal:** Apply the user's approved PT readiness, booking eligibility, lead time and daily locker pricing changes.

**Architecture:** Keep the existing services, repositories and Blazor screens. Persist readiness history through an additive EF migration and allow a PT purchase without a linked Gym membership. Enforce booking lead time on generated session timestamps on the server.

**Tech Stack:** .NET 8, EF Core 8, SQL Server, Blazor, xUnit.

**Spec:** User instructions in this chat, approved 2026-11-23, including the subsequent removal of Gym membership as a purchase prerequisite.

## Global Constraints

- Accepting must last at least 10 consecutive days to reset accumulated Not Accepting days; shorter periods pause the count. Role removal remains after more than 60 accrued days and does not accrue while contracts are running.
- Daily locker orders charge only the key deposit. Existing price snapshots and monthly prices remain intact.
- PT booking starts on any date, with the first actual session at least 24 hours after creating the hold. Exactly 24 hours is allowed; validate previews, selectable slots and holds consistently.
- Customer and Member accounts can purchase PT with or without Gym membership. PT attendance also does not require Gym membership, per the follow-up instruction. Kiosk/check-in and face recognition are out of scope and unchanged.
- No production database update, commit, push, deployment or unrelated changes.

## Review Focus

- Repeated short Accepting windows must not erase inactivity; exact 10-day boundary and running contracts must be tested.
- New daily pricing must accept zero rental fee and retain full deposit settlement; monthly prices stay positive.
- Date choices across UTC+7 midnight and non-Monday starts must respect actual session times.
- Customers without Gym membership must be able to proceed through checkout and fulfillment, not only preview.
- Existing contracts, freezes and late payment fulfillment must tolerate a missing membership link.

## Task 1 PT readiness

- [x] Add failing tests for 9/10-day Accepting windows, repeated toggles, 60/61-day inactivity and running contracts.
- [x] Persist paused/accrued inactivity and Accepting start; update status changes, daily sweep, UI and additive migration.
- [x] Run focused readiness tests and inspect the migration for legacy data preservation.

## Task 2 Daily locker pricing

- [x] Add failing deposit-only checkout and zero-fee policy tests.
- [x] Set new daily snapshots to zero rental fee, support the price list and update displayed totals; retain monthly and existing contract behavior.
- [x] Run focused locker service/client tests including deposit settlement.

## Task 3 PT booking and eligibility

- [x] Add failing tests for arbitrary start dates, exact 24-hour lead time, insufficient lead time and purchase/fulfillment without Gym membership.
- [x] Update PersonalTrainingRules, PersonalTrainingService.Checkout, PTPurchase membership link, API authorization, client date controls and messaging; remove Monday/Friday restriction.
- [x] Coordinate the combined additive schema migration with Task 1; preserve attendance access checks.
- [x] Run focused PT unit/client/API tests.

## Task 4 Integration and verification

- [x] Review all diffs and old-rule references; update the implementation policy notes.
- [x] Run the full solution tests and build, report actual results and any external environment blockers.
- [x] Review the final patch independently and fix material issues with regression coverage.

## Execution ledger

- Initial checkout clean on branch dev/vinhloc. Working in place so changes appear in the user's existing checkout.
- Detailed user instructions authorize implementation; no additional design approval is needed.
- Baseline sandbox build was blocked by SDK permissions/app locks. Tests ran with elevated local execution and isolated Policy configuration; the pre-task Debug assembly reproduced the unrelated integration failure categories.

- Final review: two Customer authorization/navigation gaps fixed, Customer freeze-history RED observed before fix; independent reviewer found no remaining actionable issues.
- Verification: 677 unit and 414 client tests passed; PT/locker group 55 passed, 2 SQL tests skipped. Full integration: 498 passed, 108 pre-existing failures, 42 SQL tests skipped. Failure names recorded in the adjacent verification report.
- Migration 20261123155353_UpdatePTPurchaseAndReadinessPolicy scaffolded; EF reports no pending model changes. Application database not updated; running app not restarted.

## Follow-up PT attendance amendment

- User explicitly removed the Gym membership prerequisite for PT attendance and excluded kiosk/check-in/face recognition from this task.
- Deleted the attendance guard, its repository query/interface/helper, and outdated PT Gym renewal warnings and coverage queries. PT freeze schedules and purchase eligibility stay independent of Gym membership.
- Regression RED: three missing/expired/suspended Gym cases returned PT_GYM_COVERAGE; active Gym case passed. After deleting the guard, all four attendance cases and three related PT dispute cases passed.
- No schema changes in this follow-up. Final validation: 677 unit and 414 client tests passed; the four PT attendance cases and no-Gym freeze planning passed in the full API suite. API suite retains108 prior failures and one additional unrelated SQLite concurrency-test failure; see verification report.
