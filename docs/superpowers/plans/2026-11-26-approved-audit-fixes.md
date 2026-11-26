# Approved audit fixes implementation plan

**Goal:** Fix approved audit items 2–7, 10 and 14; remove all PT replacement, including freeze-linked changes; correct Announcement business rules without implementing that module. User clarified item3 is approved after re-reading. Item1 was subsequently paused pending explanation; its implementation and new tests were removed.

**Design:** Leave attendance accounting and occupied-interval rules unchanged while item1 is paused. Ordinary PT changes require published slots on submit/accept; checkout start week follows Monday–Sunday. Freeze options exclude the hold being edited. Remove trainer-change UI/options and reject legacy requests/pending approvals that would transfer the trainer; keep historical data and normal freeze. Interview notes store full 2,000-character content plus an additive nullable timestamp field. Reuse existing inbox for Customer/Member; leave full Notification Center/read actions outside this request. Manager gets overdue/maintenance UI with existing authorized APIs. Confirm lost-key forfeiture before the first persistent action. No demo, commits/pushes, or database migration execution.

**User questions:** Explain existing bell/dropdown, resend-setup visibility and avatar error placement. Explain blank GBR-10; do not invent PT/Gym membership requirements. Explain hiring blockers from current code. Defer membership payment notifications (item11). Items12/13 remain explanation only. Item2 is removed, not repaired as a transfer feature.

- [x] Regression tests first; run a serialized RED batch before production edits.
- [x] Fix published reschedule validation, start-week boundary, freeze picker and remove trainer replacement (parallel independent ownership). Occupied-interval fix is deferred by the user.
- [x] Add note timestamp/schema/DTO/UI; preserve old note data; add migration only, do not apply it.
- [x] Enable Customer/Member inbox and UI, Manager locker operations and key-loss confirmation.
- [x] Update Announcement business specification and scope/messages/deferred notes; no Announcement code.
- [x] Run targeted tests/build, review diff and report remaining decisions/limitations.

**Checks:** Ownership and role isolation; expired or deactivated accounts remain forbidden; no method grants Manager cash/handover rights. Key-loss cancel writes nothing and repeated confirm cannot duplicate writes. Old API transfer payloads cannot bypass removal. New timestamp does not truncate old notes. No changes to voucher, checkout deadlines, attendance session consumption, or SQL schema columns already present.

## Verification results (26 November 2026)

- API build succeeded. Unit suite: 779 passed. Related client tests: 76 passed.
- Broader API regression: 182 passed, 9 SQL Server tests skipped because no SQL test connection was configured. Includes real Manager maintenance/read access and forbidden cash/key handover, own inbox isolation and locked/deleted denial.
- Full client suite: 598 passed, 7 pre-existing OperationsValidationTextTests translation failures remain (same seven failures recorded in the previous baseline). Added inbox mock/DI to existing public-layout test hosts; customer/membership runtime logic was not changed.
- Independent review found notification text inherited nowrap from the public navbar. Fixed with a scoped white-space reset; generated CSS verified. No other actionable review findings.
- Migration 20261126140850_TrainerInterviewTimestamp Up only adds nullable datetime2 InterviewedAt. Snapshot matches the model; EF reports no pending model changes. No database migration was executed.
- PersonalTrainingRules.cs, CheckInRepository.cs and GymAccessServices.cs match HEAD exactly; both new item1 test files were removed. Item1 remains unchanged.
- Canonical Markdown and decision/scope/message/deferred logs updated; original DOCX unchanged. No demo, commit or push. Git diff whitespace check passed.
