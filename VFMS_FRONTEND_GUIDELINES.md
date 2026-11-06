# VFMS Frontend Guidelines

## 1. Purpose

This document defines architecture, component, workflow, and styling conventions for the **`VFMS.Client`** Blazor WebAssembly + Tailwind CSS project inside the `SEP490_G144_VFMS` Clean Architecture solution. The frontend handles presentation, client-side usability validation, and role-specific navigation; all authoritative business rules, payment confirmations, and persistence reside in `VFMS.API` and `VFMS.Application`.

## 2. Responsibilities and Architectural Boundaries

- `VFMS.Client` is built with **Blazor WebAssembly on .NET 8 (`net8.0`)**, using **Tailwind CSS** and component-specific CSS where appropriate.
- Reference **only `VFMS.Contracts`** for shared Request/Response DTOs, FluentValidation validators, and standardized system message constants (`MSG-01` to `MSG-43`).
- Never reference `VFMS.Domain`, `VFMS.Application`, or `VFMS.Infrastructure`.
- Communicate with the backend exclusively through typed `HttpClient` services calling `VFMS.API` REST endpoints.
- Client-side validation and role-based UI hiding (`<AuthorizeView Roles="...">`) improve UX, but server validation and authorization are always authoritative.

## 3. Project Organization (`VFMS.Client`)

### Current public-facing implementation

- `Pages/Home.razor` is the public landing page and uses `Layout/PublicLayout.razor`.
- `Pages/FacilityMap.razor` is an interactive component embedded in Home; its styles live in `wwwroot/css/facility-map.css`.
- `Pages/Auth/Login.razor` (`/auth/login`) and `Pages/Auth/Register.razor` (`/auth/register`) use `Layout/AuthLayout.razor` and `wwwroot/css/auth.css`. Both forms use shared validators from `VFMS.Contracts`; registration does not sign the user in automatically. Logout is a direct action through `Components/Auth/LogoutButton.razor`, without a separate page.
- `wwwroot/index.html` hosts the startup screen and loads Tailwind through its CDN. The startup styles are in `wwwroot/css/splash.css`. There is currently no project-level `tailwind.config.js`.
- Logo and equipment images used by Home are in `wwwroot/brand/`, `wwwroot/images/`, and `wwwroot/equipments/`.
- Home currently contains illustrative class, equipment, pricing, and facility-map data. These values and the map layout are not live API data or verified operational information. Replace them with API-backed data when the relevant features are implemented.

### Running the authentication screens locally

- Start `VFMS.API` with its HTTPS profile and start `VFMS.Client`. The client's `wwwroot/appsettings.Development.json` sets `ApiBaseUrl` to `https://localhost:7148/`, matching the API launch profile. Update this setting when using a different API URL; never put secrets in client configuration.
- The API allows the development client origins `https://localhost:7246` and `http://localhost:5273`. For other origins or deployment, set the API's `Cors:AllowedOrigins` array explicitly. Outside Development, the client defaults to its own origin unless `ApiBaseUrl` is configured in client settings.
- Login accepts email or phone number. Unchecked "Remember me" stores the token session in session storage; checked stores it in local storage. Passwords are never persisted. `AuthSession` restores the session and rotates an expired access token through `api/auth/refresh` before returning authenticated state. Server-side authorization remains authoritative.
- Register sends `RegisterRequest` to `api/auth/register`, then shows a success screen linking to Login. Validation errors appear beside the relevant fields; API/network failures remain on the form so the user can retry. Password reset and password change screens are outside this implementation's current scope.
- Logout is available from authenticated public navigation. Clicking the button posts the current refresh token to `api/auth/logout` with a Bearer access token, refreshing access first if needed. Clear the in-memory session, session storage, and local storage, publish anonymous UI state, then return to Home. No confirmation or completion screen is shown. An API/network failure still ends the local session and returns Home; local cleanup alone does not guarantee server-side revocation.
- Run authentication client tests with `dotnet test tests/VFMS.ClientTests/VFMS.ClientTests.csproj`. These tests use fake HTTP/storage and do not create database accounts; verify the full registration/login flow separately against a configured API and database.

### Planned organization

The following is a target structure, not a claim that all folders, layouts, components, or services already exist:

