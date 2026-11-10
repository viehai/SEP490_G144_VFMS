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
- `wwwroot/index.html` hosts the startup screen and loads Tailwind through its CDN. The startup styles are in `wwwroot/css/splash.css`. There is currently no project-level `tailwind.config.js`.
- Logo and equipment images used by Home are in `wwwroot/brand/`, `wwwroot/images/`, and `wwwroot/equipments/`.
- **Home hero image convention:** use the local file `wwwroot/images/hero-bg.png`, referenced from Razor as `/images/hero-bg.png`. Do not apply `grayscale`, `contrast-*`, or `brightness-*` utility filters to this image. Render it with `object-cover` and preserve its natural warm gym lighting. If text readability needs support, use a separate CSS/Tailwind warm-ivory gradient overlay rather than permanently editing the image.
- Signed-in Customers and Members see the active Gym membership plan catalog from `api/membership-plans` on Home. The remaining sample pricing, class, equipment, and facility-map data are illustrative. Use API-backed data for live operations.

### Running the authentication screens locally

- Start `VFMS.API` with its HTTPS profile and start `VFMS.Client`. The client's `wwwroot/appsettings.Development.json` sets `ApiBaseUrl` to `https://localhost:7148/`, matching the API launch profile. Update this setting when using a different API URL; never put secrets in client configuration.
- The API allows the development client origins `https://localhost:7246` and `http://localhost:5273`. For other origins or deployment, set the API's `Cors:AllowedOrigins` array explicitly. Outside Development, the client defaults to its own origin unless `ApiBaseUrl` is configured in client settings.
- Login accepts email or phone number. Unchecked "Remember me" stores the token session in session storage; checked stores it in local storage. Passwords are never persisted. `AuthSession` restores the session and rotates an expired access token through `api/auth/refresh` before returning authenticated state. Server-side authorization remains authoritative.
- Register sends `RegisterRequest` to `api/auth/register`, then shows a success screen linking to Login. Validation errors appear beside the relevant fields; API/network failures remain on the form so the user can retry. API-backed password reset and change screens are outside this implementation's current scope; the profile preview below includes only a local password-form demo.
- Logout is available from authenticated public navigation. Clicking the button posts the current refresh token to `api/auth/logout` with a Bearer access token, refreshing access first if needed. Clear the in-memory session, session storage, and local storage, publish anonymous UI state, then return to Home. No confirmation or completion screen is shown. An API/network failure still ends the local session and returns Home; local cleanup alone does not guarantee server-side revocation.
- **Authentication compliance gaps.** The API currently activates self-registered accounts immediately, while `UC-01` requires email verification before activation. The login flow checks existing account locks, while the failed-attempt lockout required by `GBR-27` is still pending. Logout currently revokes the submitted refresh token. Immediate access-token blacklisting under `GBR-28` is still pending.
- Run authentication client tests with `dotnet test tests/VFMS.ClientTests/VFMS.ClientTests.csproj`. These tests use fake HTTP/storage and do not create database accounts; verify the full registration/login flow separately against a configured API and database.

### Dashboard and profile UI previews

- `Pages/Admin/AdminDashboard.razor` (`/admin/dashboard`) and `Pages/Manager/ManagerDashboard.razor` (`/manager/dashboard`) use `ManagerAdminLayout`. The existing black/red and slate/indigo preview colors are **legacy styling to be replaced** by the unified Sage Green / Warm Ivory design system in Section 6.
- `Pages/Staff/StaffDashboard.razor` (`/staff/dashboard`) and `Pages/PT/PTDashboard.razor` (`/pt/dashboard`) use `StaffPTLayout`. The existing black/white preview styling is also **legacy styling to be replaced**. Each dashboard may retain its page header, stat cards, quick actions, and data panel structure while adopting the shared tokens and components.
- `Pages/UserProfile.razor` (`/profile`) uses `PublicLayout`, sharing Home's top navigation and footer rather than a dashboard sidebar. Clicking the signed-in greeting opens Profile; a compact "Hồ sơ" link remains visible on smaller screens. Profile styles live in `wwwroot/css/profile.css`. Restyle the public navigation, profile controls, and footer with the shared Sage Green / Warm Ivory palette; avoid pitch-black page chrome.
- Profile edits and the password form currently validate local mock data only. No profile or password API request is sent. Birthdate is a UI preview field and is not yet part of the existing profile DTO.
- The two dashboard layouts reuse `Components/Workspace/WorkspaceShell.razor` and `wwwroot/css/workspace.css`. Keep Montserrat for headings/statistics and Inter for body text/forms, but replace the old `rounded-none`-everywhere convention with the restrained 8–12px radius rules in Section 6.
- These routes deliberately expose **mock UI previews** with visible mock-data labels. Dashboards include a screen selector; quick actions change local preview panels without performing operational actions or granting roles. Before connecting real data, enforce the permission matrix with authenticated route handling and role-specific API authorization, and remove the preview selector. Do not infer permissions from the selected preview.
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

