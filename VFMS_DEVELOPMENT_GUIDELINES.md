# VFMS Development Guidelines

## 1. Purpose and Scope

These guidelines define the shared architectural, coding, and collaboration conventions for the **Victor Fitness and Gym Management System (VFMS)** (`SEP490_G144_VFMS`). They align the entire codebase with the finalized Software Requirement Specification (SRS Report 3), the 7 Use Case Diagrams (85/87 Use Cases across 13 Modules), and the updated Entity Relationship Diagram (ERD).

## 2. Technology Stack

| Area | Standard |
|---|---|
| Runtime / Framework | .NET 8 (`net8.0`) |
| Architecture | Clean Architecture (6 Core/Presentation Projects + 2 Test Projects) |
| Frontend | Blazor WebAssembly (`VFMS.Client`) + Tailwind CSS |
| Backend API | ASP.NET Core 8 Web API (`VFMS.API`) |
| Shared Contracts | Plain C# DTOs, System Messages & Shared Validators (`VFMS.Contracts`) |
| Application Layer | Use Case Services, Interfaces, Business Rules `GBR-01`..`GBR-95` (`VFMS.Application`) |
| Domain Layer | Entities, Enums, Value Objects, Domain Exceptions (`VFMS.Domain`) |
| Infrastructure & DAL | EF Core 8, SQL Server, Background Workers, External Services (`VFMS.Infrastructure`) |
| Object Mapping | Mapster (centralized profile configurations) |
| Validation | FluentValidation (shared syntax rules in `VFMS.Contracts`, domain state rules in `VFMS.Application`) |
| Authentication | JWT Bearer Access Token (30m) + Refresh Token (7d / 30d) + BCrypt/Argon2 Password Hashing |
| Authorization | Role-based & Permission Matrix authorization (`Guest`, `Customer`, `Member`, `PT`, `Staff`, `Manager`, `Admin`) |
| External Integrations | Google Gemini AI (`API-02`), SePay Webhook (`API-03`), VietQR (`API-04`), MailKit SMTP (`API-05`), Cloudinary |

Use compatible stable package versions centralized in `Directory.Packages.props`.

## 3. Clean Architecture Solution Structure

```text
SEP490_G144_VFMS/
├── src/
│   ├── VFMS.Domain/                  # Enterprise Entities, Enums, BaseEntity, Domain Exceptions (No dependencies)
│   ├── VFMS.Contracts/               # Plain Request/Response DTOs, Validators, MSG Constants (No dependencies)
│   ├── VFMS.Application/             # 13 Module Services (Use Cases), Interfaces, Mapster, Result<T> (Depends on Domain, Contracts)
│   ├── VFMS.Infrastructure/          # VFMSDbContext, EF Core Configurations, Migrations, Repositories,
│   │                                 # External Services (Gemini, SePay, VietQR, MailKit, Cloudinary),
│   │                                 # Identity (JWT, BCrypt/Argon2), BackgroundWorkers (JOB-01..JOB-07)
│   ├── VFMS.API/                     # Controllers, Middlewares (Exception, AuditLog), DI Extensions, Program.cs
│   └── VFMS.Client/                  # Blazor WebAssembly + Tailwind CSS (Depends ONLY on VFMS.Contracts)
└── tests/
    ├── VFMS.UnitTests/               # Domain & Application Business Rule (GBR) unit tests
    └── VFMS.IntegrationTests/        # API endpoint & EF Core integration tests
```

### Dependency Direction Rules

1. **`VFMS.Domain`** and **`VFMS.Contracts`** have **zero** project dependencies.
2. **`VFMS.Application`** depends only on `VFMS.Domain` and `VFMS.Contracts`. It defines interfaces (`IRepositories`, `IUnitOfWork`, `IGeminiAiService`, `ISePayService`, `IVietQrService`, `IEmailService`, `ICloudinaryService`) without knowing database or HTTP implementation details.
3. **`VFMS.Infrastructure`** depends on `VFMS.Application`, `VFMS.Domain`, and `VFMS.Contracts`. It implements persistence with EF Core 8 and external integrations.
4. **`VFMS.API`** depends on `VFMS.Application`, `VFMS.Infrastructure`, and `VFMS.Contracts`.
5. **`VFMS.Client`** (Blazor WASM) depends **strictly on `VFMS.Contracts`**. It communicates with the backend exclusively via HTTP endpoints in `VFMS.API` and never references `VFMS.Domain` or `VFMS.Infrastructure`.

