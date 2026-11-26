# Locker payment choice, vouchers and action feedback

**Goal:** Match the PT/membership payment experience for locker rental and renewal; confirm zone creation and report action results through toasts. User explicitly declined a Staff-ticked customer-consent checkbox.

**Design:** Reuse existing voucher rules, capacity reservations, order redemption and payment fulfilment. New UI can create a pending order before selecting Cash/VietQR and voucher; legacy API requests retain immediate checkout behavior. Only the rental/renewal fee is discountable; deposits and overdue penalties are not. Choosing payment fixes the voucher and amount without extending existing deadlines. Bank callbacks remain the only confirmation for VietQR. Fully discounted renewals complete through the same fulfilment method. Pending renewal orders are linked from My Locker so the owner can choose/pay. No new DB columns.

- [x] Add failing integration tests for owner/staff checkout choice, fees excluded from discounts, voucher capacity/release, repeated requests, zero due and unchanged expiry before payment.
- [x] Add choice API and extend existing checkout DTOs, voucher reads and cancellation/expiry handling; keep old clients compatible.
- [x] Reuse shared checkout choice controls in Member, Staff rental and renewal pages. Keep actions on dedicated detail pages; no customer-consent checkbox.
- [x] Confirm Add Zone; add success/error toast types and cover locker mutations while keeping inline error details.
- [x] Update specifications/logs and inline demo, verify source tests and UI interactions.

## Verification

- Locker and MyVoucher integration: 48 passed, 1 SQL Server-specific test skipped because no test SQL Server was configured. Tests cover discounted deposits/penalties exclusion, no-charge renewal, ownership, wallet minimum spend, immutable selection/idempotency, old issued cash orders, callback-only VietQR fulfilment and reservation release on cancellation/expiry/key return.
- Relevant Locker/menu client tests: 33 passed. Full client suite: 586 passed, 7 failed in unchanged `OperationsValidationTextTests.EveryValidatorMessageIsTranslated` under Vietnamese host locale (same failures recorded in previous work).
- Both API/client compile. EF model has no pending changes; no new schema or database migration execution. Tailwind regenerated; diff whitespace checks pass.
- Preview: 60 assertions over 16 scenarios. Browser verified Member renewal -> voucher picker SAVE20 -> VietQR 200,000 VND -> simulated bank payment; Add Zone cancellation creates no zone, confirmation creates C; invalid voucher leaves amount unchanged and displays error feedback; Staff-created renewal opens from My Locker with cash/QR/voucher choices. Cash receipt confirmation is disabled until the exact amount is entered and enables immediately while typing; successful collection shows its toast on the dedicated detail page. Financial bank callbacks in tests are simulated; no real transfer.

Deferred checkouts use the existing gateway field value `PendingChoice` until selection, distinguishing them from already-issued legacy Cash/SePay orders whose PaymentStartedAt may be null. Existing orders cannot be re-quoted by this new flow.

No commit/push or database migration execution. Preserve earlier uncommitted locker/history changes.

## Follow-up: person search across Feature 5 roles

- [x] Staff counter member lookup supports name, phone and email; results show all three and require explicit eligible selection. Editing or re-submitting a search clears the old choice. Contract and unpaid searches support phone/email too.
- [x] Member screens use the authenticated owner; Manager locker screens have no person selectors. Remove the demo-only member dropdown and switch fixture accounts through existing scenarios.
- [x] Add HTTP integration coverage for all three fields, case-insensitive email, same-gym isolation, role restriction and contract/unpaid searches. Verify 56 integration passed/1 SQL Server skipped, 29 related client passed, 86 offline preview assertions over 16 scenarios. Browser verified three-field search, Enter submission, clearing selection and no person dropdowns; 736px layout has no overflow.
