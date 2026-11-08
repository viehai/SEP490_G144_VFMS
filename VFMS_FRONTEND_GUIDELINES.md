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

- `Pages/Admin/AdminDashboard.razor` (`/admin/dashboard`) and `Pages/Manager/ManagerDashboard.razor` (`/manager/dashboard`) use `ManagerAdminLayout`. Admin uses black/red (`#FF3E3E`); Manager uses slate/indigo (`#0F172A`, `#6366F1`).
- `Pages/Staff/StaffDashboard.razor` (`/staff/dashboard`) and `Pages/PT/PTDashboard.razor` (`/pt/dashboard`) use `StaffPTLayout` with black/white styling. Each dashboard contains a page header, three stat cards, quick actions, and a matching data panel.
- `Pages/UserProfile.razor` (`/profile`) uses `PublicLayout`, sharing Home's top navigation and footer rather than a dashboard sidebar. Clicking the signed-in greeting opens Profile; a compact "Hồ sơ" link remains visible on smaller screens. The navigation has a dark background on white public pages and retains Home's transparent hero and floating scroll behavior. Profile styles live in `wwwroot/css/profile.css`.
- Profile edits and the password form currently validate local mock data only. No profile or password API request is sent. Birthdate is a UI preview field and is not yet part of the existing profile DTO.
- The two dashboard layouts reuse `Components/Workspace/WorkspaceShell.razor` and `wwwroot/css/workspace.css`. All dashboard/profile cards, inputs, and buttons use square corners (`rounded-none`), borders, and no shadows. Montserrat is used for headings/statistics and Inter for body text/forms.
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

## 6. Design System & UI Vibe (Nike Brutal Minimalism / Gymshark)

### A. Color Palette & Layouts
- **User-Facing (Guest / Customer / Member):**
  - **App Background (`--bg-main`):** `#FFFFFF` (Pure white).
  - **Section / Container Background (`--bg-card`):** `#F7F7F7` (Light grey for section separation).
  - **Borders & Dividers (`--border-card`):** `#CFCFCF` (Clean, subtle solid lines).
  - **Typography Colors:** Primary text `#252525` (Deep dark). Muted/secondary text `#545454` or `#7D7D7D`.
  - **Brand Accent / CTAs (`--color-accent`):** `#0A0A0A` (Pitch black blocks with inverted white text).
  - **Layout:** A fixed public header, a high-contrast dark hero, white content sections, and a dark footer with generous padding. The Home page uses a moving service-label strip and a white interactive facility map.
- **Admin Back-Office:**
  - **Sidebar Background:** `#0A0A0A` (Pitch black).
  - **Accent Color:** `#FF3E3E` (Vivid Red for authority and system alerts).
- **Manager / Owner Back-Office:**
  - **Sidebar Background:** `#0F172A` (Deep Slate / Navy).
  - **Accent Color:** `#6366F1` (Indigo for revenue, reports, and business metrics).

### B. Typography
- Headings (`h1`–`h6`, nav links, banners, primary CTAs): `'Montserrat', sans-serif`, usually bold (`700`–`800`) and uppercase. Weight `900` is allowed for major Home headlines and hero text. Use letter spacing appropriate to the size; `0.05em` is a starting point, not a requirement for every heading.
- Body copy, tables, inputs, validation messages: `'Inter', sans-serif`, `font-weight: 400`–`500`, normal case for maximum readability.

### C. Component Styling, Geometry & Status Badges (`StatusBadge.razor`)
- **Geometry:** Use square corners (`rounded-none`) for buttons, cards, inputs, and the facility map. The floating public navigation bar may use rounded corners when scrolled, as in `PublicLayout.razor`.
- **Shadows:** The floating navigation bar may use a soft shadow. Equipment and pricing cards and selected pricing tabs may use a solid black offset shadow for interaction feedback, matching Home. Avoid adding shadows to unrelated components by default.
- **Buttons (`GymButton`):** Rectangular, heavy padding (`py-4 px-8`), uppercase, bold font. Hover states invert or darken sharply (e.g., `#0A0A0A` to `#000000`).
- **Cards:** Equipment and pricing cards may use a strong black border and a black offset shadow on hover. Featured class cards emphasize large, sharp images, subtle hover zoom, and left-aligned text; their image proportions may be responsive.
- **Status Colors (always paired with text/icon, never color alone):**
  - *Active / Approved / Confirmed / Completed / Paid / Available:* Emerald Green (`#10B981`)
  - *Pending / Processing / Frozen / Scheduled / Waitlisted:* Amber Orange (`#F59E0B`)
  - *Rejected / Expired / NoShow / Overdue / Locked / Escalated:* Warning Red (`#EF4444`)
  - *Cancelled / Inactive / Archived / Retired:* Neutral Grey (`#4B5563`)

### D. Public Home interactions
- **Header:** Reuse `PublicLayout.razor` for public browsing pages. Its header starts transparent and becomes a floating dark navigation bar on scroll; ensure navigation remains usable at smaller viewport sizes. Authentication screens use `AuthLayout.razor`, with a dark brand panel and white form area that stacks on small screens.
- **Startup screen:** Display the Victor Fitness splash before Blazor starts, then fade it out when initialization completes. Show a readable error state if startup fails; do not invent progress percentages or enforce a fixed delay.
- **Facility map:** Keep the map within Home on a white background with black/grey lines. Hover uses a light grey fill; the selected area uses a black fill with white labels. Allow keyboard selection and show the selected area's details below the map. The layout and equipment markers are illustrative until backed by verified facility data.
- **Motion and responsiveness:** Adapt layouts to phone and desktop, maintain visible keyboard focus and readable contrast, and respect `prefers-reduced-motion` for animations. The moving service-label strip is decorative; do not use motion alone to convey information.

