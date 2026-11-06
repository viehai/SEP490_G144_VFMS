# VFMS Backend Guidelines

## 1. Purpose

This document defines the architectural and domain implementation conventions for the backend projects of the **Victor Fitness and Gym Management System (VFMS)**:
- **`VFMS.Domain`**: Core entities, enums, value objects, and domain exceptions.
- **`VFMS.Contracts`**: Plain DTOs (Requests/Responses), shared FluentValidation rules, and standardized message codes (`MSG-01` to `MSG-43`).
- **`VFMS.Application`**: Business workflows implementing all 85/87 Use Cases across 13 Modules and enforcing Business Rules (`GBR-01` to `GBR-95`).
- **`VFMS.Infrastructure`**: EF Core 8 (`VFMSDbContext`), SQL Server migrations, repositories, JWT/BCrypt identity, Background Workers (`JOB-01` to `JOB-07`), and external API integrations.
- **`VFMS.API`**: ASP.NET Core 8 Web API controllers, webhook endpoints, middlewares, and dependency injection wiring.

## 2. Technology Stack & Architectural Rules

- **Runtime:** .NET 8 (`net8.0`).
- **Database & ORM:** SQL Server + Entity Framework Core 8 (Code-First Migrations in `VFMS.Infrastructure`).
- **Mapping & Validation:** Mapster for DTO projections; FluentValidation for input and business rule validation.
- **Authentication:** JWT Bearer Access Token (30-minute lifespan) + Refresh Token (7 days, or 30 days with Remember Me) with immediate token blacklisting on logout (`GBR-28`). Passwords hashed via BCrypt/Argon2 (`GBR-26`).
- **Strict Dependency Flow:** `VFMS.API` -> `VFMS.Application` -> `VFMS.Domain`. `VFMS.Infrastructure` implements interfaces defined in `VFMS.Application`. Never return EF Core entities directly from API controllers.

### Implemented authentication recovery (Module 1)

`POST /api/auth/forgot-password` queues generic email instructions; `POST /api/auth/reset-password` consumes a 15-minute, single-use token and revokes existing refresh tokens in the same transaction as the password change. SMTP uses the existing MailKit dependency. Configuration, response contracts, migration, deployment limits and tests are documented in [VFMS_PASSWORD_RECOVERY_API.md](VFMS_PASSWORD_RECOVERY_API.md). This backend feature does not wire the current mock Profile form or add a frontend reset screen.

## 3. Unified Package, Order, Voucher & Payment Workflow (Module 2)

### 3.1 Composite Package & Component Catalog (`Package` & `PackageComponent`)
- `Package` stores the commercial product (`PackageId`, `Name`, `Description`, `Price`, `PackageStatus`, `CreatedAt`, `UpdatedAt`).
- `PackageComponent` defines one or more service entitlements belonging to a `Package` (`PackageComponentId`, `PackageId`, `ComponentType` IN `('GYM', 'YOGA', 'PT')`, `DurationDays`, `DurationMonths`, `SessionCount`, `YogaClassTypeId`, `AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`, `AccessStartTime`, `AccessEndTime`).
- Packages with historical `OrderItem` records or active memberships must never be hard-deleted; toggle `PackageStatus` to `Inactive` instead (`GBR-33`).

### 3.2 Cart, Order & Beneficiary Assignment (`Order` & `OrderItem`)
- Customers and Members can purchase one or multiple packages in a single checkout transaction (`GBR-36`).
- `Order` records the transaction header (`OrderId`, `PurchaserUserId`, `OrderNumber` UNIQUE, `Channel`, `OrderStatus`, `CreatedAt`, `CompletedAt`).
- `OrderItem` records each line item (`OrderItemId`, `OrderId`, `PackageId`, `BeneficiaryUserId`, `UnitPriceSnapshot`, `PackageNameSnapshot`).
  - Allows purchasing for oneself (`BeneficiaryUserId == PurchaserUserId`) or gifting to another registered user (`BeneficiaryUserId != PurchaserUserId`) (`GBR-36`).
  - Activation start date can be immediate or deferred up to 30 days (`GBR-35`, `MSG-25`). If renewing or purchasing the same service type while an existing membership is active, queue the start date to the day immediately following current expiration (`GBR-39`).

### 3.3 Voucher Validation & Cap Calculation (`Voucher` & `VoucherRedemption`)
- Manager configures vouchers in `Voucher` (`VoucherId`, `Code`, `Name`, `Description`, `DiscountType` IN `('PERCENT', 'FIXED')`, `DiscountValue`, `StartAt`, `EndAt`, `MaxRedemptions`, `MaxRedemptionsPerUser`, `MinimumSpend`, `MaxDiscountAmount`, `VoucherStatus`, `CreatedByUserId`).
- During checkout (`Apply Voucher`), the backend must validate atomically:
  1. Only **one** voucher code can be applied per `Order` (`GBR-45` - no stacking).
  2. Current timestamp is between `StartAt` and `EndAt`, and `VoucherStatus == 'Active'` (`GBR-47`).
  3. Total confirmed redemptions `< MaxRedemptions`, and redemptions by `PurchaserUserId` (joined via `VoucherRedemption -> Order`) `< MaxRedemptionsPerUser` (`GBR-47`).
  4. Order subtotal (sum of `OrderItem.UnitPriceSnapshot`) `>= MinimumSpend` (`GBR-46`, `MSG-22`).
  5. **Discount Calculation (`GBR-46`):**
     - If `DiscountType == 'FIXED'`: `DiscountAmount = MIN(DiscountValue, OrderSubtotal)`.
     - If `DiscountType == 'PERCENT'`: `RawDiscount = OrderSubtotal * (DiscountValue / 100m)`, and if `MaxDiscountAmount IS NOT NULL`, enforce `DiscountAmount = MIN(RawDiscount, MaxDiscountAmount)`.
