# PT and shared component schema task

Implement this task in the current checkout. The user approved removing duplicate persisted data while keeping behavior outside features 3–6. Base commit e78b6f1 contains the user's original trainer filtering fixes; preserve it. Do not commit, push, create worktrees, run application migrations, or dispatch other agents.

## Ownership

You own PT domain entities (PTPurchase, PTSession, PTFreeze, PTAvailability), PackageComponent, PT enum additions, PersonalTrainingService partials, PersonalTrainingRules, PersonalTrainingRepository, DashboardQueries.Trainer/Operations (only affected PT predicates/projections), FeedbackRepository.PackageReviews (only PT session ownership), MembershipPurchaseRepository.StaffOrders, MembershipPlanRepository/Service and their interfaces (only backward-compatible component-limit aliases), and related PT/unit/integration test fixtures. You may add tests. Do not modify VfmsDbContext, migrations, TrainerService/TrainerRepository, payment entity/helper, locker sources/tests, avatar/UI, or the root FeatureSchemaSimplificationTests. Report any needed edits outside ownership to the root.

## Required persistence shape

- PTPurchase 26→16: remove ReceiptEmailSentAt/RetryAt, TrainerChangeDeadline, PurchaseDate, RemainingSessions, IsCompleted; stop persisting PackageNameSnapshot/TotalSessions/PurchasePrice (canonical purchased OrderItem/payment/voucher data). Root will prevent accidental TrainerProfileId FK. Keep MemberMembershipId and its existing legacy feedback linkage, actual member/trainer ownership, start/end dates, package freeze/level snapshots, WeeklyPatternJson, HoldExpiresAt. Cancellation/credits/payment policy unchanged.
- PTSession 38→31: remove ProposalEmailSentAt/RetryAt, AttendanceReminderSentAt, CheckInCompletedAt, persisted MemberUserId (derive PTPurchase.MemberUserId), ProposedAdjustmentType (fold into SessionStatus cancellation pending/requested states), ProposedEndTime (derive proposed start plus original duration). Keep all evidence/credits/note author/actual session trainer. Preserve unsupported DoubleSession proposal rejection and occupied-time/credit semantics.
- PTFreeze 29→22: remove SupportingDocumentUrl; derive MinimumDaysSnapshot/MaxDaysSnapshot/RequestedByUserId from parent purchase; ActualStartDate is RequestedStartDate only when approved/active/completed; ChangeResumeDate from ChangePayloadJson. Replace ChangeRequestType + ChangeRequestStatus by one mapped enum ChangeState (None plus Withdraw/EarlyResume×Pending/Approved/Rejected). Keep initial RequestStatus independently and keep original dates and change review evidence. Public DTO string values stay compatible.
- PTAvailability 11→8: remove AvailableDate and mapped EndTime (new weekly slots always one hour, calculated from StartTime). Keep SlotStatus and nullable DayOfWeek (never convert old dated slots to recurring). Root removes accidental TrainerProfileId.
- PackageComponent 19→17: remove PTFreezeMinimumDays/PTFreezeMaxTotalDays, use MinFreezeDaysPerRequest/MaxFreezeDaysPerRequest for PT only. Keep one-freeze contract rule. Do not change GYM/YOGA values or generic freeze behavior; generic membership flow rejects PT. Preserve catalog DTO aliases/projections used by other features.

## Canonical data rules

Use the immutable OrderItem snapshot, never the mutable Package catalog. PT checkout currently produces exactly one order item; do not guess from First() on ambiguous history. Root migration will validate/backfill existing links and reject unmappable/ambiguous histories. Domain reads must load their real source. Do not introduce unmapped mutable fallback state or create synthetic orders/items implicitly in property getters/setters. Pure [NotMapped] read projections retaining familiar CLR names are acceptable, provided all SQL predicates/projections use translatable mapped sources. Legacy fixtures should build real complete linked data. Preserve captured discounts even when voucher state later becomes Released. Pending Cash has no PaymentTransaction until staff confirmation; amount due falls back to item price minus captured discount. Extra transfer original quotes and shared refunds are retained.

## Test contract

Root added FeatureSchemaSimplificationTests and is running it RED before production changes. Add a behavioral regression that PT catalog/checkout uses generic component freeze limits while Gym values remain intact, and preserve existing PT checkout/attendance/cancellation/freeze/review behavior. Tests must exercise production behavior. Build/test with dotnet --no-restore -m:1 -nodeReuse:false, and use escalation if testhost is blocked. An old trainer SQL baseline is currently running from separate binaries; avoid modifying trainer test fixtures or historical migrations. Root will coordinate final full suites; do not spend long fixing unrelated baseline failures. Record commands/results and remaining cross-owner compilation references.

## Report

Write the report to docs/superpowers/plans/2026-11-27-schema-pt-report.md with files changed, canonical helper/interfaces introduced, tests and unresolved issues. Return a concise summary to root. Keep working through all owned changes; no new approval questions.
