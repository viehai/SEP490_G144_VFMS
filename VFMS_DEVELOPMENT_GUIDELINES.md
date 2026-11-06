# VFMS Development Guidelines

## 1. Purpose and Scope

These guidelines define shared architectural, coding and collaboration conventions for **Victor Fitness and Gym Management System (VFMS)**. For sections 3–6, the revised Report 3 use-case specifications and business rules (including GBR-96/97) are the business baseline. Older diagrams, UC identifiers and model comments may require alignment; guidelines describe target behavior, not implemented feature coverage.

## 2. Technology Stack

| Area | Standard |
|---|---|
| Runtime / Framework | .NET 8 (`net8.0`) |
| Architecture | Clean Architecture (6 Core/Presentation Projects + 3 Test Projects) |
| Frontend | Blazor WebAssembly (`VFMS.Client`) + Tailwind CSS |
| Backend API | ASP.NET Core 8 Web API (`VFMS.API`) |
| Shared Contracts | Plain C# DTOs, System Messages & Shared Validators (`VFMS.Contracts`) |
| Application Layer | Use Case Services, Interfaces, Business Rules through `GBR-97` (`VFMS.Application`) |
| Domain Layer | Entities, Enums, Value Objects, Domain Exceptions (`VFMS.Domain`) |
| Infrastructure & DAL | EF Core 8, SQL Server, Background Workers, External Services (`VFMS.Infrastructure`) |
| Object Mapping | Mapster (centralized profile configurations) |
| Validation | FluentValidation (shared syntax rules in `VFMS.Contracts`, domain state rules in `VFMS.Application`) |
| Authentication | JWT Bearer Access Token (30m) + Refresh Token (7d / 30d) + BCrypt/Argon2 Password Hashing |
| Authorization | Role-based & Permission Matrix authorization (`Guest`, `Customer`, `Member`, `PT`, `Staff`, `Manager`, `Admin`) |
| External Integrations | Google Gemini AI (`API-02`), SePay Webhook (`API-03`), VietQR (`API-04`), MailKit SMTP (`API-05`), Cloudinary |

Currently package versions are declared in individual `.csproj` files. Keep compatible versions consistent; central package management is a future consolidation, not an existing prerequisite.

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
    ├── VFMS.IntegrationTests/        # API endpoint & EF Core integration tests
    └── VFMS.ClientTests/             # Client authentication tests
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
  - `PT` (Personal Trainer / Yoga Instructor): Manage trainer profile, configure availability, view assigned members/progress, propose schedule changes, view/advice on PT freezes (no approval authority), view assigned Yoga classes, take attendance and access own notifications.
  - `Staff`: Create customer accounts, manual member check-in/out, view active check-ins, handle equipment issues, manage purchase proposals, manage competition participants & results, manage supplement orders, and manage locker registry/contracts/deposits/overdue lockers.
  - `Manager`: Full operational management across Membership Plans, Freeze Policies & Approvals, Promotions/Vouchers, Trainer Application Screening, PT Packages/Policies/Freeze Approvals, Yoga Studios/Classes/Schedules, Equipment & Repairs, Purchase Request Approvals, Supplement Products & Orders, Competitions & Result Confirmation, Customer Feedback, Gym Announcements, General Policies, and Business Reports.
  - `Admin`: System administration including Manage Staff Accounts, Assign Roles, Lock/Unlock Accounts, Manage Banned Words, View System Audit Logs and manage Gym Announcements alongside Manager.
- **JWT Security (`GBR-26`, `GBR-27`, `GBR-28`):**
  - Hash passwords with BCrypt/Argon2 (8-32 chars, uppercase, lowercase, digit, special char).
  - Lock accounts for 15 minutes after 5 consecutive failed logins within 15 minutes.
  - Issue 30-minute JWT Access Tokens and 7-day Refresh Tokens (30 days with Remember Me); blacklist tokens immediately upon logout.

## 7. Background Jobs (`JOB-01` to `JOB-07`)