## 4. Coding Style & Conventions

- Use C# 12 (.NET 8) and English for identifiers, comments, commit messages, and technical documentation.
- Follow standard .NET naming: `PascalCase` for types, methods, and properties; `camelCase` for local variables and parameters; `_camelCase` for private fields.
- Keep controllers thin: `Controller` -> `Application Service (Use Case)` -> `Repository / UnitOfWork`.
- Return explicit `Result<T>` / `PagedResult<T>` objects from Application Services using standardized message codes (`MSG-01` to `MSG-43`) or translate domain exceptions into consistent ASP.NET Core Problem Details responses via `GlobalExceptionHandlingMiddleware`.
- Use `async`/`await` for all I/O operations and propagate `CancellationToken` down the call stack.
- Store all timestamps in UTC (`DATETIME2`) and convert to local gym time (`Asia/Ho_Chi_Minh`, UTC+7) at the presentation or scheduling evaluation boundary.

## 5. Core Domain & ERD Alignment Principles

- **Composite Package & Unified Order Model:**
  - `Package` defines the commercial catalog item (`PackageId`, `Name`, `Description`, `Price`, `PackageStatus`).
  - `PackageComponent` defines the entitlements inside a package (`ComponentType` = `'GYM'`, `'YOGA'`, or `'PT'`, `DurationDays`, `DurationMonths`, `SessionCount`, `YogaClassTypeId`, `AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`, `AccessStartTime`, `AccessEndTime`). This supports both single-service packages and multi-service combos.
  - `Order` (`OrderId`, `PurchaserUserId`, `OrderNumber`, `Channel`, `OrderStatus`) and `OrderItem` (`OrderItemId`, `OrderId`, `PackageId`, `BeneficiaryUserId`, `UnitPriceSnapshot`, `PackageNameSnapshot`) unify checkout across Gym, PT, and Yoga packages.
  - `MemberMembership` links directly to `OrderItemId` once payment is confirmed.
- **Voucher Optimization (`GBR-45`, `GBR-46`, `GBR-47`):**
  - `Voucher` enforces date range (`StartAt`, `EndAt`), global quota (`MaxRedemptions`), per-user limit (`MaxRedemptionsPerUser`), minimum order subtotal (`MinimumSpend`), discount type (`PERCENT` or `FIXED`), `DiscountValue`, and maximum discount cap (`MaxDiscountAmount`).
  - `VoucherRedemption` links directly to `VoucherId` and `OrderId` (1 voucher per `Order`, no stacking).
- **Historical & Financial Integrity (`GBR-33`):**
  - Never hard-delete user accounts, packages with historical sales, invoices, payment transactions, competition results, or audit logs. Use status transitions (`Inactive`, `Archived`, `Locked`) or soft-delete flags (`IsDeleted`).

## 6. Authentication, Authorization & Role Hierarchy