## 6. Design System & UI Vibe (Sage Athletic Minimalism / Warm Ivory Fitness)

The VFMS visual direction is a **bright, athletic, premium fitness product** using muted Sage Green, Warm Ivory, natural gym photography, restrained geometry, and enterprise-grade information hierarchy. The product must feel intentionally designed rather than like a generic AI-generated dashboard.

### A. Global Color Tokens

Use the same core brand tokens across Guest, Customer, Member, PT, Staff, Manager, Admin, Auth, and Kiosk experiences. Role differentiation should come from navigation labels, icons, permissions, data, and page context — **not from completely different brand palettes**.

- **Primary Sage (`--color-primary`):** `#5F6F52`
- **Primary Hover / Dark Sage (`--color-primary-hover`):** `#4E5D44`
- **Primary Soft (`--color-primary-soft`):** `#E8EDE3`
- **Warm Ivory App Background (`--bg-main`):** `#F7F4EC`
- **White Surface (`--bg-surface`):** `#FFFFFF`
- **Muted Section Background (`--bg-muted`):** `#EEF1EA`
- **Primary Text (`--text-primary`):** `#383E43`
- **Secondary Text (`--text-secondary`):** `#69706A`
- **Subtle Text (`--text-muted`):** `#8A9089`
- **Borders / Dividers (`--border-card`):** `#D8DED2`
- **Dark Olive Surface (`--surface-dark`):** `#3F493A` — use sparingly for footer, high-emphasis banners, or compact contrast areas; never as the dominant app background.
- **Focus Ring:** use `#5F6F52` with sufficient visible contrast.

Do not reintroduce black/red, black/white, navy/indigo, purple/blue neon, or role-specific brand themes as the default visual system. Red remains reserved for destructive actions and error states.

### B. Layout Direction by Product Area

#### Public / Guest / Customer / Member
- Use Warm Ivory or white as the dominant canvas.
- Use full-width, high-quality **realistic gym photography** with natural lighting.
- Avoid dark full-screen hero sections.
- Use Sage Green only for important emphasis: primary CTA, active navigation, key numbers, selected states, icons, and small decorative rules.
- Keep sections spacious with clear content hierarchy instead of placing every block inside a card.

#### PT / Staff / Manager / Admin Back-Office
- Use a **light enterprise dashboard** for every role.
- Default sidebar: `#FFFFFF` or `#F7F4EC`, with a `#E8EDE3` active item and `#5F6F52` icon/accent.
- Main content background: `#F7F4EC`.
- Data surfaces and forms: `#FFFFFF`.
- Use thin borders instead of heavy shadows.
- Do not give Admin a black/red theme or Manager a navy/indigo theme. Destructive Admin actions may use red only at the action/status level.
- Keep management screens practical: page title + contextual description + toolbar + filters + data table/list + pagination + drawer/modal/form when appropriate.

#### Auth
- Keep Login/Register bright and consistent with the public brand.
- Prefer a Warm Ivory / Sage branded illustration or photography area plus a white form surface.
- Do not use a large pitch-black brand panel.

#### Kiosk
- Kiosk may use stronger contrast for distance readability, but it must still use Sage / Ivory / White as the primary identity.
- Success, warning, and error states may use their semantic colors prominently.

### C. Typography

- **Headings, nav links, banners, major statistics, primary CTAs:** `'Montserrat', sans-serif`.
  - Weight: `700`–`800`; `900` only for major hero headlines where it remains readable.
  - Uppercase is acceptable for short hero headlines, navigation, labels, and primary CTAs.
  - Avoid uppercase for paragraphs, tables, long form labels, and dense admin content.
- **Body copy, tables, forms, validation messages:** `'Inter', sans-serif`, `400`–`500`.
- Use typography and spacing to establish hierarchy instead of relying on excessive borders, cards, gradients, or oversized decorative elements.

### D. Geometry, Shadows & Components

- **Radius:**
  - Buttons / inputs / selects: `8px`
  - Cards / panels / modals / drawers: `10px`–`12px`
  - Pills / status badges: full pill radius only where semantically appropriate