Implement scheduled background tasks in `VFMS.Infrastructure/BackgroundServices/`:
1. **`JOB-01` (`PaymentOrderExpirationWorker`):** Cancels unpaid online VietQR orders after 15 minutes (`GBR-48`) and counter cash orders after 24 hours, releasing locked slots and voucher reservations.
2. **`JOB-02` (`MembershipLifecycleWorker`):** Activates `Scheduled` memberships on their start date (or auto-activates on day 30 per `GBR-35`) and transitions expired `Active` memberships to `Expired`.
3. **`JOB-03` (`FreezeTransitionWorker`):** Activates approved freezes on the effective start date and resumes on Resume Date. PT validity is extended exactly once at approval; the worker must not extend it again. Early resumption recalculates actual frozen days. Expire pending requests past their start date.
4. **`JOB-04` (`AutoCheckOutWorker`):** Automatically closes open `Active` gym check-ins as `AutoClosed` at gym closing time + 30 minutes or 6 hours after check-in.
5. **`JOB-05` (`CompetitionLifecycleWorker`):** Transitions competitions `Published` -> `Ongoing` -> `Completed` and flags unverified result submissions older than 7 days.
6. **`JOB-06` (`PromotionAndExpiryReminderWorker`):** Dispatches advance expiration reminders for memberships, lockers, and vouchers/promotions.
7. **`JOB-07` (`BiometricAndAccountPurgeWorker`):** Removes unverified accounts after 24 hours and purges `FaceData` (`FaceNetEmbedding`) 90 days after a user's last membership expires (PDPL compliance).

## 8. Git Workflow & Collaboration

### Revised sections 3–6 implementation baseline

- PT uses fixed recurring schedules selected at purchase: 12/24/36 credits, validity 60/120/180 days. Generate only purchased credits, check both participants' calendars and Gym Membership coverage, and hold dates for 15 minutes from order creation. Late payments require reconciliation; duplicate callbacks cannot duplicate fulfillment.
- One credit is 60 minutes; a continuous 120-minute appointment uses two. Track Used, Reserved and Available separately. Cancellation at least 4 hours before start releases reserved credits; late cancellation/no-show consumes allocated credits. Schedule changes require member confirmation.
- Recruitment does not create an account until hiring. Link verified existing accounts, preserve history and use setup links for new accounts. GBR-96/97 follow the module 3 simplification decision below.
- Locker online checkout leads to PendingHandover/Reserved, then Active/Occupied after physical handover. Financial settlements and refund completion are explicit records, not inferred from locker status.
- Notification access is recipient-scoped; announcement access is audience-scoped. Manager and Admin publish announcements. PT check-in records attendance rather than immediate completion.
- Scheduled responsibilities must also cover locker collection timeout, notification archival, announcement publication/expiration and email retry. These are required behaviors, not a claim that workers exist yet.

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


### Module 3 simplification decision of 7 November 2026

- Reuse `TrainerApplication` with `ApplicationType` = `Recruitment` or `ProfileUpdate`. Each accepted submission/resubmission is a new row; retain prior attempts. Do not add `TrainerDocument`, `TrainerCredentialChange`, `TrainerRecruitmentRestriction`, `TrainerSetupToken` or `TrainerEmail` tables.
- Apply the existing 30-day email/CCCD cooldown to `Recruitment` only. `ProfileUpdate` allows one pending application per PT, including concurrent requests. This is an explicit implementation decision; confirmation in the policy document remains outstanding.
- PT submits the same full DTO to `POST /api/trainers/applications` with `ApplicationType: ProfileUpdate`. Keep published `TrainerProfile` unchanged until approval. Manager uses `POST /api/trainers/applications/{id}/review`, `Action: Approve` or `Reject` for updates; `Interview`/`Hire` belong to recruitment (which also supports rejection). Remove the separate credential-change endpoints.
- ProfileUpdate email must match the current account email, and Citizen ID must match the original hired application. Matching the original application email is not required: a verified existing account linked by phone may legitimately have a different email.
- Recruitment restriction management updates existing application rows matching the exact email AND Citizen ID pair, with a mandatory Manager reason. No exact pair match returns 404. Historical rows sharing only email or Citizen ID do not prevent management of the matching pair. Submission/hiring restriction checks remain email OR Citizen ID. Unblocking does not waive the Recruitment cooldown or unlock an account.
- Certificate metadata JSON on application/profile is canonical. Legacy URL fields remain for compatibility. New uploads go only to private disk at `TrainerFiles:Root` (`TrainerFiles__Root` environment variable), default `AppContext.BaseDirectory/App_Data/trainer-files`. Preserve old binary content in `LegacyContentBase64` JSON fallback during migration.
- Use `/api/trainers/applications/{id}/documents/{fileId}` for private application files, `/api/trainers/{profileId}/documents/{fileId}` for public approved certificates, and `/api/trainers/{profileId}/avatar`. Route handlers enforce ownership/publication access; never expose raw disk paths. Document `Id` is a string and `DownloadUrl` is supplied; profile `AvatarUrl` replaces `AvatarFileId`.
- Nullable `PasswordSetupTokenHash` and `PasswordSetupExpiresAt` belong to `ApplicationUser`. Login/logout contracts remain unchanged. `EmailQueueJson` on each application holds encrypted email body and retry information. Existing `AuditLog` stores action/record IDs and before/after status only, with no PII content or secrets.
