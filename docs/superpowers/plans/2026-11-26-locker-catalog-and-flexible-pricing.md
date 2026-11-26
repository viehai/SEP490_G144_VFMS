# Locker catalog and flexible pricing implementation plan

**Goal:** Implement the user's 26/11/2026 changes in the application, then mirror the implemented UI in the inline demo.

**Approved scope:** Separate pricing page; add/edit monthly rental tiers including 2 and 5 months; auto-name zones A, B, ..., Z, AA; remove floor from locker UI; separate zone/locker forms; selecting a zone filters its diagram; vertical rules panel on the right with the key deposit; clear borders for Maintenance cells.

**Design:** Keep the existing controller/service/repository and DTOs compatible. Extend the pricing request with optional monthly tiers. Append nullable duration columns to pricing policies and contracts; use enum-based fallback for legacy rows. Each saved price list creates new policy versions, preserving old holds/contracts. Durations must be positive whole months up to 1,200 (100 years), unique within the active list; amounts keep the existing whole-VND bounds. Daily rent stays free and deposit-only.

**Pages:** `/manager/lockers` is zones/lockers with separate forms and a right rules editor/preview. `/manager/locker-pricing` manages monthly rows and common deposit/overdue fee. Member locker pages show the same right-side rules component. Keep the legacy floor columns/DTO fields for compatibility, omit them from UI and ignore them on automatic zone creation.

**Files:** Locker contracts/entities/service partials, LockerGrid and new rules panel, manager catalog/pricing pages and navigation, member locker pages, integration/client tests, additive EF migration, Markdown specs/logs, existing inline demo.

## Work

- [x] Write and run failing integration tests for 2/5-month prices, immutable pending orders, renewal, validation and automatic zone naming.
- [x] Implement flexible durations, automatic naming, pricing validation, common rules deposit data and migration.
- [x] Split manager pages; remove floor UI; zone-selected grid; right rules panel; explicit cell border styles. Add client rendering and API tests.
- [x] Run Locker integration/client tests and inspect the actual rendered components/styles.
- [x] Align Markdown specifications/logs and rebuild the inline demo from the implemented structure and colors. Verify demo transitions.

## Verification on 26/11/2026

- Locker integration tests: 34 passed. Covers legacy behavior, history and custom durations/zone naming.
- Locker client and workspace menu tests: 28 passed. Full client suite: 581 passed, 7 failed (588 total). All failures are `OperationsValidationTextTests.EveryValidatorMessageIsTranslated`, outside Locker, asserting translated text differs even when FluentValidation already emits Vietnamese under the host locale. These modules were not changed.
- EF model matches migration (`has-pending-model-changes`); `git diff --check` passes. Migration not applied to a database.
- Inline demo: 45 assertions across 15 scenarios; browser verified adding/saving 2- and 5-month rows, zone switching, blank locker number disabled and layouts at 320/1024px. Disabled Maintenance cell computed opacity 1, background #ECE8E4, border 1px solid #9F948D. Fixed the workspace disabled-button rule overriding cell opacity in the application CSS too.
- Demo uses fake local data; source colors/classes, manager forms, member rental/current pages, shared grid and rules panel mirror Razor/CSS. External bank and account operations remain simulated; the inline view does not run Blazor or access the database.

No commit, push, deployment, or migration against a shared database is requested. Preserve existing uncommitted history work.

## Follow-up requests: empty zones, narrow previews and detail navigation

- Empty zones offer Delete with confirmation. Only Manager may soft-delete a zone of their own gym; zones containing any locker, including inactive ones, remain protected. Uses existing IsDeleted; no new migration.
- Fix the duplicate rules wrapper in the preview and use container queries in both locker pages and the preview, so the diagram is not squeezed by fixed sidebar/rules columns in a narrow host.
- Processing and history details open dedicated pages with Back links, preserving list tab/search/page context. User explicitly rejected dialogs for these flows; dialogs are used only for deletion confirmation. Keep the existing payment panel mounted when refreshing a contract, preserving its pending checkout.
- Preview starts with an active Member rental, A02, and includes a named active-rental scenario.

Follow-up verification: 36 Locker integration tests and 31 Locker/menu client tests passed. Full client suite: 584 passed, 7 failures in the unchanged `OperationsValidationTextTests.EveryValidatorMessageIsTranslated` (same localization assertions described above). Preview: 50 assertions across 16 scenarios; browser confirmed no dialogs for processing/history details, Back navigation, confirmation before deleting an empty zone, active A02 rental, a single rules panel, and no content overflow at container widths 320px and 736px. The 320px locker diagram retains two columns. Regenerated Tailwind; diff whitespace checks pass. No database migration applied.