- Do not use extreme `20px+` rounding throughout the product.
- Do not use `rounded-none` globally.
- Use subtle border-first surfaces. If a shadow is needed, keep it soft and low elevation.
- Avoid black offset shadows, glassmorphism, glow, neon, and excessive gradients.
- Use line icons with consistent stroke weight.

#### Buttons
- Primary: Sage `#5F6F52`, white text; hover `#4E5D44`.
- Secondary: white or transparent surface, Sage border/text.
- Tertiary: text/icon button with restrained hover background.
- Destructive: semantic red; never use red as a general brand accent.
- Keep labels concise and action-oriented.

#### Cards
- Use cards only when grouping content adds meaning.
- Prefer white surfaces with `#D8DED2` borders.
- Avoid filling dashboard pages with repetitive KPI cards that do not aid decisions.
- Important metrics may use a subtle Sage icon, label, or number accent without tinting the whole card green.

#### Tables
- White surface, thin separators, clear column labels, comfortable row height.
- Use a subtle `#EEF1EA` header or hover state.
- Keep actions compact and predictable.
- On smaller screens, switch to structured stacked rows/cards only when a table would become unusable.

### E. Status Colors (`StatusBadge.razor`)

Status color is semantic and must always be paired with text and/or icon:

- **Active / Approved / Confirmed / Completed / Paid / Available:** `#16A34A`
- **Pending / Processing / Frozen / Scheduled / Waitlisted:** `#D97706`
- **Rejected / Expired / NoShow / Overdue / Locked / Escalated:** `#DC2626`
- **Cancelled / Inactive / Archived / Retired:** `#6B7280`

These colors do not replace the Sage brand palette; they are used only for state communication.

### F. Home Hero & Public Landing Page

The Home hero is the primary visual reference for the new brand direction.

- Hero image file: **`wwwroot/images/hero-bg.png`**
- Razor source: **`/images/hero-bg.png`**
- Render with `w-full h-full object-cover`.
- Do **not** apply `grayscale`, `contrast-*`, `brightness-*`, heavy dark overlays, or filters that turn the image black.
- The hero may use the image full-bleed across the available hero area.
- Keep the athlete / equipment composition naturally visible.
- Place hero copy on top of the image only when readability is sufficient.
- If extra readability is required, use a **separate overlay layer** such as a warm-ivory gradient from the left, e.g. strong ivory at the far left fading to transparent around 55–60% width. Do not bake this gradient into the image asset.
- Keep the header transparent or lightly tinted at the top of the hero; on scroll, transition to a light Warm Ivory / white floating navigation surface with Sage active states. Do not switch to a pitch-black floating bar.
- Primary hero CTA uses Sage Green; secondary CTA uses a transparent/white surface with Sage border.
- Keep the page visually athletic through photography, typography, strong cropping, spacing, and content — not through black backgrounds.

### G. Public Home Interactions

- **Header:** Reuse `PublicLayout.razor`. Top-of-page navigation may overlay the hero, but text/icons must remain readable. On scroll, use a light floating bar with subtle border/shadow and Sage active/hover states.
- **Startup screen:** Keep the Victor Fitness splash, but style it with Warm Ivory / White / Sage instead of full black. Fade out when Blazor is ready. Show a readable error state if startup fails; do not invent progress percentages or enforce a fixed delay.
- **Facility map:** Keep the map within Home. Use white or Warm Ivory background, muted grey/Sage lines, a soft Sage hover fill, and a deeper Sage selected state with white text. Maintain keyboard accessibility and selected-area details.
- **Motion and responsiveness:** Adapt layouts to phone and desktop, maintain visible keyboard focus and readable contrast, and respect `prefers-reduced-motion`. Decorative movement must never be required to understand content.

### H. Anti-"AI Generated" Rules

Avoid these patterns across the entire system:
- dark full-page backgrounds by default;
- purple/blue gradients;
- neon glow;
- glassmorphism;
- excessive gradient buttons;
- every section wrapped in a rounded card;
- giant empty KPI tiles;
- excessive `rounded-2xl` / `rounded-3xl`;
- generic dashboard layouts repeated unchanged for every role;
- fake analytics or decorative charts without business value;
- overuse of icons where text is clearer;
- arbitrary animations;
- inconsistent colors between roles.

Prefer:
- natural gym photography;
- strong but restrained editorial composition;
- consistent spacing;
- real data tables and task-oriented forms;
- role-specific workflows within one shared brand system;
- clear page hierarchy and realistic content;
- small, purposeful Sage accents.

---

## 7. AI Agent Prompt (For UI Code Generation)

*When using an AI assistant (Cursor, GitHub Copilot, Claude, ChatGPT, Gemini, Codex) to redesign or generate Blazor UI, provide the prompt below together with this guideline file.*

