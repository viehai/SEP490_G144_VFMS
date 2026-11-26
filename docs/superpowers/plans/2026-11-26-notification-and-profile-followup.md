# Approved follow-up: notifications, setup email and avatar errors

User approved items 8, 12 and 13 after the explanation, and clarified that GBR-10 is retired. These changes extend the uncommitted approved-audit fixes; do not revert those fixes or reintroduce item1 check-in changes.

## Design

- Reuse the existing bell dropdown and notification data. Add an explicit per-message mark-read action, mark-all-read button and View All link inside the dropdown. Full history is recipient-only with server pagination (10/page), stable newest-first order and existing role-appropriate workspace shells. Opening the dropdown does not mark messages read. Mutations are owner-scoped, idempotent and reject inactive/deleted users. Preserve the eight-item preview; no new database columns or realtime transport.
- Expose management-only CanResendSetup capability derived from the same authoritative account eligibility as the resend endpoint. Show the resend button only when this is true; keep server validation for stale UI. Already activated or linked existing accounts do not qualify. Preserve interview privacy, raw notes and separate timestamp.
- Put invalid-type, oversize and file-reading errors directly under the trainer avatar input with accessible field associations. Clear them on valid reselect, preserve the published avatar until successful save, and retain existing file limits. Keep global/profile and biography errors in their existing locations.
- Remove the retired GBR-10 reference and its empty legacy business-rule row from the canonical Markdown. No change to PT check-in logic or gym membership eligibility is authorized by this document correction.

Use existing components/endpoints/patterns; a separate notification system, email rebroadcast, changed training policies or modified database columns would add unnecessary scope. No demo, commit/push or migration execution.

## Work

- [x] Write baseline-compiling regression tests and verify RED before production edits.
- [x] Implement notification API ownership/read state plus dropdown/history UI.
- [x] Implement server-derived resend capability and Manager visibility.
- [x] Move avatar validation errors inline and retain published-image behavior.
- [x] Clean retired GBR-10 reference and update scope/message/decision logs.
- [x] Run serialized targeted checks, appropriate suites, independent review and final diff check.

## Verification

- RED: all 14 initial dropdown/avatar cases failed as expected; resend UI was 1 failure/1 valid control. API RED was 17 expected failures/5 existing control passes. SQLite executes all new resend eligibility tests without requiring SQL Server.
- Additional Review(Hire) response regression caught a missing capability on initial/repeated Hire responses; both manager return paths now use the same eligibility helper. Pending PT/Yoga return true, linked activated account false; list/public mappings remain false/private.
- Independent review caught a page-change/dropdown mutation race. Three delayed-response regressions reproduced disabled-state, wrong-page and stale-unread issues. Shared busy state and load versions fix them; all three passed in the final suite.
- Full unit suite: 779 passed. API follow-up/regression: 56 passed, 1 SQL Server-only test skipped because no SQL test connection is configured.
- Full client final: 620 passed, 7 pre-existing OperationsValidationTextTests.EveryValidatorMessageIsTranslated failures (Equipment Id, Renewal Advance Days, Renewal Grace Days, Note, Min Price, Max Price, Purchase Price). No new client failures.
- Final git diff whitespace check passed. All three paused check-in/occupancy files match HEAD exactly. Canonical Markdown has zero remaining GBR-10 references; the legacy empty facility-entry rule was removed.
- No new schema changes in this follow-up; the earlier InterviewedAt migration remains unapplied. Original DOCX unchanged. No demo, commit or push.
- Category filters and entity deep-link navigation from the full UC 6.1 remain deferred; this implementation covers dropdown actions, recipient history, read state, pagination and permissions.