---

## 7. AI Agent Prompt (For UI Code Generation)

*When using an AI assistant (Cursor, GitHub Copilot, Claude, ChatGPT, Gemini) to generate Blazor UI components, always provide this section as context to ensure the output strictly matches the project's architecture and design system.*

**[BEGIN PROMPT FOR AI AGENT]**

Bạn là một Chuyên gia Blazor WebAssembly Frontend Developer (.NET 8 + Tailwind CSS) và UI/UX Designer. Khi viết mã Razor Components (`.razor`), Scoped CSS (`.razor.css`), Layouts, hoặc bất kỳ thành phần giao diện Blazor nào cho dự án **VFMS (`VFMS.Client`)**, bạn PHẢI tuân thủ tuyệt đối kiến trúc Clean Architecture (chỉ dùng DTO/Validator từ `VFMS.Contracts`) và Hệ thống Thiết kế **"Nike Brutal Minimalism / Gymshark"** với các quy tắc NGHIÊM NGẶT dưới đây:

**1. PHONG CÁCH THIẾT KẾ CỐT LÕI (BRUTAL MINIMALISM)**
- **YÊU CẦU TỐI THƯỢNG:** Sạch sẽ, tương phản cao, góc cạnh.
- **Hình khối (Geometry):** Nút bấm, card, input và sơ đồ dùng góc vuông (`rounded-none`). Navbar công khai dạng nổi được phép bo góc khi cuộn theo `PublicLayout.razor`.
- **Hiệu ứng (Shadows):** Navbar nổi được phép có bóng mềm; card thiết bị, gói tập và tab gói được chọn được phép có bóng khối đen như Home. Không thêm bóng tùy tiện cho các thành phần khác.
- **Tương tác (Hover):** Nút chuyển màu mượt; card lớp học có thể zoom ảnh nhẹ, card thiết bị/gói tập có thể dùng viền và bóng khối khi hover. Hỗ trợ bàn phím và giảm chuyển động theo cài đặt người dùng.

**2. TYPOGRAPHY (KIỂU CHỮ)**
- **Tiêu đề (Headings h1-h6, NavLink, Primary CTA):**
  + **Font:** Dùng font sans-serif `'Montserrat'`.
  + **Font-weight:** Thông thường `700` hoặc `800`; cho phép `900` với tiêu đề lớn và banner.
  + **Transform:** Tiêu đề, điều hướng và CTA thường viết hoa (`text-transform: uppercase`).
  + **Letter-spacing:** Điều chỉnh theo kích thước và độ dễ đọc; `0.05em` là giá trị tham khảo.
- **Nội dung & Form (Body text, Input, `<ValidationMessage>`, Table):**
  + **Font:** `'Inter'`.
  + **Font-weight:** `400`, `500`.
  + Tuyệt đối không uppercase ở phần nội dung dài để giữ độ dễ đọc.

**3. QUY TẮC MÀU SẮC & LAYOUTS (Tailwind Config / CSS Variables)**
- **Giao diện Người dùng (Customer/Guest/Member):** Nền trắng `#FFFFFF`, phân vùng nền xám nhạt `#F7F7F7`. Chữ đen tuyền `#252525` và chữ phụ `#7D7D7D`. Đường kẻ divider `#CFCFCF`. Nút bấm CTA màu đen `#0A0A0A` chữ trắng.
- **Giao diện Admin:** Dùng Layout có Sidebar nền Đen `#0A0A0A`, text trắng, màu accent Đỏ rực `#FF3E3E`.
- **Giao diện Owner/Manager:** Dùng Layout có Sidebar nền Xanh Slate `#0F172A`, text trắng, màu accent Chàm Indigo `#6366F1`.
- **Trang Home:** Hero tối, các khu vực nội dung và sơ đồ nền trắng; sơ đồ dùng nét đen/xám và khu vực đang chọn có nền đen, chữ trắng. Splash screen hiển thị khi Blazor khởi động và ẩn khi ứng dụng sẵn sàng.
- **Dữ liệu minh họa:** Nội dung tĩnh và sơ đồ hiện có trên Home chỉ là minh họa, không mặc định là dữ liệu thời gian thực; dùng API cho dữ liệu nghiệp vụ thực tế.

**4. QUY CHUẨN MÀU SẮC TRẠNG THÁI (STATUS BADGES)**
- **Active / Approved / Completed / Available:** Xanh lá (`#10B981`)
- **Pending / Processing / Frozen / Waitlisted:** Cam (`#F59E0B`)
- **Rejected / NoShow / Overdue / Locked:** Đỏ (`#EF4444`)
- **Cancelled / Inactive / Archived:** Xám trung tính (`#4B5563`)

**5. YÊU CẦU KỸ THUẬT BLAZOR**
1. **Cấu trúc Component:** Phân tách rõ Directive (`@page`, `@inject`, `@using VFMS.Contracts...`), phần HTML UI, và khối `@code { }`.
2. **Quản lý Trạng thái UI:** Xử lý đủ 4 trạng thái: `Loading`, `Empty` (hiển thị `MSG-01`), `Error` (hiển thị `MSG-xx`), và `Success`. Thêm `disabled` khi gửi form.
3. **Nghiệp vụ Mới Nhất:** Hỗ trợ Giỏ hàng Combo (GYM, PT, YOGA), Check-out thanh toán VietQR động kèm đếm ngược 15 phút, và bám sát ma trận phân quyền `<AuthorizeView Roles="...">` (7 roles).

**[END PROMPT FOR AI AGENT]**