- Record applied voucher in `VoucherRedemption` (`VoucherRedemptionId`, `VoucherId`, `OrderId`, `DiscountAmount`, `RedeemedAt`, `RedemptionStatus`). If an order expires after 15 minutes (`GBR-48`, `JOB-01`) or is cancelled before fulfillment (`GBR-49`), release the voucher reservation.

### 3.4 VietQR Generation, SePay Webhook & Fulfillment (`PaymentTransaction`, `Invoice`, `MemberMembership`)
- Online checkout creates a `PaymentTransaction` (`PaymentTransactionId`, `UserId`, `OrderId`, `GatewayName`, `GatewayTransactionReference`, `PaymentMethod`, `AmountDue`, `AmountReceived`, `PaymentStatus`, `PaidAt`, `RawGatewayStatus`) and calls `VietQrService` (`API-04`) to generate a dynamic QR code containing `OrderNumber`.
- `POST /api/v1/commerce/payment/webhook/sepay` (`API-03`) verifies the SePay signature, matches the transaction idempotently, and within a single database transaction:
  1. Updates `PaymentTransaction.PaymentStatus = 'Paid'` and `Order.OrderStatus = 'Paid'` (`GBR-37`).
  2. Generates an immutable `Invoice` (`InvoiceId`, `PaymentTransactionId`, `InvoiceNumber`, `IssuedAt`).
  3. Provisions `MemberMembership` (linked to `OrderItemId`) for each purchased component and promotes the beneficiary's role from `Customer` to `Member` if not already a `Member`.

## 4. Membership Freeze, Trainer Recruitment & PT 1-on-1 Workflows (Modules 2, 3, 4)

### 4.1 Membership Freeze (`FreezePolicy` & `MembershipFreeze`)
- Global defaults live in `FreezePolicy` (`EligibilityPaidDays`, `MinimumRequestDays`, `DefaultMaxFreezeDays`, `DefaultMaxFreezeCount`), while component-level overrides live in `PackageComponent` (`AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`).
- Validate advance notice (`GBR-42`), min/max duration per request (`GBR-40`), and remaining cumulative quota (`GBR-41`, `MSG-27`). Weekly Yoga packages cannot be frozen (`GBR-85`).
- Manager reviews and approves/rejects requests (`UC-17`). Upon approval, extend `MemberMembership` expiry date by frozen days and suspend turnstile/booking access while `Frozen` (`GBR-43`). Support early unfreeze recalculation (`GBR-44`).

### 4.2 Trainer Recruitment & Profile (`GBR-01` to `GBR-05`)
- Guests/Customers submit trainer applications with Citizen ID (CCCD), Email, Phone, and certificate attachments (`<= 5MB` PDF/JPG/PNG, `GBR-01`). Enforce 30-day duplicate submission cooldown per CCCD/Email (`GBR-03`) and block blacklisted applicants.
- Submitting an application does **not** create an `ApplicationUser` account (`GBR-02`). Only when the Manager marks the candidate as `Hired` (`UC-23`) and sets Trainer Level + commission tier (`GBR-04`) does the system provision a PT user account.
- PTs can update their public bio and avatar (screened against `BannedWordList`), but new certificates/specialties require Manager approval (`GBR-05`).

### 4.3 Personal Training (PT 1-on-1) Scheduling & PT Freeze (`GBR-06` to `GBR-17`)
- Requires an active General Gym Membership covering the training period (`GBR-10`, `MSG-31`).
- Support PT availability configuration (`UC-26`), schedule option viewing (`UC-27`), and PT package purchase (`UC-29`) with atomic database locking (`GBR-09`, `MSG-30`) to prevent double-booking.
- **PT Freeze (`UC-32`, `UC-33`, `UC-34`):**
  - Governed by `PTFreezePolicy` (min 7 days; cumulative caps: 12 sessions -> 15 days, 24 sessions -> 30 days, 36 sessions -> 60 days per `GBR-15`).
  - Per the finalized Use Case Diagrams, **both Manager and the assigned PT** can review and process PT freeze requests (`UC-34`).
  - Approving a PT freeze cancels scheduled sessions inside the freeze window, releases those dates/slots for temporary booking by other members whose package duration fits inside the freeze window (`GBR-17`), and projects/reschedules remaining sessions from the Resume Date (`GBR-16`).

## 5. Locker Rental, Yoga, Check-in & AI Workout Workflows (Modules 5, 7, 8, 9)

