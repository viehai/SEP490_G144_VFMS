# VFMS Development Guidelines

## 1. Purpose and Scope

These guidelines define the shared architectural, coding, and collaboration conventions for the **Victor Fitness and Gym Management System (VFMS)** (`SEP490_G144_VFMS`). Report 3 is the authority for functional and non-functional requirements and defines 87 Use Cases across 13 feature groups/modules (`UC-01` through `UC-87`). Use Case Diagrams document actor associations. Report 4 and the Entity Relationship Diagram (ERD) inform architecture and data design, and the current source code establishes implementation status.

Reference documents are listed in precedence order for requirements, actor assignments, and design. [Report 3 SRS](https://drive.google.com/file/d/1ikvvPj3Q224wwM_5xqCjP680q0noxfZP/view), [Latest standalone Use Case Diagram](https://drive.google.com/file/d/1UHzNJp126lZMapU8gWvM-cAEkYpmhZ2z/view), [Report 4 SDS](https://drive.google.com/file/d/1ITngkoOxluYMKFVV-Zt4n0e37CfFHTHf/view), [ERD](VFMS%20Erd.drawio.svg).

## 2. Technology Stack

The stack and solution structure below describe VFMS project architecture and development conventions.

| Area | Standard |
|---|---|
| Runtime / Framework | .NET 8 (`net8.0`) |
| Architecture | Clean Architecture (6 Core/Presentation Projects + 3 Test Projects) |
| Frontend | Blazor WebAssembly (`VFMS.Client`) + Tailwind CSS |
| Backend API | ASP.NET Core 8 Web API (`VFMS.API`) |
| Shared Contracts | Plain C# DTOs, System Messages & Shared Validators (`VFMS.Contracts`) |
| Application Layer | Use Case Services, Interfaces, and business rules defined in Report 3 (identifiers up to `GBR-95`, non-contiguous numbering) (`VFMS.Application`) |
| Domain Layer | Entities, Enums, Value Objects, Domain Exceptions (`VFMS.Domain`) |
| Infrastructure & DAL | EF Core 8, SQL Server, Background Workers, External Services (`VFMS.Infrastructure`) |
| Object Mapping | Mapster (centralized profile configurations) |
| Validation | FluentValidation (shared syntax rules in `VFMS.Contracts`, domain state rules in `VFMS.Application`) |
| Authentication | JWT Bearer Access Token (30m) + Refresh Token (7d / 30d) + BCrypt/Argon2 Password Hashing |
| Authorization | Role-based & Permission Matrix authorization (`Guest`, `Customer`, `Member`, `PT`, `Staff`, `Manager`, `Admin`) |

Use compatible stable package versions. The current checkout declares them in individual project files. Use `Directory.Packages.props` if the project adopts central package management.

### SRS External API Inventory

| ID | Service |
|---|---|
| `API-02` | Gemini |
| `API-03` | SePay |
| `API-04` | VietQR |
| `API-05` | Email Gateway |

### Implementation-Specific Integrations

- The current email integration uses MailKit over SMTP.
- Cloudinary remains a design integration for avatars and equipment images. Report 4 also lists facial reference photos, which conflict with Report 3's biometric data minimization requirements. Apply the Report 3 requirements to biometric capture and reconcile the facial-photo design before implementation.

## 3. Clean Architecture Solution Structure

```text
SEP490_G144_VFMS/
├── src/
│   ├── VFMS.Domain/                  # Enterprise Entities, Enums, BaseEntity, Domain Exceptions (No dependencies)
│   ├── VFMS.Contracts/               # Plain Request/Response DTOs, Validators, MSG Constants (No dependencies)
│   ├── VFMS.Application/             # 13 Module Services (Use Cases), Interfaces, Mapster, Result<T> (Depends on Domain, Contracts)
│   ├── VFMS.Infrastructure/          # VFMSDbContext, EF Core Configurations, Migrations, Repositories,
│   │                                 # External service adapters (see inventory and implementation notes above),
│   │                                 # Identity (JWT, BCrypt/Argon2), BackgroundWorkers (JOB-01..JOB-07)
│   ├── VFMS.API/                     # Controllers, Middlewares (Exception, AuditLog), DI Extensions, Program.cs
│   └── VFMS.Client/                  # Blazor WebAssembly + Tailwind CSS (Depends ONLY on VFMS.Contracts)
└── tests/
    ├── VFMS.ClientTests/             # Blazor client tests
    ├── VFMS.UnitTests/               # Domain & Application Business Rule (GBR) unit tests
    └── VFMS.IntegrationTests/        # API endpoint & EF Core integration tests
```

### Dependency Direction Rules

1. **`VFMS.Domain`** and **`VFMS.Contracts`** have **zero** project dependencies.
2. **`VFMS.Application`** depends only on `VFMS.Domain` and `VFMS.Contracts`. Define application-facing interfaces for data access and external services here. Keep database and HTTP implementation details in Infrastructure.
3. **`VFMS.Infrastructure`** depends on `VFMS.Application`, `VFMS.Domain`, and `VFMS.Contracts`. It implements persistence with EF Core 8 and external integrations.
4. **`VFMS.API`** depends on `VFMS.Application`, `VFMS.Infrastructure`, and `VFMS.Contracts`.
5. **`VFMS.Client`** (Blazor WASM) depends **strictly on `VFMS.Contracts`**. It communicates with the backend exclusively via HTTP endpoints in `VFMS.API` and never references `VFMS.Domain` or `VFMS.Infrastructure`.

## 4. Coding Style & Conventions

- Use C# 12 (.NET 8) and English for identifiers, comments, commit messages, and technical documentation.
- Follow standard .NET naming: `PascalCase` for types, methods, and properties; `camelCase` for local variables and parameters; `_camelCase` for private fields.
- Keep controllers thin: `Controller` -> `Application Service (Use Case)` -> `Repository / UnitOfWork`.
- Return explicit `Result<T>` objects from application services, using `PagedResponse<T>` for paginated data. Use the relevant standardized message codes (`MSG-01` to `MSG-43`) and consistent ASP.NET Core Problem Details at the API boundary.
- Use `async`/`await` for all I/O operations and propagate `CancellationToken` down the call stack.
- Store all timestamps in UTC (`DATETIME2`) and convert to local gym time (`Asia/Ho_Chi_Minh`, UTC+7) at the presentation or scheduling evaluation boundary.

## 5. Core Domain & ERD Alignment Principles

The entity names and relationships below describe the ERD and implementation design. The cited GBR entries describe business behavior.

- **Composite Package & Unified Order Model:**
  - `Package` defines the commercial catalog item (`PackageId`, `Name`, `Description`, `Price`, `PackageStatus`).
  - `PackageComponent` defines the entitlements inside a package (`ComponentType` = `'GYM'`, `'YOGA'`, or `'PT'`, `DurationDays`, `DurationMonths`, `SessionCount`, `YogaClassTypeId`, `AllowFreeze`, `MaxFreezeDaysPerRequest`, `MaxFreezeCount`, `AccessStartTime`, `AccessEndTime`). This supports both single-service packages and multi-service combos.
  - `Order` (`OrderId`, `PurchaserUserId`, `OrderNumber`, `Channel`, `OrderStatus`) and `OrderItem` (`OrderItemId`, `OrderId`, `PackageId`, `BeneficiaryUserId`, `UnitPriceSnapshot`, `PackageNameSnapshot`) unify checkout across Gym, PT, and Yoga packages.
  - `MemberMembership` links directly to `OrderItemId` once payment is confirmed.
- **Voucher Optimization (`GBR-45`, `GBR-46`, `GBR-47`)**
  - Validate voucher dates (`StartAt`, `EndAt`), global quota (`MaxRedemptions`), and minimum order subtotal (`MinimumSpend`). Apply the discount type (`PERCENT` or `FIXED`), `DiscountValue`, and the configured percentage discount cap (`MaxDiscountAmount`). Transition the voucher to `Expired` when its global quota is reached or its campaign end date passes.
  - `VoucherRedemption` links directly to `VoucherId` and `OrderId` (1 voucher per `Order`, no stacking).
  - **Optional implementation extension beyond Report 3.** The ERD and current `Voucher` entity contain `MaxRedemptionsPerUser` for a possible per-user quota.
- **Membership Package Retention (`GBR-33`)**
  - When deletion is requested for a membership package with existing sales records or active subscriptions, block permanent deletion and transition it to `Inactive`. Preserve its historical records and existing member entitlements.
- **System Audit Logging (`GBR-65`)**
  - Record privileged membership plan and membership policy changes in the System Audit Log. Keep the logs read-only and retain them for 12 months per the Report 3 security and data privacy NFR.
- **Supplement Catalog (`GBR-69`)**
  - Maintain catalog availability without tracking stock quantities. `MSG-41` describes insufficient stock. This discrepancy is pending SRS clarification.
  - `GBR-70` limits the customer and member catalog to `Available` products. The current store API also returns `OutOfStock` products.

## 6. Authentication, Authorization & Role Hierarchy

- **Role Hierarchy (Use Case Diagram guidance):**
  - `Guest`: Unauthenticated visitor (Register Account, Submit Trainer Application, View Personal Trainers, View Yoga Schedule, View Competitions & Leaderboard, View Gym Details & Facilities).
  - `Customer`: Inherits `Guest` (except Register Account). Can purchase packages, apply vouchers, view transactions/export invoices, purchase/track/cancel supplements, generate/manage AI workout plans, register Face ID, and view notifications/announcements.
  - `Member`: Inherits `Customer` permissions upon activating a membership. Can self check-in/out at kiosk, view/renew membership, submit membership/PT freeze requests, view PT schedule options & purchase PT packages, book/cancel Yoga classes, report equipment issues, register for competitions, view locker rentals, and review gym services.
  - `PT` (Personal Trainer / Yoga Instructor): Manage trainer profile, configure PT availability schedule, view assigned members & PT package progress, view assigned Yoga classes, and take Yoga attendance as the instructor.
  - `Staff`: Create customer accounts, manual member check-in/out, view active check-ins, handle equipment issues, manage purchase proposals, manage competition participants & results, manage supplement orders, manage locker registry/contracts/deposits/overdue lockers, and submit Yoga attendance under `GBR-86`.
  - `Manager`: Full operational management across Membership Plans, Freeze Policies & Approvals, Promotions/Vouchers, Trainer Application Screening, PT Packages/Policies/Freeze Approvals, Yoga Studios/Classes/Schedules, Equipment & Repairs, Purchase Request Approvals, Supplement Products & Orders, Competitions & Result Confirmation, Customer Feedback, Gym Announcements, General Policies, and Business Reports.
  - `Admin`: System administration including Manage Staff Accounts, Assign Roles, Lock/Unlock Accounts, Manage Banned Words, and View System Audit Logs.

  **PT freeze review authorization requires reconciliation.** The latest standalone Use Case Diagram and the Report 3 §II.4.10 precondition assign processing to Manager (`UC-34`). The Report 3 §4.4 matrix and embedded PT diagram grant PT access, and §II.4.10 lists PT as a secondary actor. Use the Manager review flow pending clarification of PT authority.

  **Yoga attendance authorization requires reconciliation.** `GBR-86` and Report 3 §II.7.9 allow the instructor or Staff to submit the roster within 30 minutes after the session starts. The §4.4 matrix grants access to PT and denies it to Staff, and the embedded Staff diagram omits the use case. The master list identifies Take Yoga Attendance as `UC-51`. The attendance rule's reference to `UC-67` also requires correction because the master list assigns that ID to Review Gym & Services.

  **Gym Announcement authorization requires SRS/UCD reconciliation.** `GBR-22` allows Manager and Admin to create and publish announcements. Report 3 §II.6.2 lists both as primary actors but gives a Manager-only precondition, and the §I.4.4 matrix denies Admin. The diagrams link Manage Gym Announcements (`UC-42`) directly to Manager. The standalone diagram also models Admin as a specialization of Manager. Resolve the discrepancy before changing route or API authorization.

  **Guest access requires reconciliation.** The latest Use Case Diagram associates Guest with trainer applications, trainer views, and the Yoga schedule. The Report 3 security NFR lists a narrower set of unauthenticated exceptions covering login, registration, password reset, and public gym and competition information. Resolve the discrepancy before expanding anonymous route or API access.

- **JWT Security (`GBR-26`, `GBR-27`, `GBR-28`):**
  - Hash passwords with BCrypt/Argon2 (8-32 chars, uppercase, lowercase, digit, special char).
  - Lock accounts for 15 minutes after 5 consecutive failed logins within 15 minutes.
  - Issue 30-minute JWT Access Tokens and 7-day Refresh Tokens (30 days with Remember Me); blacklist tokens immediately upon logout.
  - **Implementation gaps.** The current login flow checks account lock status, with automatic failed-attempt lockout still outstanding. Logout revokes the refresh token. Immediate access-token invalidation remains outstanding.

- **Biometric data handling (SRS NFR)**
  - Obtain explicit biometric consent before Face ID registration, store only encrypted facial feature embeddings, and discard raw camera images immediately after extraction.
  - Delete facial templates immediately at the user's request. `JOB-07` covers the separate automatic deletion path 90 days after the user's last membership expires.

## 7. Background Jobs (`JOB-01` to `JOB-07`)

Use `VFMS.Infrastructure/BackgroundServices/` for scheduled worker implementations. Worker class names are project conventions.

1. **`JOB-01` (`PaymentOrderExpirationWorker`):** Cancels unpaid online VietQR orders after 15 minutes (`GBR-48`) and counter cash orders after 24 hours, releasing locked slots and voucher reservations.
2. **`JOB-02` (`MembershipLifecycleWorker`):** Activates `Scheduled` memberships on their start date (or auto-activates on day 30 per `GBR-35`) and transitions expired `Active` memberships to `Expired`.
3. **`JOB-03` (`FreezeTransitionWorker`):** Transitions `Approved` membership freezes to `Active` on their start date and sets the membership to `Frozen`. Restores `Active` status and extends expiry dates when the freeze ends, and expires unapproved `Pending` freeze requests past their start date.
4. **`JOB-04` (`AutoCheckOutWorker`):** Automatically closes open `Active` gym check-ins as `AutoClosed` at gym closing time + 30 minutes or 6 hours after check-in.
5. **`JOB-05` (`CompetitionLifecycleWorker`):** Transitions competitions `Published` -> `Ongoing` -> `Completed` and flags result submissions still pending more than 7 days after the competition ends.
6. **`JOB-06` — Loyalty Points Expiry & Reminder Job.** Sends expiration reminders 30 days in advance and expires unused Loyalty Points older than 12 months using FIFO.

   **Pending SRS clarification.** The job inventory refers to Loyalty Points, while the master use-case list focuses on Vouchers. Confirm the intended scope before implementing this job.

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
