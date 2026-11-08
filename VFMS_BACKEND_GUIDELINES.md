# VFMS Backend Guidelines

## 1. Purpose

This document defines the architectural and domain implementation conventions for the backend projects of the **Victor Fitness and Gym Management System (VFMS)**:
- **`VFMS.Domain`**: Core entities, enums, value objects, and domain exceptions.
- **`VFMS.Contracts`**: Plain DTOs (Requests/Responses), shared FluentValidation rules, and standardized message codes (`MSG-01` to `MSG-43`).
- **`VFMS.Application`**: Business workflows for the 87 use cases across 13 feature groups and enforcement of the business rules defined in Report 3 (identifiers up to `GBR-95` with non-contiguous numbering).
- **`VFMS.Infrastructure`**: EF Core 8 (`VfmsDbContext`), SQL Server migrations, repositories, JWT/BCrypt identity, Background Workers (`JOB-01` to `JOB-07`), and external API integrations.
- **`VFMS.API`**: ASP.NET Core 8 Web API controllers, webhook endpoints, middlewares, and dependency injection wiring.

Report 3 governs functional and non-functional requirements. Use case diagrams provide actor assignments, subject to the discrepancies noted below. The technology stack, entity schemas, and persistence mappings are project design. Use Report 4 and the ERD as design references, and check the source code to establish implementation status. [Report 3 SRS](https://drive.google.com/file/d/1ikvvPj3Q224wwM_5xqCjP680q0noxfZP/view), [Use Case Diagrams](https://drive.google.com/file/d/1UHzNJp126lZMapU8gWvM-cAEkYpmhZ2z/view), [Report 4 SDS](https://drive.google.com/file/d/1ITngkoOxluYMKFVV-Zt4n0e37CfFHTHf/view), [ERD](VFMS%20Erd.drawio.svg).

## 2. Technology Stack & Architectural Rules

- **Runtime:** .NET 8 (`net8.0`).
- **Database & ORM:** SQL Server + Entity Framework Core 8 (Code-First Migrations in `VFMS.Infrastructure`).
- **Mapping & Validation:** Mapster for DTO projections; FluentValidation for input and business rule validation.
- **Authentication:** Use JWT bearer access tokens (maximum 30 minutes) and refresh tokens (7 days, or 30 days with Remember Me). Blacklist active tokens immediately on logout (`GBR-28`). Hash passwords using BCrypt or Argon2 (`GBR-26`). Temporarily lock accounts for 15 minutes after five consecutive failed sign-ins within a 15-minute window (`GBR-27`).
- **Strict Dependency Flow:** `VFMS.API` -> `VFMS.Application` -> `VFMS.Domain`. `VFMS.Infrastructure` implements interfaces defined in `VFMS.Application`. Never return EF Core entities directly from API controllers.

**Current authentication gaps.** Logout revokes the submitted refresh token. A previously issued access token can remain valid until expiry. Registration creates an active account directly, so OTP verification remains pending. Login checks existing lock status, with automatic lockout after repeated failures still pending.

### Password recovery requirement and current implementation (Module 1)

The approved password recovery verification flow (`UC-04`, `GBR-29`) requires a single-use OTP of six numeric digits valid for exactly 5 minutes (300 seconds) under `GBR-25`.

**SRS clarification.** The `API-05` inventory describes emailing password reset links. Reconcile this delivery wording with the required OTP verification step before changing the recovery workflow.

**SRS compliance gap.** The current `POST /api/auth/forgot-password` queues email instructions with a single-use reset link containing a hexadecimal token. Its configured default lifetime is 15 minutes. `POST /api/auth/reset-password` consumes that token and revokes existing refresh tokens in the same transaction as the password change. SMTP uses MailKit. The backend endpoints are available independently of the mock Profile form, and a frontend reset screen is still pending. See [VFMS_PASSWORD_RECOVERY_API.md](VFMS_PASSWORD_RECOVERY_API.md) for implementation details.

## 3. Unified Package, Order, Voucher & Payment Workflow (Module 2)

### 3.1 Composite Package & Component Catalog (`Package` & `PackageComponent`)
- `Package` stores the commercial product (`PackageId`, `Name`, `Description`, `Price`, `PackageStatus`, `CreatedAt`, `UpdatedAt`).
- `PackageComponent` defines one or more service entitlements belonging to a `Package` (`PackageComponentId`, `PackageId`, `ComponentType` IN `('GYM', 'YOGA', 'PT')`, `DurationDays`, `DurationMonths`, `SessionCount`, `YogaClassTypeId`, `AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`, `AccessStartTime`, `AccessEndTime`).
- When removal is requested, membership packages with existing sales records or active subscriptions must transition to `Inactive` to preserve historical records and existing entitlements (`GBR-33`).

### 3.2 Cart, Order & Beneficiary Assignment (`Order` & `OrderItem`)
- Customers and Members can purchase one or multiple packages in a single checkout transaction (`GBR-36`).
- `Order` records the transaction header (`OrderId`, `PurchaserUserId`, `OrderNumber` UNIQUE, `Channel`, `OrderStatus`, `CreatedAt`, `CompletedAt`).
- `OrderItem` records each line item (`OrderItemId`, `OrderId`, `PackageId`, `BeneficiaryUserId`, `UnitPriceSnapshot`, `PackageNameSnapshot`).
  - Allows purchasing for oneself (`BeneficiaryUserId == PurchaserUserId`) or gifting to another registered user (`BeneficiaryUserId != PurchaserUserId`) (`GBR-36`).
  - Activation can be immediate or deferred up to 30 days (`GBR-35`, `MSG-25`). For a same-service purchase or renewal made before the active membership expires, use the day after expiry when the member leaves the start date unspecified (`GBR-39`).

### 3.3 Voucher Validation & Cap Calculation (`Voucher` & `VoucherRedemption`)
- Manager configures vouchers in `Voucher` (`VoucherId`, `Code`, `Name`, `Description`, `DiscountType` IN `('PERCENT', 'FIXED')`, `DiscountValue`, `StartAt`, `EndAt`, `MaxRedemptions`, `MinimumSpend`, `MaxDiscountAmount`, `VoucherStatus`, `CreatedByUserId`).
- During checkout (`Apply Voucher`), the backend must validate atomically:
  1. Only **one** voucher code can be applied per `Order` (`GBR-45` - no stacking).
  2. Current timestamp is between `StartAt` and `EndAt`, and `VoucherStatus == 'Active'` (`GBR-47`).
  3. If a global quota is configured, total confirmed redemptions must be below `MaxRedemptions`. Transition the voucher to `Expired` when the global quota is exhausted or the campaign ends (`GBR-47`).
  4. Order subtotal (sum of `OrderItem.UnitPriceSnapshot`) `>= MinimumSpend` (`GBR-46`, `MSG-22`).
  5. **Discount Calculation (`GBR-46`):**
     - If `DiscountType == 'FIXED'`: `DiscountAmount = MIN(DiscountValue, OrderSubtotal)`.
     - If `DiscountType == 'PERCENT'`, require a configured `MaxDiscountAmount`. Compute `RawDiscount = OrderSubtotal * (DiscountValue / 100m)` and enforce `DiscountAmount = MIN(RawDiscount, MaxDiscountAmount)`.
- Record applied vouchers in `VoucherRedemption` (`VoucherRedemptionId`, `VoucherId`, `OrderId`, `DiscountAmount`, `RedeemedAt`, `RedemptionStatus`). Release the reservation when an unpaid online order expires after 15 minutes (`GBR-48`, `JOB-01`). Restore the voucher redemption count within 15 minutes of a legitimate pre-fulfillment cancellation or refund (`GBR-49`).
- **Optional implementation extension.** The current entity and ERD contain `MaxRedemptionsPerUser`. Per-user quotas fall outside the Report 3 checkout requirements.

### 3.4 VietQR Generation, SePay Webhook & Fulfillment (`PaymentTransaction`, `Invoice`, `MemberMembership`)
- The checkout design creates a `PaymentTransaction` (`PaymentTransactionId`, `UserId`, `OrderId`, `GatewayName`, `GatewayTransactionReference`, `PaymentMethod`, `AmountDue`, `AmountReceived`, `PaymentStatus`, `PaidAt`, `RawGatewayStatus`) and calls VietQR (`API-04`) for a dynamic QR code containing bank details, the exact amount, and a unique payment code.
- Report 3 specifies `POST /api/v1/commerce/payment/webhook/sepay` as the backend callback (`API-03`). The backend design verifies the SePay signature, matches the transaction idempotently, and performs the following steps in a single database transaction:
  1. Updates `PaymentTransaction.PaymentStatus = 'Paid'` and `Order.OrderStatus = 'Paid'` (`GBR-37`).
  2. Generates an immutable `Invoice` (`InvoiceId`, `PaymentTransactionId`, `InvoiceNumber`, `IssuedAt`).
  3. Provisions `MemberMembership` (linked to `OrderItemId`) for each purchased component and promotes the beneficiary's role from `Customer` to `Member` if not already a `Member`.
- The current membership catalog is read-only for Customers and Members. Membership checkout and the SePay webhook remain planned.

## 4. Membership Freeze, Trainer Recruitment & PT 1-on-1 Workflows (Modules 2, 3, 4)

### 4.1 Membership Freeze (`FreezePolicy` & `MembershipFreeze`)
- Global defaults live in `FreezePolicy` (`EligibilityPaidDays`, `MinimumRequestDays`, `DefaultMaxFreezeDays`, `DefaultMaxFreezeCount`), while component-level overrides live in `PackageComponent` (`AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`).
- Validate advance notice (`GBR-42`), min/max duration per request (`GBR-40`), and remaining cumulative quota (`GBR-41`, `MSG-27`). Weekly Yoga packages cannot be frozen (`GBR-85`).
- Manager reviews and approves or rejects requests (`UC-17`). `JOB-03` puts the membership into `Frozen` when an approved freeze starts, restores `Active` at the end, and extends the expiry date by the frozen days. Suspend turnstile and booking access while `Frozen` (`GBR-43`). Recalculate the extension using the actual frozen days on early unfreeze (`GBR-44`).

### 4.2 Trainer Recruitment & Profile (`GBR-01` to `GBR-05`)
- Guests/Customers submit trainer applications with Citizen ID (CCCD), Email, Phone, and certificate attachments (`<= 5MB` PDF/JPG/PNG, `GBR-01`). Enforce 30-day duplicate submission cooldown per CCCD/Email (`GBR-03`) and block blacklisted applicants.
- Submitting an application does **not** create an `ApplicationUser` account (`GBR-02`). Only when the Manager marks the candidate as `Hired` (`UC-23`) and sets Trainer Level + commission tier (`GBR-04`) does the system provision a PT user account.
- PTs can update their public bio and avatar (screened against `BannedWordList`), but new certificates/specialties require Manager approval (`GBR-05`).

### 4.3 Personal Training (PT 1-on-1) Scheduling & PT Freeze (`GBR-06` to `GBR-17`)
- Requires an active General Gym Membership covering the training period (`GBR-10`, `MSG-31`).
- Support PT availability configuration (`UC-26`), schedule option viewing (`UC-27`), and PT package purchase (`UC-29`) with atomic database locking (`GBR-09`, `MSG-30`) to prevent double-booking.
- **SRS clarification.** The `UC-27` summary limits each trainer to one member per day, and the `UC-32` summary describes day-based availability despite its freeze-policy title. `GBR-06` and `GBR-09` describe time-slot scheduling. Reconcile the booking unit before finalizing availability rules.
- **PT Freeze (`UC-32`, `UC-33`, `UC-34`):**
  - Require at least 7 consecutive days per request. Apply the cumulative caps in `GBR-15`: 12 sessions → 15 days, 24 sessions → 30 days, and 36 sessions → 60 days.
  - The Manager reviews, approves, or rejects PT freeze requests (`UC-34`) under the latest standalone use case diagram and the Manager precondition in Report 3 §II.4.10. PT access remains scoped to permitted assigned-member and scheduling workflows.
  - **Authorization discrepancy.** Report 3 §I.4.4 and its embedded PT diagram (§I.4.3.7) also give PT access, and §II.4.10 names PT as a secondary actor. Reconcile these references before granting PT review or approval authority.
  - On approval, extend the PT package expiration by the accepted number of frozen days (`GBR-16`). Cancel booked sessions within the freeze window, restore the affected unused sessions to the balance, and release the trainer slots for other eligible members (`GBR-17`). Preserve confirmed bookings outside the freeze window and prevent double-booking when released slots are booked again.
  - **SRS clarification.** Report 3 §II.4.10 describes projecting sessions from the resume date and setting expiry to the last rescheduled session. This can differ from the exact frozen-day extension in `GBR-16`. Separately, the `GBR-17` rule text ties cancellation and slot release to approval, while its responsibility column lists freeze activation. Confirm the projection and execution timing before implementing them.

## 5. Locker Rental, Yoga, Check-in & AI Workout Workflows (Modules 5, 7, 8, 9)

### 5.1 Locker Rental Management (`GBR-18` to `GBR-20`)
- Restrict each Member to at most **1 active locker** at any time (`GBR-18`, `MSG-39`).
- Front-desk Staff manages locker registry, contracts (`Manage Locker Registry & Contracts`), and termination/deposit settlement (`Terminate Locker & Settle Deposit`, 100,000 VND refundable Key Deposit per `GBR-19`, `MSG-40`).
- Staff and Manager handle overdue lockers (`> 7 days` overdue incurs deposit forfeiture and forced clearance per `GBR-20`) and maintenance states.

### 5.2 Yoga & Group Class Management (`YogaStudio`, `YogaClass`, `YogaClassSchedule`, `YogaBooking`)
- `YogaStudio`: Unique studio name per gym + floor number (`GBR-71`); cannot deactivate if future active sessions exist (`GBR-72`).
- `YogaClass` & `YogaClassSchedule`: Enforce min/max participants (`1 <= min <= max`, `GBR-73`), session duration 45-90 minutes with a 15-minute studio buffer (`GBR-75`), no studio or instructor overlap (`GBR-74`), and publication at least 7 days before the first session (`GBR-77`). Rescheduled sessions link back via `RescheduledFromId`.
- `YogaBooking` requires active Yoga/Combo membership (`MSG-31`) and allows one active booking per class (`GBR-79`). Direct new registrations to the waitlist when a class is full. Joining is free and the first waitlisted member must confirm when a seat opens (`GBR-80`, `GBR-82`). Cancellation is penalty-free at least 2 hours before start. Record later cancellations as no-shows (`GBR-83`). Suspend Yoga booking for 7 days after **3 no-shows within 30 days** (`GBR-84`).
  - **SRS clarification.** The `MSG-34` context labels late cancellation as denied, while `GBR-83` and the message text describe a no-show penalty. Preserve the penalty behavior and reconcile the message context.
- The assigned PT/Yoga Instructor or Staff submits the attendance roster within 30 minutes after the session starts (`GBR-86`, `UC-51`). Mark each booked member as attended or a no-show. The session still takes place if nobody attends (`GBR-87`).
  - **SRS clarification.** Report 3 §II.7.9 and `GBR-86` include Staff, while the permission matrix (§I.4.4) and Staff diagram (§I.4.3.4) omit Staff attendance access. §II.7.9 also describes saving late rosters and flagging the Manager. Reconcile Staff access and late submissions with the matrix and 30-minute rule.
  - **SRS reference discrepancy.** The attendance rules cite `UC-67`, which the master list assigns to Review Gym & Services. The master list identifies Take Yoga Attendance as `UC-51`.

### 5.3 Biometrics & Kiosk Check-in/Check-out (`FaceData`, `QRCode`, `GBR-89` to `GBR-92`)
- Obtain explicit biometric consent before capture, store only encrypted facial feature embeddings, and discard raw camera images immediately. Delete facial templates immediately at the user's request. Automatically delete them 90 days after the user's last membership expires (`JOB-07`).
- The current storage design maps `FaceData.FaceNetEmbedding` to `VARBINARY(MAX)`. The API workflow for user-requested deletion and the scheduled `JOB-07` purge remain pending.
- Validate active membership (`GBR-89`, `MSG-36`) and enforce a **15-minute anti-passback cooldown** (`GBR-90`, `MSG-37`).
- Accept check-out only when the member has an open check-in (`GBR-91`).
- Self check-in within `[-30 mins, +15 mins]` of a scheduled PT session automatically marks that PT session as completed (`GBR-92`, `MSG-38`).
- Staff can perform manual check-in/check-out (`UC-55`, `UC-56`) with Staff UserId logged; Staff and Manager can monitor live occupancy (`View Active Check-ins`, `UC-57`).

### 5.4 AI Personalized Workout Plan (`API-02`, `GBR-93` to `GBR-95`)
- Requires completed Fitness Survey (`GBR-93`). Strip all PII (Name, Email, Phone, UserId) before calling Google Gemini API (`API-02`).
- Only one plan is `Active` at a time. Generating a new plan archives the previous active plan (`GBR-94`). If Gemini times out or fails, assign a goal-matched fallback and mark it as a standard plan (`GBR-95`).
- **SRS reference discrepancy.** The AI rules cite `UC-14` and `UC-15`. Use `UC-58` and `UC-59` for workout-plan generation and management in the master list.

## 6. Competitions, Equipment, Supplements & System Administration (Modules 6, 10, 11, 12, 13)

- **Communication (`GBR-21` to `GBR-23`):** Retain in-app notifications for 90 days. Each `GymAnnouncement` must have a valid target audience and expire within 60 days of publication. Allow a maximum of **3 concurrent active pinned announcements**.
  - **Authorization discrepancy.** `GBR-22` allows Manager and Admin to create and publish announcements. Report 3 §II.6.2 lists both as primary actors but gives a Manager-only precondition, and the §I.4.4 matrix denies Admin. The diagrams link Manage Gym Announcements (`UC-42`) directly to Manager. The standalone diagram also models Admin as a specialization of Manager. Reconcile these sources before changing route or API authorization.
- **Competitions (`UC-60` to `UC-66`):** Guest/Customer/Member view competitions & leaderboards; Member registers and views personal history; Staff manages participants and records preliminary results; Manager configures competitions/scoring rules and confirms final results + voucher rewards.
- **Equipment & Procurement (`GBR-53` to `GBR-56`, `GBR-62`, `GBR-68`):** Members report issues (`UC-71`); Staff handles issues (`Resolve`, `Escalate`, `Mark as Safe` in `UC-72`) and submits multi-item `PurchaseProposal` (`UC-74`); Manager manages `GymEquipment`, arranges `EquipmentRepairs`, and approves/rejects purchase proposals (`PurchaseProposalReview`, `UC-75`).
- **Supplement Store (`GBR-57` to `GBR-61`, `GBR-69`, `GBR-70`):** Catalog-only (no stock quantity tracking). Customers and Members may view and order only products with status `Available`. Status flow is `Pending -> Processing -> ReadyForPickup / OutForDelivery -> Completed`. Record offline payment before completion (`GymPickup` at counter or `HomeDelivery` COD). Customers and Members can cancel only while `Pending`. Staff and Manager process orders, and Manager manages products.
  - **SRS clarification pending.** `MSG-41` describes insufficient stock, while `GBR-69` prohibits stock quantity tracking. Keep the catalog-only model pending clarification of when the message applies.
  - **SRS compliance gap.** The current store lists both `Active` and manually marked `OutOfStock` products. `GBR-70` limits visibility to available products.
- **Reviews, Banned Words, Business Reports & Audit Logs (`GBR-63` to `GBR-66`):**
  - Screen gym reviews (`UC-67`) against `BannedWordList` (`GBR-66`); Manager replies to feedback (`UC-68`).
  - Business reports show read-only historical metrics (`UC-86`, `GBR-63`). The backend stores report snapshots in `BusinessReportSnapshot` and `ReportMetric`.
  - **Authorization discrepancy.** Report 3 §II.14.1 and the current business reports API allow Manager and Admin to view reports (`UC-86`). The §I.4.4 matrix grants Manager access and denies Admin. The diagrams link the use case directly to Manager, and the standalone diagram also models Admin as a specialization of Manager. Reconcile report access separately from the security NFR, which limits member-information exports to Manager.
  - Admin manages staff accounts, roles, account locks (cannot self-lock per `GBR-64`), banned words, and views read-only `AuditLog` (`GBR-65`).
  - Record privileged membership plan and membership policy changes in the system audit log (`GBR-65`). The security NFR also requires read-only logs retained for 12 months, covering administrative actions, manual check-ins and check-outs, payment confirmations and refunds, and member data exports. The current membership plan service audits create, update, activate, deactivate, and delete actions. The gym settings service audits changes to general policies.