### 5.1 Locker Rental Management (`GBR-18` to `GBR-20`)
- Restrict each Member to at most **1 active locker** at any time (`GBR-18`, `MSG-39`).
- Front-desk Staff manages locker registry, contracts (`Manage Locker Registry & Contracts`), and termination/deposit settlement (`Terminate Locker & Settle Deposit`, 100,000 VND refundable Key Deposit per `GBR-19`, `MSG-40`).
- Staff and Manager handle overdue lockers (`> 7 days` overdue incurs deposit forfeiture and forced clearance per `GBR-20`) and maintenance states.

### 5.2 Yoga & Group Class Management (`YogaStudio`, `YogaClass`, `YogaClassSchedule`, `YogaBooking`)
- `YogaStudio`: Unique studio name per gym + floor number (`GBR-71`); cannot deactivate if future active sessions exist (`GBR-72`).
- `YogaClass` & `YogaClassSchedule`: Enforce min/max participants (`1 <= min <= max`, `GBR-73`), session duration 45-90 minutes with a 15-minute studio buffer (`GBR-75`), no studio or instructor overlap (`GBR-74`), and publication at least 7 days before the first session (`GBR-77`). Rescheduled sessions link back via `RescheduledFromId`.
- `YogaBooking`: Requires active Yoga/Combo membership (`MSG-31`), 1 booking per class (`GBR-79`), automatic waitlist when full (`GBR-80`, `GBR-82`), free cancellation `>= 2 hours` before start (`GBR-83`, `MSG-34`), and automatic 7-day Yoga booking suspension after **3 no-shows within 30 days** (`GBR-84`).
- Assigned PT/Yoga Instructor records attendance (`Attended` / `NoShow`) within 30 minutes of session start (`GBR-86`, `GBR-87`, `UC-51`).

### 5.3 Biometrics & Kiosk Check-in/Check-out (`FaceData`, `QRCode`, `GBR-89` to `GBR-92`)
- Store only encrypted facial embeddings (`VARBINARY(MAX) FaceNetEmbedding` in `FaceData`) after explicit PDPL consent; discard raw camera images immediately and purge embeddings 90 days after last membership expiry (`JOB-07`).
- Validate active membership (`GBR-89`, `MSG-36`) and enforce a **15-minute anti-passback cooldown** (`GBR-90`, `MSG-37`).
- Self check-in within `[-30 mins, +15 mins]` of a scheduled PT session automatically marks that PT session as completed (`GBR-92`, `MSG-38`).
- Staff can perform manual check-in/check-out (`UC-55`, `UC-56`) with Staff UserId logged; Staff and Manager can monitor live occupancy (`View Active Check-ins`, `UC-57`).

### 5.4 AI Personalized Workout Plan (`API-02`, `GBR-93` to `GBR-95`)
- Requires completed Fitness Survey (`GBR-93`). Strip all PII (Name, Email, Phone, UserId) before calling Google Gemini API (`API-02`).
- Only one plan is `Active` at a time; generating a new plan archives the previous active plan (`GBR-94`). If Gemini API times out or fails, automatically assign a goal-matched standard fallback plan (`GBR-95`).

## 6. Competitions, Equipment, Supplements & System Administration (Modules 6, 10, 11, 12, 13)

- **Communication (`GBR-21` to `GBR-23`):** Retain in-app notifications for 90 days. Manager manages `GymAnnouncement` (expiry `<= 60 days`, maximum **3 concurrent pinned announcements**).
- **Competitions (`UC-60` to `UC-66`):** Guest/Customer/Member view competitions & leaderboards; Member registers and views personal history; Staff manages participants and records preliminary results; Manager configures competitions/scoring rules and confirms final results + voucher rewards.
- **Equipment & Procurement (`GBR-53` to `GBR-56`, `GBR-62`, `GBR-68`):** Members report issues (`UC-71`); Staff handles issues (`Resolve`, `Escalate`, `Mark as Safe` in `UC-72`) and submits multi-item `PurchaseProposal` (`UC-74`); Manager manages `GymEquipment`, arranges `EquipmentRepairs`, and approves/rejects purchase proposals (`PurchaseProposalReview`, `UC-75`).
- **Supplement Store (`GBR-57` to `GBR-61`, `GBR-69`, `GBR-70`):** Catalog-only (no stock inventory tracking). Status flow: `Pending -> Processing -> ReadyForPickup / OutForDelivery -> Completed`. Offline payment only (`GymPickup` at counter or `HomeDelivery` COD). Customers/Members can cancel only while `Pending`. Staff and Manager process orders; Manager manages products.
- **Reviews, Banned Words, Business Reports & Audit Logs (`GBR-63` to `GBR-66`):**
  - Screen gym reviews (`UC-67`) against `BannedWordList` (`GBR-66`); Manager replies to feedback (`UC-68`).
  - Manager views read-only historical business metrics (`BusinessReportSnapshot` & `ReportMetric`, `GBR-63`).
  - Admin manages staff accounts, roles, account locks (cannot self-lock per `GBR-64`), banned words, and views read-only `AuditLog` (`GBR-65`).