```text
VFMS.Client/
├── Pages/                          # Organized by the 7 Actors + Kiosk Mode
│   ├── Public/                     # Guest views: Home, GymDetails, Trainers, YogaSchedule, Competitions, TrainerApplication
│   ├── Auth/                       # Login, Register, VerifyOtp, ResetPassword
│   ├── Customer/                   # CustomerDashboard, PackageCatalog, Checkout, Transactions, Supplements, AiWorkout, FaceSetup
│   ├── Member/                     # MemberDashboard, MyMemberships, FreezeRequests, PTSchedule, PTProgress, MyYogaBookings, MyLocker, EquipmentReport, GymReview
│   ├── PT/                         # TrainerProfile, AvailabilityConfig, AssignedMembers, PTFreezeReview, AssignedYogaClasses, YogaAttendance
│   ├── Staff/                      # WalkInAccount, ManualCheckInOut, ActiveCheckIns, LockerRegistry, LockerTermination, OverdueLockers, EquipmentIssues, PurchaseProposals, CompetitionRoster, SupplementOrders
│   ├── Manager/                    # MembershipPlans, FreezePolicies, FreezeApprovals, Promotions, TrainerScreening, PTPackages, YogaStudiosAndClasses, EquipmentAndRepairs, ProposalApprovals, SupplementCatalog, Announcements, FeedbackManagement, BusinessReports, GeneralPolicies
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

- **JWT Bearer Transport (`GBR-28`):** Attach the 30-minute JWT Access Token via a custom `AuthorizationMessageHandler` on all authenticated API calls, and automatically refresh using the Refresh Token before redirecting to `/auth/login` with `MSG-11` on expiration.
- **Strict Alignment with the 7 Use Case Diagrams & Permission Matrix:**
  - `Guest`: Can access `Register Account`, `Submit Trainer Application`, `View Personal Trainers`, `View Yoga Schedule`, `View Competitions & Leaderboard`, and `View Gym Details & Facilities`.
  - `Customer`: Inherits `Guest` (except `Register Account`). Can access `Purchase Membership Packages`, `Apply Voucher`, `View Transaction History`, `Export Invoice`, `Purchase/Track/Cancel/View Supplements`, `Generate/Manage Personalized Workout Plan`, `Manage Face Recognition Profile`, and `View Notifications / Gym Announcements`.
  - `Member`: Inherits `Customer`. Unlocks `View Membership Details`, `Renew Membership`, `Manage & Submit Membership Freeze Request`, `View PT Schedule Options`, `Purchase PT Package`, `View PT Package Progress`, `Manage PT Freeze Request`, `Book/Cancel/View Yoga Bookings`, `Check In / Check Out`, `Report Equipment Issue`, `Manage Competition Registration / History`, `View My Locker Rental`, and `Review Gym & Services`.
  - `PT`: Accesses trainer profile, availability, assigned members/progress, schedule-change proposals, assigned Yoga classes, attendance and own notifications. PT may view and advise on assigned freeze requests; approval/rejection belongs to Manager only.
  - `Staff`: Accesses `Create Customer Account` (locked to `Customer` role per `GBR-30`), `Check In/Out Member`, `View Active Check-ins`, `Handle Equipment Issues`, `Manage Purchase Proposals`, `Manage Competition Participants & Results`, `Manage Supplement Orders`, `Manage Locker Registry & Contracts`, `Terminate Locker & Settle Deposit`, `Handle Overdue Lockers & Maintenance`, and `View Notifications / Gym Announcements`.
  - `Manager`: Accesses all operational configuration, approval, product/schedule management, announcements, and `View Business Reports` screens per UCD.
  - `Admin`: Accesses `Manage Staff Accounts`, `Assign Roles`, `Lock/Unlock Account`, `Manage Banned Words`, `View System Audit Logs`, own notifications and announcement management alongside Manager.

## 5. UI Handling for Updated VFMS Workflows

### 5.1 Unified Multi-Service Cart, Order, Voucher Cap & VietQR Checkout
- Render single-service and combo packages from `Package` and `PackageComponent` (`GYM`, `YOGA`, `PT`), clearly displaying duration, session count, access hours, and freeze allowances.
- Allow adding multiple packages into a single `Order` cart, selecting activation start dates (immediate or deferred up to 30 days per `GBR-35`), and specifying `BeneficiaryUserId` (buying for self or gifting per `GBR-36`).
- On the Checkout screen, support applying a single voucher code (`GBR-45`). Display the subtotal, minimum spend requirement (`MinimumSpend`), percentage cap (`MaxDiscountAmount` per `GBR-46`), and calculated `DiscountAmount` returned by the API.
- Display the dynamic VietQR code (`API-04`) with a live **15-minute countdown timer** (`GBR-48`, `JOB-01`) and poll/listen for SePay webhook confirmation (`API-03`) to transition the screen to Payment Success (`MSG-23`) and enable PDF Invoice download (`Export Invoice`).

### 5.2 Membership Freeze, PT Scheduling & PT Freeze
- Display remaining freeze quota, min/max days (`GBR-40`, `GBR-41`), and advance notice rules (`GBR-42`). Disable freeze actions for weekly Yoga packages (`GBR-85`).
- For PT booking (`UC-27`, `UC-29`), check that the user holds an active gym membership (`GBR-10`, `MSG-31`). Render available PT schedule options and temporary freeze-window availability notices (`GBR-17`).
- Provide approval/rejection controls only for Manager. Assigned PT sees request details and can advise. Preview replacement dates and the exact frozen-day extension; never show a future approved request as already Frozen.
- Purchase selects trainer, package, start date and fixed recurring days/time. Preview all generated appointments; a two-week view does not limit booking to 14 days. Display a 15-minute hold deadline from the server and a separate late-payment reconciliation state.
- Show Used/Reserved/Available/Remaining credits distinctly from appointment counts. One credit is 60 minutes; a double appointment uses two. Explain cancellation penalties and request explicit member confirmation for trainer-proposed changes.
- Public trainer cards link to View My PT Schedule or Explore Packages, not rolling Book Session. Credential edits show pending approval while the approved profile stays public. Recruitment screens support tracking, 30-day cooldown and recruitment restrictions (GBR-96/97); onboarding uses setup links.

### 5.3 Yoga Timetable, Waitlist, No-Show Penalty & Attendance
- Display weekly Yoga sessions (`View Yoga Schedule`) with studio name, floor, instructor, difficulty, and live free-seat counter.
- When `Confirmed` bookings reach `MaxParticipants`, change the primary CTA from `"Book Class"` to `"Join Waitlist (Free)"` (`GBR-80`, `GBR-82`).
- When cancelling a booking `< 2 hours` before start time, display a confirmation modal with `MSG-34` warning that late cancellation counts as a no-show (`GBR-83`) and that 3 no-shows within 30 days suspend Yoga booking for 7 days (`GBR-84`).
- Provide the `Take Yoga Attendance` roster screen for `PT` (Instructor) with a 30-minute submission countdown (`GBR-86`).

### 5.4 Locker Rental, Kiosk Biometrics & AI Workout Plan
- **Locker Rental:** Show the actual contract deposit (default 100,000 VND), Reserved/PendingHandover after online payment, 24-hour collection deadline, and Active/Occupied only after Staff confirms key handover. Include Available, Overdue and Maintenance states. Explain renewal dates, itemized overdue/key charges, refund requested versus paid, and additional amounts due. Forced clearance records both witnesses and inventory; safe inspection precedes availability.
- **Biometrics & Kiosk (`UC-52` to `UC-57`):** Require unchecked-by-default biometric consent before opening the camera. Show check-in, eligibility and anti-passback feedback. PT check-in records attendance only; do not display automatic session completion or consumed credits. Align MSG-38 wording before reusing it.

- **AI Workout Plan (`GBR-93` to `GBR-95`):** Require health-survey consent, show a non-blocking loading state, label standard fallback plans, and allow exercise completion toggling.

### 5.4.1 Notifications and announcements (Report 3 section 6)
- All authenticated roles, including PT, access their own Notification Center. Archive read/unpinned records after 90 days; do not hide retained unread/pinned records merely because they are older. Handle inaccessible deep-link destinations without exposing content.
- Manager/Admin manage Draft, Scheduled, Published, Expired and Archived announcements with target audience, publish/expiry times (maximum 60 days), sanitized rich text and optional media. A maximum of 3 active pins applies; scheduled overflow publishes unpinned and informs the author. Edits do not automatically rebroadcast.
- Guest sees Public only; signed-in users see Public and their role audiences. Backend enforces the same rules for lists and detail views. UI hiding alone is insufficient.

### 5.5 Equipment, Procurement, Supplements, Competitions & Moderation
- **Equipment & Procurement:** Member submits issue reports (`UC-71`); Staff triages issues (`Resolve`, `Escalate`, `Mark as Safe`) and builds multi-item purchase proposals with dynamic total budget calculation (`GBR-62`); Manager manages repairs, equipment lifecycle, and proposal approvals (`GBR-68`).
- **Supplement Store:** Display `Available` products without stock counts (`GBR-69`, `GBR-70`). Support `GymPickup` (pay at counter) and `HomeDelivery` (COD) (`GBR-58`). Show `"Cancel Order"` button only while status is `Pending` (`GBR-60`).
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