- **Role Hierarchy (matching the 7 Use Case Diagrams):**
  - `Guest`: Unauthenticated visitor (Register Account, Submit Trainer Application, View Personal Trainers, View Yoga Schedule, View Competitions & Leaderboard, View Gym Details & Facilities).
  - `Customer`: Inherits `Guest` (except Register Account). Can purchase packages, apply vouchers, view transactions/export invoices, purchase/track/cancel supplements, generate/manage AI workout plans, register Face ID, and view notifications/announcements.
  - `Member`: Inherits `Customer` upon activating a membership (`GBR-37`). Can self check-in/out at kiosk, view/renew membership, submit membership/PT freeze requests, view PT schedule options & purchase PT packages, book/cancel Yoga classes, report equipment issues, register for competitions, view locker rentals, and review gym services.
  - `PT` (Personal Trainer / Yoga Instructor): Manage trainer profile, configure PT availability schedule, view assigned members & PT package progress, process PT freeze requests, view assigned Yoga classes, and take Yoga attendance.
  - `Staff`: Create customer accounts, manual member check-in/out, view active check-ins, handle equipment issues, manage purchase proposals, manage competition participants & results, manage supplement orders, and manage locker registry/contracts/deposits/overdue lockers.
  - `Manager`: Full operational management across Membership Plans, Freeze Policies & Approvals, Promotions/Vouchers, Trainer Application Screening, PT Packages/Policies/Freeze Approvals, Yoga Studios/Classes/Schedules, Equipment & Repairs, Purchase Request Approvals, Supplement Products & Orders, Competitions & Result Confirmation, Customer Feedback, Gym Announcements, General Policies, and Business Reports.
  - `Admin`: System administration including Manage Staff Accounts, Assign Roles, Lock/Unlock Accounts, Manage Banned Words, and View System Audit Logs.
- **JWT Security (`GBR-26`, `GBR-27`, `GBR-28`):**
  - Hash passwords with BCrypt/Argon2 (8-32 chars, uppercase, lowercase, digit, special char).
  - Lock accounts for 15 minutes after 5 consecutive failed logins within 15 minutes.
  - Issue 30-minute JWT Access Tokens and 7-day Refresh Tokens (30 days with Remember Me); blacklist tokens immediately upon logout.

## 7. Background Jobs (`JOB-01` to `JOB-07`)

Implement scheduled background tasks in `VFMS.Infrastructure/BackgroundServices/`:
1. **`JOB-01` (`PaymentOrderExpirationWorker`):** Cancels unpaid online VietQR orders after 15 minutes (`GBR-48`) and counter cash orders after 24 hours, releasing locked slots and voucher reservations.
2. **`JOB-02` (`MembershipLifecycleWorker`):** Activates `Scheduled` memberships on their start date (or auto-activates on day 30 per `GBR-35`) and transitions expired `Active` memberships to `Expired`.
3. **`JOB-03` (`FreezeTransitionWorker`):** Transitions `Approved` membership/PT freezes to `Active` (`Frozen`) on start date, restores `Active` status and extends expiry dates when freeze ends, and expires unapproved `Pending` freeze requests past their start date.
4. **`JOB-04` (`AutoCheckOutWorker`):** Automatically closes open `Active` gym check-ins as `AutoClosed` at gym closing time + 30 minutes or 6 hours after check-in.
5. **`JOB-05` (`CompetitionLifecycleWorker`):** Transitions competitions `Published` -> `Ongoing` -> `Completed` and flags unverified result submissions older than 7 days.
6. **`JOB-06` (`PromotionAndExpiryReminderWorker`):** Dispatches advance expiration reminders for memberships, lockers, and vouchers/promotions.
7. **`JOB-07` (`BiometricAndAccountPurgeWorker`):** Removes unverified accounts after 24 hours and purges `FaceData` (`FaceNetEmbedding`) 90 days after a user's last membership expires (PDPL compliance).

## 8. Git Workflow & Collaboration

### Branches

Use one `main` branch and one persistent development branch per team member:
- `main`
- `dev/viethai`
- `dev/haiduc`
- `dev/ducthang`
- `dev/vinhloc`
- `dev/duchung`

### Commit Messages (Conventional Commits)

Format: `type(scope): description`
- `feat(order): implement multi-package cart checkout and voucher cap validation`
- `feat(yoga): add waitlist auto-notification and 2-hour cancellation check`
- `fix(checkin): enforce 15-minute anti-passback cooldown`
- `docs(architecture): update guidelines to Clean Architecture on .NET 8`

### Pull Requests

- Open PRs from `dev/<member-name>` into `main`.
- Ensure `dotnet build` and unit tests pass before requesting review from the team leader.
- Use **Squash and merge** to maintain a clean linear history on `main`.
