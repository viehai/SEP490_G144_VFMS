# VFMS Frontend Guidelines

## 1. Purpose

This document defines architecture, component, workflow, and styling conventions for the **`VFMS.Client`** Blazor WebAssembly + Tailwind CSS project inside the `SEP490_G144_VFMS` Clean Architecture solution. The frontend handles presentation, client-side validation, and role-specific navigation. The backend owns business rules, payment confirmation, and persistence.

Report 3 governs functional and non-functional requirements. Use Case Diagrams (UCDs) describe actor and use-case associations. Report 4 and the ERD document architecture and entity design. The technology stack and UI design in this guide are project conventions. Use the current source code to confirm implementation status. [Report 3 SRS](https://drive.google.com/file/d/1ikvvPj3Q224wwM_5xqCjP680q0noxfZP/view), [Use Case Diagrams](https://drive.google.com/file/d/1UHzNJp126lZMapU8gWvM-cAEkYpmhZ2z/view), [Report 4 SDS](https://drive.google.com/file/d/1ITngkoOxluYMKFVV-Zt4n0e37CfFHTHf/view).

## 2. Responsibilities and Architectural Boundaries

- `VFMS.Client` is built with **Blazor WebAssembly on .NET 8 (`net8.0`)**, using **Tailwind CSS** and component-specific CSS where appropriate.
- Reference **only `VFMS.Contracts`** for shared Request/Response DTOs, FluentValidation validators, and standardized system message constants (`MSG-01` to `MSG-43`).
- Never reference `VFMS.Domain`, `VFMS.Application`, or `VFMS.Infrastructure`.
- Use typed `HttpClient` services for `VFMS.API` REST calls.
- Client-side validation and role-based UI hiding (`<AuthorizeView Roles="...">`) improve UX, but server validation and authorization are always authoritative.

## 3. Project Organization (`VFMS.Client`)

### Current public-facing implementation

- `Pages/Home.razor` is the public landing page and uses `Layout/PublicLayout.razor`.
- `Pages/FacilityMap.razor` is an interactive component embedded in Home; its styles live in `wwwroot/css/facility-map.css`.
- `Pages/Auth/Login.razor` (`/auth/login`) and `Pages/Auth/Register.razor` (`/auth/register`) use `Layout/AuthLayout.razor` and `wwwroot/css/auth.css`. Both forms use shared validators from `VFMS.Contracts`; registration does not sign the user in automatically. Logout is a direct action through `Components/Auth/LogoutButton.razor`, without a separate page.
- `wwwroot/index.html` loads the local `wwwroot/css/tailwind.css` stylesheet. When adding Tailwind classes or changing the theme, run `npx --yes tailwindcss@3.4.17 -c tailwind.config.js -o wwwroot/css/tailwind.css --minify` from `src/VFMS.Client` and commit the generated CSS. Startup screen styles are in `wwwroot/css/splash.css`.
- Logo and equipment images used by Home are in `wwwroot/brand/`, `wwwroot/images/`, and `wwwroot/equipments/`.
- **Home hero image convention:** use the local file `wwwroot/images/hero-bg.png`, referenced from Razor as `/images/hero-bg.png`. Do not apply `grayscale`, `contrast-*`, or `brightness-*` utility filters to this image. Render it with `object-cover` and preserve its natural gym lighting. If text readability needs support, use a separate CSS/Tailwind overlay rather than permanently editing the image.
- **Auth branding image convention:** use the local file `wwwroot/images/auth.png`, referenced as `/images/auth.png`, for the Login/Register branding panel. The asset should remain a realistic equipment still-life (kettlebell, dumbbells, rolled mat, towel, bottle, gym interior) with no generated faces, text, logo, baked-in red ribbon, or UI. Any darkening, red accent, logo, typography, or decorative ribbon belongs in the Auth layout/CSS so the source image remains reusable.
- Signed-in Customers and Members see the active Gym membership plan catalog from `api/membership-plans` on Home. The remaining sample pricing, class, equipment, and facility-map data are illustrative. Use API-backed data for live operations.

### Running the authentication screens locally

- Start `VFMS.API` with its HTTPS profile and start `VFMS.Client`. The client's `wwwroot/appsettings.Development.json` sets `ApiBaseUrl` to `https://localhost:7148/`, matching the API launch profile. Update this setting when using a different API URL; never put secrets in client configuration.
- The API allows the development client origins `https://localhost:7246` and `http://localhost:5273`. For other origins or deployment, set the API's `Cors:AllowedOrigins` array explicitly. Outside Development, the client defaults to its own origin unless `ApiBaseUrl` is configured in client settings.
- Login accepts email or phone number. Unchecked "Remember me" stores the token session in session storage; checked stores it in local storage. Passwords are never persisted. `AuthSession` restores the session and rotates an expired access token through `api/auth/refresh` before returning authenticated state. Server-side authorization remains authoritative.
- After sign-in, Login awaits the authenticated principal before choosing a destination through `AuthNavigation`. A safe local return URL takes precedence only when its actual page authorization permits the user; otherwise Admin, Manager, Staff, and PT go directly to their existing `/admin/dashboard`, `/manager/dashboard`, `/staff/dashboard`, or `/pt/dashboard`. Customer/Member default to Home and retain valid return destinations. Multi-role landing preference is **Admin → Manager → Staff → PT**, a navigation preference rather than inherited authorization. Restored workspace users visiting Login follow the same destination selection.
- `App.razor` provides authentication state through the `CascadingAuthenticationState` component around the Router. Keep this single cascade source: service-based cascading in the current .NET 8 packages can fail when logout synchronously removes protected-route subscribers during notification.
- Register sends `RegisterRequest` to `api/auth/register`, then shows a success screen linking to Login. Validation errors appear beside the relevant fields; API/network failures remain on the form so the user can retry. API-backed password reset and change screens are outside this implementation's current scope; the profile preview below includes only a local password-form demo.
- Logout is available from authenticated public navigation. Clicking the button posts the current refresh token to `api/auth/logout` with a Bearer access token, refreshing access first if needed. Clear the in-memory session, session storage, and local storage, publish anonymous UI state, then return to Home. No confirmation or completion screen is shown. An API/network failure still ends the local session and returns Home; local cleanup alone does not guarantee server-side revocation.
- **Authentication compliance gaps.** The API currently activates self-registered accounts immediately, while `UC-01` requires email verification before activation. The login flow checks existing account locks, while the failed-attempt lockout required by `GBR-27` is still pending. Logout currently revokes the submitted refresh token. Immediate access-token blacklisting under `GBR-28` is still pending.
- Run authentication client tests with `dotnet test tests/VFMS.ClientTests/VFMS.ClientTests.csproj`. These tests use fake HTTP/storage and do not create database accounts; verify the full registration/login flow separately against a configured API and database.

### Dashboard and profile UI previews

- `Pages/Admin/AdminDashboard.razor` (`/admin/dashboard`) and `Pages/Manager/ManagerDashboard.razor` (`/manager/dashboard`) use `ManagerAdminLayout`. The existing black/red and slate/indigo preview colors are **legacy styling to be replaced** by the unified Soft Black / Muted Red / Warm Neutral design system in Section 6.
- `Pages/Staff/StaffDashboard.razor` (`/staff/dashboard`) and `Pages/PT/PTDashboard.razor` (`/pt/dashboard`) use `StaffPTLayout`. The existing black/white preview styling is also **legacy styling to be replaced**. Each dashboard may retain its page header, stat cards, quick actions, and data panel structure while adopting the shared tokens and components.
- `Pages/UserProfile.razor` (`/profile`) uses `PublicLayout`, sharing Home's top navigation and footer rather than a dashboard sidebar. Clicking the signed-in greeting opens Profile; a compact "Hồ sơ" link remains visible on smaller screens. Profile styles live in `wwwroot/css/profile.css`. Restyle the public navigation, profile controls, and footer with the shared Soft Black / Muted Red / Warm Neutral palette; avoid pitch-black page chrome and pure-white full-page surfaces.
- Profile edits and the password form currently validate local mock data only. No profile or password API request is sent. Birthdate is a UI preview field and is not yet part of the existing profile DTO.
- The two dashboard layouts reuse `Components/Workspace/WorkspaceShell.razor` and `wwwroot/css/workspace.css`. Keep Montserrat for headings/statistics and Inter for body text/forms, but replace the old `rounded-none`-everywhere convention with the restrained 8–12px radius rules in Section 6.
- These dashboards retain **mock data** with visible labels, and quick actions change local preview panels without performing operational actions. Each dashboard now requires its matching authenticated role through `AuthorizeRouteView`; operational API authorization remains separate and authoritative. The cross-role preview selector has been removed, and workspace headers offer logout for switching accounts.
- The Manager dashboard links to the live UC-11 page at `/manager/membership-plans`. The page and its management API both enforce the `Manager` role, and the backend audits plan changes. Checkout and membership freeze policy editing remain planned workflows.

### Planned organization

The following is a target structure, not a claim that all folders, layouts, components, or services already exist:

```text
VFMS.Client/
├── Pages/                          # Organized by the 7 Actors + Kiosk Mode
│   ├── Public/                     # Guest views: Home, GymDetails, Trainers, YogaSchedule, Competitions, TrainerApplication
│   ├── Auth/                       # Login, Register, VerifyOtp, ResetPassword
│   ├── Customer/                   # CustomerDashboard, PackageCatalog, Checkout, Transactions, Supplements, AiWorkout, FaceSetup
│   ├── Member/                     # MemberDashboard, MyMemberships, FreezeRequests, PTSchedule, PTProgress, MyYogaBookings, MyLocker, EquipmentReport, GymReview
│   ├── PT/                         # TrainerProfile, AvailabilityConfig, AssignedMembers, AssignedYogaClasses, YogaAttendance
│   ├── Staff/                      # WalkInAccount, ManualCheckInOut, ActiveCheckIns, LockerRegistry, LockerTermination, OverdueLockers, EquipmentIssues, PurchaseProposals, CompetitionRoster, SupplementOrders, YogaAttendance
│   ├── Manager/                    # MembershipPlans, FreezePolicies, FreezeApprovals, PTFreezeReview, Promotions, TrainerScreening, PTPackages, YogaStudiosAndClasses, EquipmentAndRepairs, ProposalApprovals, SupplementCatalog, Announcements, FeedbackManagement, BusinessReports, GeneralPolicies
│   ├── Admin/                      # StaffAccountsAndRoles, AccountLockManagement, BannedWords, SystemAuditLogs
│   └── Kiosk/                      # Entrance/Exit Turnstile Web Kiosk (FaceID Camera & Dynamic QR Scanner)
├── Layout/                         # Role-segregated layouts
│   ├── PublicLayout.razor          # Guest & public browsing
│   ├── MemberLayout.razor          # Customer & Member portal
│   ├── StaffPTLayout.razor         # Front-desk Staff & Personal Trainer workspace
│   ├── ManagerAdminLayout.razor    # Manager & Admin back-office portal
│   └── KioskLayout.razor           # Full-screen distraction-free Turnstile Check-in/Check-out screen
├── Components/                     # Reusable UI building blocks (StatusBadge, ConfirmDialog, Pagination, VietQrModal, CameraCapture)
│   └── UI/                         # Base Gymshark-styled components (GymButton, ClassCard)
├── Services/                       # Typed HttpClient wrappers (AuthApiClient, OrderApiClient, YogaApiClient, CheckInApiClient...)
├── Auth/                           # JwtAuthStateProvider, TokenRefreshHandler, Local/Session TokenStorageService
├── Styles/input.css                # Tailwind directives & global CSS theme variables
├── tailwind.config.js              # Tailwind configuration mapped to VFMS athletic theme
└── wwwroot/                        # Compiled css, images, JS interop for Webcam/QR/PDF download
```

## 4. Authentication, JWT Handling & Role UX

- **JWT Bearer Transport.** `GBR-28` limits access token validity to 30 minutes. Use `AuthSession` to obtain and refresh bearer tokens for authenticated API calls. Use `MSG-11` when the session cannot be restored and the user must sign in again.
- **Role-based navigation and the use case diagrams**
  - The UCD links `Guest` to `Register Account`, password reset, `Submit Trainer Application`, `View Personal Trainers`, `View Yoga Schedule`, competition information, and `View Gym Details & Facilities`.
  - `Customer`: Inherits `Guest` (except `Register Account`). Can access `Purchase Membership Packages`, `Apply Voucher`, `View Transaction History`, `Export Invoice`, `Purchase/Track/Cancel/View Supplements`, `Generate/Manage Personalized Workout Plan`, `Manage Face Recognition Profile`, and `View Notifications / Gym Announcements`.
  - `Member`: Inherits `Customer`. Unlocks `View Membership Details`, `Renew Membership`, `Manage & Submit Membership Freeze Request`, `View PT Schedule Options`, `Purchase PT Package`, `View PT Package Progress`, `Manage PT Freeze Request`, `Book/Cancel/View Yoga Bookings`, `Check In / Check Out`, `Report Equipment Issue`, `Manage Competition Registration / History`, `View My Locker Rental`, and `Review Gym & Services`.
  - `PT` can access `Manage Trainer Profile`, `Configure PT Availability Schedule`, `View Assigned Members`, `View PT Package Progress`, and `View Assigned Yoga Classes`. A PT acting as Yoga Instructor may take attendance.
  - `Staff` can access `Create Customer Account` (locked to `Customer` role per `GBR-30`), `Check In/Out Member`, `View Active Check-ins`, `Handle Equipment Issues`, `Manage Purchase Proposals`, `Manage Competition Participants & Results`, `Manage Supplement Orders`, `Manage Locker Registry & Contracts`, `Terminate Locker & Settle Deposit`, `Handle Overdue Lockers & Maintenance`, and `View Notifications / Gym Announcements`. `GBR-86` also permits Staff to submit Yoga attendance.
  - `Manager` can access operational configuration, approvals including `Process PT Freeze Requests` (`UC-34`), product/schedule management, and `View Business Reports` screens per the UCD.
  - `Admin`: Accesses `Manage Staff Accounts`, `Assign Roles`, `Lock/Unlock Account`, `Manage Banned Words`, `View System Audit Logs`, and `View Notifications / Gym Announcements`.
- **Guest access requires SRS/UCD reconciliation.** The Report 3 Security NFR lists login, registration, password reset, and public Gym and Competition information as sign-in exceptions. Reconcile its scope with the UCD's Guest links for trainer applications, trainer browsing, and Yoga schedule before configuring public routes.
- **Gym Announcement authorization requires SRS/UCD reconciliation.** `GBR-22` permits Manager and Admin to create and publish announcements. The detailed use case (§II.6.2) lists both as primary actors but gives a Manager-only precondition, and the §I.4.4 matrix denies Admin. The diagrams link `Manage Gym Announcements` (`UC-42`) directly to Manager. The standalone diagram also models Admin as a specialization of Manager. Resolve the discrepancy before changing route or API permissions.

## 5. UI Handling for Updated VFMS Workflows

### 5.1 Unified Multi-Service Cart, Order, Voucher Cap & VietQR Checkout
- Render single-service and combo packages from `Package` and `PackageComponent` (`GYM`, `YOGA`, `PT`), clearly displaying duration, session count, access hours, and freeze allowances.
- Allow adding multiple packages into a single `Order` cart, selecting activation start dates (immediate or deferred up to 30 days per `GBR-35`), and specifying `BeneficiaryUserId` (buying for self or gifting per `GBR-36`).
- On the Checkout screen, support applying a single voucher code (`GBR-45`). Display the subtotal, minimum spend requirement (`MinimumSpend`), percentage cap (`MaxDiscountAmount` per `GBR-46`), and calculated `DiscountAmount` returned by the API.
- Display the dynamic VietQR code (`API-04`) with a live **15-minute countdown timer** (`GBR-48`, `JOB-01`). In the planned checkout flow, SePay sends its webhook (`API-03`) to `VFMS.API`, which verifies the payment and persists the result. The frontend can poll a backend payment-status endpoint or receive server-pushed status updates. Show Payment Success (`MSG-23`) and enable PDF Invoice download (`Export Invoice`) only after the backend confirms payment.

### 5.2 Membership Freeze, PT Scheduling & PT Freeze
- Display remaining freeze quota, min/max days (`GBR-40`, `GBR-41`), and advance notice rules (`GBR-42`). Disable freeze actions for weekly Yoga packages (`GBR-85`).
- For PT booking (`UC-27`, `UC-29`), display the backend's eligibility result for an active gym membership covering the training period (`GBR-10`, `MSG-31`). Render available PT schedule options and temporary freeze-window availability notices (`GBR-17`).
- Provide the `UC-34` review, approval, and rejection workflow for `Manager` according to the standalone UCD. Keep PT navigation for assigned-member and scheduling workflows. Report 3 §4.4 grants both roles full access to PT freeze review, and its embedded PT diagram (§I.4.3.7) also links PT to this action. The detailed flow (§II.4.10) lists Manager as primary, PT as secondary, and sets a Manager-only precondition. Resolve the discrepancy before granting PT review access.
- **PT scheduling granularity requires SRS clarification.** The master list describes day-based availability in `UC-27` and `UC-32`, while `GBR-06` through `GBR-09` use time slots. Render backend availability and keep collision checks on the server.
- Membership plan and membership policy changes must be recorded by the backend in the System Audit Log (`GBR-65`). Present the result returned by the API.

### 5.3 Yoga Timetable, Waitlist, No-Show Penalty & Attendance
- Display weekly Yoga sessions (`View Yoga Schedule`) with studio name, floor, instructor, difficulty, and live free-seat counter.
- When `Confirmed` bookings reach `MaxParticipants`, change the primary CTA from `"Book Class"` to `"Join Waitlist (Free)"` (`GBR-80`, `GBR-82`).
- When cancelling a booking `< 2 hours` before start time, use a confirmation dialog to explain that late cancellation counts as a no-show (`GBR-83`) and that 3 no-shows within 30 days suspend Yoga booking for 7 days (`GBR-84`). The `MSG-34` context labels late cancellation as denied, while the rule and message text describe a penalty. Preserve the penalty behavior pending clarification.
- Provide the `Take Yoga Attendance` roster for the assigned Yoga Instructor (`PT`) and for `Staff` (`UC-51`). `GBR-86` requires submission within 30 minutes after the session starts. Display each booked member's `Attended` or `NoShow` state. The session takes place even if nobody attends (`GBR-87`).
- **Attendance permission and timing discrepancies.** Report 3 §4.4 grants roster submission to PT and excludes Staff, consistent with its embedded diagrams. The detailed use case (§II.7.9) and `GBR-86` allow Instructor or Staff. The detailed use case saves late submissions and flags them for Manager, while `GBR-86` specifies the 30-minute window. Reconcile late handling before enforcing a cutoff in the UI. The current PT dashboard contains an attendance preview. Roster submission remains a planned workflow.
- **SRS cross-reference discrepancy.** The rule table points `GBR-86` and `GBR-87` to `UC-67`, while the master list names `UC-51` as `Take Yoga Attendance`.

### 5.4 Locker Rental, Kiosk Biometrics & AI Workout Plan
- **Locker Rental.** Members view active locker details, 100,000 VND deposit status, and 7-day overdue grace warnings (`View My Locker Rental`). Staff manages the interactive locker grid (`Available`, `Occupied`, `Overdue`, `Maintenance`), key handover, deposit settlement, and forced clearance with Manager. Show `MSG-40` when the locker is returned and the deposit is refunded.
- **Biometrics & Kiosk (`UC-52` to `UC-57`).** Require explicit PDPL biometric consent before opening the camera, using an unchecked-by-default consent checkbox on Face ID registration. The biometric workflow must discard raw camera images immediately after extraction and persist only encrypted facial feature embeddings. Provide a user-facing action that requests immediate backend deletion of the facial template. `JOB-07` separately deletes the template automatically 90 days after the user's last membership expires. On the Kiosk screen, display `MSG-35` on successful Face ID check-in, `MSG-36` on inactive membership or face mismatch, `MSG-37` on 15-minute anti-passback violation, and `MSG-38` when a PT session is auto-completed.
- **Face ID eligibility requires SRS clarification.** The detailed `UC-52` flow (§II.8.1) names Customer and Member as actors and requires an active or pending membership. Confirm eligibility for Customers without such a membership before configuring access to enrollment.
- **AI Workout Plan (`UC-58`, `UC-59`):** Require PDPL Health Survey consent, show a non-blocking loading state while Gemini (`API-02`) generates the weekly routine, notify the user if a fallback standard plan was used (`GBR-95`), and allow daily exercise completion toggling.

### 5.5 Equipment, Procurement, Supplements, Competitions & Moderation
- **Equipment & Procurement:** Member submits issue reports (`UC-71`); Staff triages issues (`Resolve`, `Escalate`, `Mark as Safe`) and builds multi-item purchase proposals with dynamic total budget calculation (`GBR-62`); Manager manages repairs, equipment lifecycle, and proposal approvals (`GBR-68`).
- **Supplement Store:** Display `Available` products without stock counts (`GBR-69`, `GBR-70`). Support `GymPickup` (pay at counter) and `HomeDelivery` (COD) (`GBR-58`). Show `"Cancel Order"` button only while status is `Pending` (`GBR-60`).
- **Supplement stock requires SRS clarification.** `GBR-69` specifies a catalog without stock-quantity tracking, while `MSG-41` describes insufficient stock. Keep the catalog-only model pending clarification.
- **Supplement visibility implementation gap.** The current store API also returns products marked `OutOfStock`. `GBR-70` limits Customer and Member visibility to `Available` products.
- **Reviews & Banned Words:** Highlight offending words inline if a gym review or trainer bio triggers the `BannedWordList` filter (`GBR-66`, `GBR-05`).

---

## 6. Design System & UI Vibe (Soft Black / Muted Red / Warm Neutral Fitness)

The VFMS visual direction is a **strong but softened premium fitness product** built around charcoal typography, muted red accents, warm neutral surfaces, realistic gym photography, restrained geometry, and enterprise-grade information hierarchy. The product should feel athletic and intentional without becoming pitch-black, red-heavy, overly white, or obviously AI-generated.

### A. Global Color Tokens

Use the same core brand tokens across Guest, Customer, Member, PT, Staff, Manager, Admin, Auth, and Kiosk experiences. Role differentiation should come from navigation, permissions, workflows, icons, labels, and data — **not from completely different color themes**.

```css
:root {
    --vfms-bg: #F2EFEC;

    --vfms-surface: #F8F5F2;
    --vfms-surface-alt: #ECE8E4;
    --vfms-surface-strong: #FFFFFF;

    --vfms-charcoal: #252323;
    --vfms-charcoal-soft: #2B2928;

    --vfms-text: #242424;
    --vfms-text-secondary: #6F6966;
    --vfms-text-muted: #8C8581;

    --vfms-primary: #B83A3A;
    --vfms-primary-hover: #9F3030;
    --vfms-primary-dark: #7F2929;
    --vfms-primary-soft: #F3E4E2;

    --vfms-border: #D8D1CD;
    --vfms-border-strong: #C9C0BB;
}
```

Recommended visual balance:
- **70–80%** warm neutral / light surfaces.
- **15–20%** charcoal / soft black.
- **5–10%** muted red accents.

Do not use pure black `#000000` as the default page background. Do not use neon red, purple/blue gradients, or role-specific brand palettes. The brand red is an accent color, not a replacement for semantic success/warning/error colors.

### B. Layout Direction by Product Area

#### Public / Guest / Customer / Member
- Use `#F2EFEC`, `#F8F5F2`, and `#ECE8E4` as the dominant canvas.
- Use realistic gym photography with natural or cinematic lighting.
- Use charcoal for typography and muted red for CTA, active navigation, selected states, small section labels, dividers, and important links.
- Avoid pure-white full-page backgrounds. White may be used selectively where stronger separation is needed.
- Keep sections spacious and editorial without wrapping every block in a card.

#### PT / Staff / Manager / Admin Back-Office
- Use a **light enterprise dashboard** for every role.
- App background: `#F2EFEC`.
- Sidebar: `#F8F5F2`.
- Active sidebar item: `#F3E4E2`.
- Active icon/text: `#B83A3A`.
- Cards, tables, and forms: normally `#F8F5F2`; use `#FFFFFF` only where stronger separation is useful.
- Borders: `#D8D1CD`.
- Do not restore separate black/red Admin, navy/indigo Manager, or black/white Staff/PT themes.
- Keep management screens practical: page title + contextual description + toolbar + filters + data table/list + pagination + drawer/modal/form when appropriate.

#### Auth
Auth is allowed to be more brand-driven than the rest of the application, while still using the same tokens.

Desktop Login/Register:
- Use an approximate **66–68% branding/image panel + 32–34% form panel** split.
- Left branding image: `/images/auth.png`.
- The asset itself must contain **no text, no logo, no UI, and no baked-in effects**.
- Apply charcoal darkening and red decorative elements in CSS/layout layers, not in the source image.
- Use the actual Victor Fitness logo asset from the project.
- Right panel background should be warm beige/warm gray (`#ECE8E4` or nearby), **not pure white**.
- Auth card surface should be around `#F4F0ED`, with a restrained border and subtle shadow.
- The form must not become the single brightest object on screen; visual attention should remain balanced between the branding image and form.
- Login and Register must reuse the same Auth layout and visual system.

#### Kiosk
- Kiosk may use stronger contrast for distance readability.
- Maintain charcoal / warm neutral / muted red brand identity.
- Success, warning, and error colors remain semantic and may be more prominent when operationally necessary.

### C. Typography

- **Headings, navigation, banners, major statistics, hero/auth branding, primary CTAs:** `'Montserrat', sans-serif`.
  - Weight: `700`–`800`; `900` only for short major statements where it remains readable.
  - Uppercase is appropriate for short headlines, labels, navigation, and CTA text.
- **Body copy, tables, forms, validation messages:** `'Inter', sans-serif`, `400`–`500`.
- Avoid oversized typography used only to mimic AI landing-page aesthetics.
- Use type scale, spacing, and contrast to create hierarchy instead of filling the page with cards or decoration.

### D. Geometry, Shadows & Components

- Buttons / inputs / selects: `8px` radius.
- Cards / panels / modals / drawers: `10px`–`12px`.
- Auth card may use `12px`–`14px`.
- Status badges may use a pill shape when semantically appropriate.
- Avoid excessive `rounded-2xl` / `rounded-3xl`.
- Prefer border-first surfaces.
- Shadows should be soft and low elevation.
- Avoid black offset shadows, glow, neon, and strong glassmorphism.
- Use line icons with consistent stroke weight.

#### Buttons
- Primary: `#B83A3A`, white text.
- Primary hover: `#9F3030`.
- Strong/pressed state: `#7F2929`.
- Secondary: transparent or warm surface with `#C9C0BB` border and charcoal text.
- Destructive: semantic red when the action is actually destructive; do not confuse brand red with error semantics.
- No default gradient buttons.

#### Cards
- Default: `#F8F5F2` with `#D8D1CD` border.
- Selected/emphasized: `#F3E4E2` with muted red border/accent.
- Use cards only when grouping content improves comprehension.
- Do not fill dashboards with repetitive empty KPI cards.

#### Tables
- Table surface: `#F8F5F2`.
- Header: `#ECE8E4`.
- Hover row: approximately `#F3EDEA`.
- Divider: `#D8D1CD`.
- Keep actions compact.
- Do not make the full table header red.

#### Forms
- Input surface: `#F8F5F2` or `#E9E3DF` depending on context.
- Border: `#D8D1CD` / `#D2CAC5`.
- Disabled surface: `#ECE8E4`.
- Focus: muted red `#B83A3A`, with a restrained focus ring.
- Validation messages remain near the relevant field.
- Loading and disabled states must remain obvious.

### E. Semantic Status Colors (`StatusBadge.razor`)

Brand red does **not** replace semantic state colors.

- **Active / Approved / Confirmed / Completed / Paid / Available:** `#16A34A`
- **Pending / Processing / Frozen / Scheduled / Waitlisted:** `#D97706`
- **Rejected / Expired / NoShow / Overdue / Locked / Escalated:** `#DC2626`
- **Cancelled / Inactive / Archived / Retired:** `#6B7280`

Prefer tinted badge backgrounds with readable foreground text. Always pair color with text and/or icon.

### F. Home Hero & Public Landing Page

- Hero image file: **`wwwroot/images/hero-bg.png`**
- Razor source: **`/images/hero-bg.png`**
- Render with `w-full h-full object-cover`.
- Do not apply `grayscale`, `contrast-*`, or `brightness-*` filters that destroy the photo.
- Use the image full-bleed where appropriate.
- If text readability needs support, add a separate warm-neutral/charcoal overlay layer. Do not permanently edit the asset.
- Hero typography should use charcoal/near-black and muted red where the image is light enough; when placed over dark photography, use warm white with muted red emphasis.
- Primary hero CTA: muted red.
- Secondary CTA: transparent/warm surface with restrained border.
- Keep the page athletic through photography, typography, composition, and content — not by making the whole screen black.

### G. Public Navigation & Shrinking Navbar

`PublicLayout.razor` uses two visual states:

#### At top / before scrolling
- Full width.
- No corner radius.
- Background: `#F2EFEC`.
- Border-bottom: `#D8D1CD`.
- No heavy shadow.
- Logo left, navigation truly centered, account actions right.

#### Scrolled / floating state
- Shrink slightly and center the navbar.
- Desktop target: `width: calc(100% - 48px)`, `max-width: 1500px`, `top: 14px`, radius around `16px`.
- Background:

```css
background: rgba(242, 239, 236, 0.94);
backdrop-filter: blur(10px);
-webkit-backdrop-filter: blur(10px);
border: 1px solid rgba(37, 35, 35, 0.08);
box-shadow: 0 8px 24px rgba(37, 35, 35, 0.08);
```

- Active/hover navigation: `#B83A3A`.
- Primary public CTA: `#B83A3A`.
- Keep translucency restrained; do not turn the navbar into obvious glassmorphism.

### H. Auth Image & Login/Register Composition

The Auth screen uses the source image:

- **File:** `wwwroot/images/auth.png`
- **URL:** `/images/auth.png`

The source image is a reusable equipment still-life and must remain free of:
- text;
- Victor Fitness logo;
- red ribbons;
- UI controls;
- baked-in dark gradient;
- generated people/faces.

Recommended left-panel treatment:

```css
background-image:
    linear-gradient(
        90deg,
        rgba(30, 27, 26, 0.92) 0%,
        rgba(30, 27, 26, 0.72) 38%,
        rgba(30, 27, 26, 0.25) 70%,
        rgba(30, 27, 26, 0.05) 100%
    ),
    url('/images/auth.png');
background-size: cover;
background-position: center;
```

Branding content over the image:
- Actual Victor Fitness logo in the upper-left.
- Eyebrow: `VICTOR FITNESS / ESTABLISH YOUR STANDARD`.
- Headline:
  - `KỶ LUẬT.`
  - `SỨC MẠNH.`
  - `BẢN LĨNH.`
- First two lines: warm white.
- `BẢN LĨNH.`: muted red.
- Supporting text: `Mỗi buổi tập là một bước tiến. Bắt đầu hành trình của bạn cùng Victor Fitness.`
- Bottom labels: `V-FITNESS MANAGEMENT` and `MOVE WITH PURPOSE ↗`.
- A restrained red geometric/ribbon accent may be rendered as a separate CSS element.

Right panel:
- Background around `#ECE8E4`, not white.
- Auth card around `#F4F0ED`.
- Input surface around `#E9E3DF`.
- Login CTA uses `#B83A3A`.
- Register reuses the same Auth layout and image treatment.
- On mobile, collapse to a compact branding image/header followed by the form; do not preserve the 2-column split.

### I. Facility Map

- Base surface: `#F8F5F2`.
- Stroke: approximately `#9A918C`.
- Hover: `#F3E4E2`.
- Selected: `#B83A3A`.
- Selected text: white.
- Preserve keyboard accessibility, hover/click behavior, and selected-area details.

### J. Anti-"AI Generated" Rules

Avoid:
- pure-white pages with one giant floating card as the only focal point;
- full-page pitch-black backgrounds by default;
- neon red;
- purple/blue gradients;
- glow;
- glassmorphism-heavy panels;
- decorative gradient buttons;
- excessive rounded cards;
- every section wrapped in a card;
- giant empty KPI tiles;
- fake analytics/charts without business value;
- repetitive spacing that makes every section look templated;
- generated people/faces when a real gym/equipment photograph can do the job;
- AI-generated logos instead of project logo assets;
- role-specific themes that break the shared brand system.

Prefer:
- realistic gym photography or equipment still-life;
- warm neutral surfaces;
- charcoal typography;
- small purposeful muted red accents;
- editorial but practical composition;
- real task-oriented tables and forms;
- meaningful whitespace;
- visual hierarchy that balances imagery and functional UI.

---

## 7. AI Agent Prompt (For UI Code Generation)

*When using an AI assistant (Cursor, GitHub Copilot, Claude, ChatGPT, Gemini, Codex), provide this guideline file as the source of truth. Execute the redesign in phases rather than rewriting the entire frontend blindly.*

**[BEGIN PROMPT FOR AI AGENT]**

Bạn là Senior Blazor WebAssembly Frontend Developer (.NET 8 + Tailwind CSS) đồng thời là Senior UI/UX Product Designer.

Nhiệm vụ là redesign `VFMS.Client` theo design system **Soft Black + Muted Red + Warm Neutral**, nhưng TUYỆT ĐỐI không làm thay đổi business logic, API contract, authorization, validation, routing hoặc workflow hiện có.

### 1. RÀNG BUỘC KIẾN TRÚC

- Dự án: Blazor WebAssembly .NET 8.
- Chỉ reference `VFMS.Contracts` cho DTO, validator và system message dùng chung.
- Không reference trực tiếp `VFMS.Domain`, `VFMS.Application`, `VFMS.Infrastructure`.
- Giữ nguyên typed `HttpClient`, REST API, event handler, binding, validators và auth flow.
- Giữ nguyên `<AuthorizeView Roles="...">`, route authorization và role-based navigation.
- Backend là source of truth cho authorization, business rules, payment confirmation và persistence.
- Không tự tạo API, DTO field, permission hoặc business rule mới.
- Mock/preview data phải tiếp tục được ghi rõ là mock.

### 2. PHASE 1 — MIGRATE TONE MÀU TOÀN HỆ THỐNG

Thực hiện **design-token/color migration trước**, chưa redesign sâu Login/Register.

Core tokens:

```css
:root {
    --vfms-bg: #F2EFEC;
    --vfms-surface: #F8F5F2;
    --vfms-surface-alt: #ECE8E4;
    --vfms-surface-strong: #FFFFFF;

    --vfms-charcoal: #252323;
    --vfms-charcoal-soft: #2B2928;

    --vfms-text: #242424;
    --vfms-text-secondary: #6F6966;
    --vfms-text-muted: #8C8581;

    --vfms-primary: #B83A3A;
    --vfms-primary-hover: #9F3030;
    --vfms-primary-dark: #7F2929;
    --vfms-primary-soft: #F3E4E2;

    --vfms-border: #D8D1CD;
    --vfms-border-strong: #C9C0BB;
}
```

Nguyên tắc:
- warm neutral là màu nền chủ đạo;
- charcoal là màu chữ/dark surface;
- muted red chỉ là accent;
- không dùng pure white cho toàn page;
- không dùng pure black làm default background;
- không dùng neon/glow/purple-blue gradient/glassmorphism.

Migrate lần lượt:
1. shared/global styles và tokens;
2. reusable UI components;
3. `PublicLayout`;
4. public pages/Home;
5. `WorkspaceShell`;
6. `ManagerAdminLayout`;
7. `StaffPTLayout`;
8. dashboards;
9. CRUD/management pages;
10. profile/member/transaction pages.

Không redesign Auth sâu trong Phase 1; chỉ đảm bảo Auth không còn phụ thuộc Sage tokens cũ.

### 3. PUBLIC NAVBAR / SHRINKING HEADER

At top:
- full width;
- background `#F2EFEC`;
- no radius;
- light bottom border;
- no heavy shadow.

Scrolled:
- centered shrinking navbar;
- `width: calc(100% - 48px)`;
- `max-width: 1500px`;
- `top: 14px`;
- radius around `16px`;
- background `rgba(242,239,236,0.94)`;
- blur `10px`;
- subtle charcoal border/shadow;
- active/hover `#B83A3A`.

Do not use a black floating navbar.

### 4. HOME

Use:
- `/images/hero-bg.png`
- `object-cover`
- natural photo color

Do not use:
- `grayscale`
- destructive brightness/contrast filters
- heavy black overlays

If copy needs contrast, use a separate CSS overlay.

Primary CTA: muted red.
Secondary CTA: warm surface/transparent with restrained border.

### 5. DASHBOARD / CRUD

All roles share the same brand tokens.

Main background:
`#F2EFEC`

Sidebar:
`#F8F5F2`

Active sidebar:
`#F3E4E2`

Active text/icon:
`#B83A3A`

Cards/tables/forms:
`#F8F5F2`

Border:
`#D8D1CD`

Do not recreate:
- black/red Admin theme;
- navy/indigo Manager theme;
- black/white Staff/PT theme.

Role identity comes from workflow, navigation and permissions, not palette.

### 6. COMPONENTS

Radius:
- button/input/select: 8px
- cards/panels/modals: 10–12px

Primary button:
`#B83A3A`

Hover:
`#9F3030`

Selected card:
soft red background `#F3E4E2`

Tables:
- warm surface
- neutral header
- muted red only for focused/active actions

Forms:
- warm inputs
- muted red focus
- visible validation
- clear disabled/loading states

Keep semantic colors:
- success `#16A34A`
- warning `#D97706`
- error `#DC2626`
- inactive `#6B7280`

### 7. FACILITY MAP

Default:
`#F8F5F2`

Stroke:
`#9A918C`

Hover:
`#F3E4E2`

Selected:
`#B83A3A`

Selected text:
white

Preserve all existing SVG/interactions.

### 8. PHASE 2 — REDESIGN LOGIN & REGISTER RIÊNG

Only start this phase after Phase 1 builds successfully.

Auth files:
- `Pages/Auth/Login.razor`
- `Pages/Auth/Register.razor`
- `Layout/AuthLayout.razor`
- `wwwroot/css/auth.css`
- shared Auth components if needed

Use local image:

`wwwroot/images/auth.png`

Razor/CSS URL:

`/images/auth.png`

Do not generate or download another Auth image.

The source image must remain an equipment-only photograph. Do not add people/faces to it.

#### Desktop composition

Split approximately:
- left 66–68%;
- right 32–34%.

Left:
- full-cover `/images/auth.png`;
- separate charcoal overlay;
- actual project Victor Fitness logo at upper-left;
- eyebrow:
  `VICTOR FITNESS / ESTABLISH YOUR STANDARD`
- headline:
  `KỶ LUẬT.`
  `SỨC MẠNH.`
  `BẢN LĨNH.`
- first two lines warm white;
- `BẢN LĨNH.` muted red;
- supporting copy:
  `Mỗi buổi tập là một bước tiến.`
  `Bắt đầu hành trình của bạn cùng Victor Fitness.`
- bottom:
  `V-FITNESS MANAGEMENT`
  `MOVE WITH PURPOSE ↗`
- optional restrained red CSS ribbon/accent.

Do not bake any of those elements into the image.

#### Right Auth panel

Background:
`#ECE8E4`

Top:
- `← TRỞ VỀ TRANG CHỦ`
- `VICTOR FITNESS`

Login card:
- width about 380–420px;
- surface `#F4F0ED`;
- border around `#D2CAC5`;
- radius about 12px;
- extremely subtle shadow.

Do not use a pure-white card.

Login content:
- eyebrow `TÀI KHOẢN / ĐĂNG NHẬP`
- heading:
  `CHÀO MỪNG`
  `TRỞ LẠI.`
- existing email/phone field
- existing password field
- existing remember-me behavior
- primary button `#B83A3A`
- existing Register link

Keep all current validation/API/auth behavior intact.

#### Register

Reuse the same `AuthLayout` and `/images/auth.png`.

Use heading:
`BẮT ĐẦU`
`HÀNH TRÌNH.`

Do not create a separate visual theme.

If Register is taller than the viewport:
- allow the right/form panel to scroll;
- keep the branding panel stable where practical.

#### Mobile Auth

Do not keep a 2-column layout.
Use:
1. compact brand/photo area;
2. form below.

Keep logo and core branding visible without oversized typography.

### 9. ANTI-AI CHECKLIST

Before completing any page, remove:
- pure-white page + oversized floating card;
- pitch-black full-page defaults;
- neon/glow;
- purple/blue gradients;
- obvious glassmorphism;
- decorative charts;
- excessive rounded corners;
- card-for-everything layouts;
- repetitive template spacing;
- generated human imagery;
- fake logo variants;
- arbitrary animations.

Prefer:
- real/local gym photography;
- warm neutral surfaces;
- charcoal typography;
- muted red emphasis;
- task-focused layouts;
- restrained decoration.

### 10. WORKFLOW / UI STATES

Every async page must support:
- Loading
- Empty (`MSG-01` where appropriate)
- Error (`MSG-xx` in context)
- Success

On submit:
- disable action;
- prevent double-submit;
- show clear feedback.

Preserve all current and planned workflows described in this guideline. Do not infer that planned workflows are implemented.

### 11. EXECUTION ORDER

1. Read existing components before editing.
2. Preserve logic/API/validation/authorization.
3. Complete Phase 1 token migration.
4. Build project and fix compile issues.
5. Report Phase 1 files changed.
6. Then complete Phase 2 Auth redesign.
7. Build again.
8. Verify Login, Register, desktop and mobile layouts.
9. Report all files changed and build result.

**[END PROMPT FOR AI AGENT]**