**[BEGIN PROMPT FOR AI AGENT]**

Bạn là Senior Blazor WebAssembly Frontend Developer (.NET 8 + Tailwind CSS) đồng thời là Senior UI/UX Product Designer.

Nhiệm vụ của bạn là **redesign toàn bộ giao diện `VFMS.Client` theo một design system thống nhất**, nhưng TUYỆT ĐỐI không làm thay đổi business logic, API contract, authorization, validation, routing hoặc các workflow hiện có.

### 1. RÀNG BUỘC KIẾN TRÚC — KHÔNG ĐƯỢC PHÁ VỠ

- Dự án là Blazor WebAssembly .NET 8.
- Chỉ reference `VFMS.Contracts` cho DTO, validator và system message dùng chung.
- Không reference trực tiếp `VFMS.Domain`, `VFMS.Application`, `VFMS.Infrastructure`.
- Giữ nguyên các typed `HttpClient` service và REST API hiện có.
- Giữ nguyên `<AuthorizeView Roles="...">`, route authorization và role-based navigation đang có.
- Backend vẫn là source of truth cho authorization, business rules, payment confirmation và persistence.
- Không tự ý tạo API giả, field giả, quyền giả hoặc business rule mới.
- Không thay đổi luồng nghiệp vụ chỉ để phù hợp giao diện.
- Các trang mock/preview phải tiếp tục được ghi rõ là mock cho đến khi được nối API thật.

### 2. DESIGN DIRECTION MỚI — SAGE ATHLETIC MINIMALISM

Loại bỏ toàn bộ phong cách dark/black-heavy hiện tại.

Toàn bộ Guest, Customer, Member, PT, Staff, Manager và Admin phải dùng chung một brand system:

- Primary Sage: `#5F6F52`
- Primary Hover: `#4E5D44`
- Soft Sage: `#E8EDE3`
- Warm Ivory Background: `#F7F4EC`
- White Surface: `#FFFFFF`
- Muted Section: `#EEF1EA`
- Primary Text: `#383E43`
- Secondary Text: `#69706A`
- Border: `#D8DED2`

Không tạo theme đen/đỏ riêng cho Admin.
Không tạo theme navy/indigo riêng cho Manager.
Không tạo theme đen/trắng riêng cho Staff/PT.

Khác biệt giữa các role phải đến từ navigation, permission, workflow, icon và dữ liệu — không phải đổi toàn bộ màu thương hiệu.

### 3. HERO HOME — BẮT BUỘC DÙNG ẢNH LOCAL

Home hero phải dùng:

`wwwroot/images/hero-bg.png`

và trong Razor:

`/images/hero-bg.png`

Yêu cầu:
- `object-cover`
- full-width / full hero image
- giữ màu ảnh tự nhiên
- KHÔNG dùng `grayscale`
- KHÔNG dùng `contrast-*`
- KHÔNG dùng `brightness-*`
- KHÔNG phủ overlay đen nặng

Nếu chữ khó đọc, tạo một layer gradient riêng:
- Warm Ivory `#F7F4EC` đậm ở bên trái
- fade mượt sang transparent khoảng 55–60% chiều rộng
- không chỉnh gradient trực tiếp vào file ảnh

Hero phải sáng, premium, athletic và realistic.
Không được quay lại kiểu hero đen toàn màn hình.

### 4. PUBLIC WEBSITE

Redesign:
- `PublicLayout`
- Home
- public navigation
- footer
- membership section
- class section
- equipment/facility sections
- facility map
- Profile
- Login/Register layouts

Hướng thiết kế:
- nhiều khoảng trắng;
- ảnh gym thật là visual chủ đạo;
- Sage chỉ dùng để nhấn;
- CTA chính màu Sage;
- CTA phụ viền Sage;
- section nền Warm Ivory / White luân phiên nhẹ;
- không nhét tất cả content vào card;
- không dùng gradient/neon/glassmorphism.

Header:
- trên hero có thể transparent;
- khi scroll chuyển thành floating light header màu white/ivory;
- active nav dùng Soft Sage + Sage text;
- không dùng floating navbar màu đen.

### 5. DASHBOARD CHO TẤT CẢ ROLE

Redesign `WorkspaceShell`, `ManagerAdminLayout`, `StaffPTLayout` và các dashboard theo light enterprise UI.

Mặc định:
- App background `#F7F4EC`
- Sidebar white hoặc Warm Ivory
- Active sidebar item `#E8EDE3`
- Active icon/text `#5F6F52`
- Main cards / tables / forms white
- Border `#D8DED2`
- Text `#383E43`

