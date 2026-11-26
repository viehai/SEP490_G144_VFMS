# Shared locker checkout and cash receipt implementation plan

**Goal:** Reuse the existing PT/membership cash receipt behavior and checkout presentation for lockers without changing other features' payment rules.

**Design / spec:** User requests, 2026-11-26: cash receipt must match PT/membership; reuse checkout where reasonable; avoid affecting other features. The existing shared Staff package page accepts tendered cash >= due, shows change, and sends only due to its service. Locker collection currently requires exact input and embeds different forms. Use one shared presentation component for cash receipt, one for PT/locker payment choice, and one for all three QR displays. Keep API clients, fulfilment, deadlines, voucher apply/remove behavior and role checks in each feature. Do not combine the package-only order repository or change dashboard queries. Cash rental handover uses the shared receipt form plus existing identity/terms checks; key handover and payment remain one atomic locker API action.

**Tech stack:** Existing Blazor/.NET 8, Ui tokens and Vnd formatter. No dependencies, schema changes, migration execution, commits or pushes. Preserve all earlier changes.

- [x] Add failing behavior tests for locker cash tendered below/equal/above due and submit amount; retain PT/membership endpoint regression tests.
- [x] Extract the package cash form into Components/Payments/CashReceiptForm and use it in MembershipCashReceipt, Locker CollectPayment, outstanding collection and cash/key handover. Keep busy/error/confirmation checks and invoice feedback.
- [x] Share CheckoutPaymentOptions between PT and locker; share CheckoutQrDetails between membership, PT and locker. Keep membership voucher semantics unchanged and retain the existing CheckoutVoucherPicker.
- [x] Update scope/messages and finish related client/payment regression checks.

User cancelled further demo work on this turn. Stop preview/browser work; deliver code and test results only. Earlier local preview edits are not a deliverable and were not visually reviewed.

Verification so far: 61 cash/locker/staff-list API tests passed. 76 related client tests passed, including shared form shortage/change/malformed input, busy/identity blocking, new-order reset and checkout callbacks. Full client run before the last 3 added tests: 592 passed and 7 pre-existing OperationsValidationTextTests failures (same Vietnamese validator translation assertions as prior work). No API implementation or schema changes.

Final verification: rebuilt client and re-ran all 76 related tests after outstanding-collection and expired-cash-handover changes; all passed. Tailwind regenerated using cached pinned 3.4.17. Diff whitespace checks passed. No browser/visual verification after the user cancelled demo work. All feature-specific API calls and payment/expiry/voucher rules remain in their existing services.

**Review focus:** Short or malformed cash never submits; excess cash displays change and never increases booked revenue; repeated submission disabled while processing; QR never offers Staff manual-paid confirmation; due/order change resets tendered input. PT/membership voucher and expiry behavior remain unchanged. Existing PT legacy receipt route remains compatible.