Dashboard phải task-oriented:
- page header;
- mô tả ngắn;
- action chính;
- KPI chỉ khi thực sự hữu ích;
- data table/list;
- filters/search;
- pagination;
- empty/loading/error/success states.

Không tạo hàng loạt card chỉ để lấp khoảng trống.
Không thêm chart nếu workflow không cần.
Không dùng màu role-specific làm brand theme.

### 6. COMPONENT SYSTEM

Dùng component reusable và nhất quán.

Radius:
- Button/input/select: `8px`
- Card/panel/modal/drawer: `10px–12px`
- Status badge: pill khi phù hợp

Không dùng `rounded-none` toàn hệ thống.
Không dùng `rounded-2xl/3xl` tràn lan.

Shadow:
- ưu tiên border;
- shadow chỉ rất nhẹ khi cần elevation;
- không dùng black offset shadow;
- không glow.

Buttons:
- Primary: `#5F6F52`, text white
- Hover: `#4E5D44`
- Secondary: white/transparent + Sage border
- Destructive: red semantic only

Tables:
- white;
- header/hover `#EEF1EA`;
- divider mỏng;
- action compact;
- responsive hợp lý.

Forms:
- label rõ;
- input white;
- border `#D8DED2`;
- focus Sage;
- validation message ngay cạnh field;
- disabled/loading state rõ.

### 7. TYPOGRAPHY

Headings / nav / major CTA:
- Montserrat
- 700–800
- 900 chỉ cho hero headline nếu cần

Body / form / table:
- Inter
- 400–500

Không uppercase đoạn văn dài.
Không dùng typography quá to chỉ để tạo cảm giác “modern AI landing page”.

### 8. STATUS COLORS

Giữ semantic colors:
- Success / Active / Approved / Paid: `#16A34A`
- Pending / Processing / Frozen / Waitlisted: `#D97706`
- Error / Rejected / Locked / Overdue: `#DC2626`
- Inactive / Cancelled / Archived: `#6B7280`

Luôn đi kèm text/icon; không dựa vào màu duy nhất.

### 9. GIỮ NGUYÊN WORKFLOW VÀ TRẠNG THÁI UI

Mọi trang async phải xử lý:
- Loading
- Empty (`MSG-01` nếu phù hợp)
- Error (`MSG-xx` đúng context)
- Success

Khi submit:
- disable action;
- tránh double-submit;
- hiển thị feedback rõ.

Giữ nguyên các workflow hiện có và planned workflow trong guideline:
- authentication;
- membership plans;
- cart GYM/PT/YOGA;
- VietQR checkout;
- membership freeze;
- PT scheduling;
- Yoga booking/waitlist/attendance;
- locker;
- FaceID/Kiosk;
- AI workout;
- equipment;
- procurement;
- supplements;
- competitions;
- moderation.

Không tự suy diễn implementation đã tồn tại nếu guideline nói đó mới là planned workflow.

### 10. ANTI-AI UI CHECKLIST

Trước khi hoàn thành bất kỳ page nào, tự kiểm tra và loại bỏ:
- nền đen phủ toàn page;
- purple/blue gradient;
- neon;
- glow;
- glassmorphism;
- gradient button không cần thiết;
- card bo tròn quá lớn;
- card ở mọi section;
- chart trang trí;
- icon thừa;
- spacing quá đều theo kiểu template;
- cùng một dashboard layout copy-paste cho mọi role;
- fake data không được đánh dấu mock;
- màu sắc không thuộc design tokens.

Mục tiêu cuối cùng:
**VFMS phải nhìn như một sản phẩm quản lý phòng gym thật: sáng, mạnh mẽ, premium, thực dụng, đồng bộ, dễ đọc và có chủ đích — không giống giao diện được AI generate hàng loạt.**

### 11. CÁCH THỰC HIỆN

Khi sửa code:
1. Đọc component/layout hiện tại trước khi thay.
2. Giữ logic, event handler, binding, API call, validation và authorization.
3. Ưu tiên tạo/reuse design tokens và shared components trước.
4. Sau đó restyle theo thứ tự:
   - `PublicLayout` + Home hero
   - Auth
   - WorkspaceShell + role layouts
   - shared components
   - dashboards
   - CRUD/management screens
   - profile/public supporting pages
5. Không xoá chức năng đang hoạt động chỉ vì chưa phù hợp visual.
6. Sau mỗi nhóm thay đổi, đảm bảo project vẫn build được.

**[END PROMPT FOR AI AGENT]**
