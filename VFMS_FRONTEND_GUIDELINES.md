# VFMS Frontend Guidelines

## 1. Purpose

This document defines architecture, component, workflow, and styling conventions for the **`VFMS.Client`** Blazor WebAssembly + Tailwind CSS project inside the `SEP490_G144_VFMS` Clean Architecture solution. The frontend handles presentation, client-side usability validation, and role-specific navigation; all authoritative business rules, payment confirmations, and persistence reside in `VFMS.API` and `VFMS.Application`.

## 2. Responsibilities and Architectural Boundaries

- `VFMS.Client` is built with **Blazor WebAssembly on .NET 8 (`net8.0`)** styled with **Tailwind CSS** and Scoped CSS variables.
- Reference **only `VFMS.Contracts`** for shared Request/Response DTOs, FluentValidation validators, and standardized system message constants (`MSG-01` to `MSG-43`).
- Never reference `VFMS.Domain`, `VFMS.Application`, or `VFMS.Infrastructure`.
- Communicate with the backend exclusively through typed `HttpClient` services calling `VFMS.API` REST endpoints.
- Client-side validation and role-based UI hiding (`<AuthorizeView Roles="...">`) improve UX, but server validation and authorization are always authoritative.

## 3. Project Organization (`VFMS.Client`)

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
  - `PT`: Accesses `Manage Trainer Profile`, `Configure PT Availability Schedule`, `View Assigned Members`, `View PT Package Progress`, `Process PT Freeze Requests`, `View Assigned Yoga Classes`, and `Take Yoga Attendance` (note: PT uses `Manage Trainer Profile` rather than general `Manage Profile`, per UCD).
  - `Staff`: Accesses `Create Customer Account` (locked to `Customer` role per `GBR-30`), `Check In/Out Member`, `View Active Check-ins`, `Handle Equipment Issues`, `Manage Purchase Proposals`, `Manage Competition Participants & Results`, `Manage Supplement Orders`, `Manage Locker Registry & Contracts`, `Terminate Locker & Settle Deposit`, `Handle Overdue Lockers & Maintenance`, and `View Notifications / Gym Announcements`.
  - `Manager`: Accesses all operational configuration, approval, product/schedule management, announcements, and `View Business Reports` screens per UCD.
  - `Admin`: Accesses `Manage Staff Accounts`, `Assign Roles`, `Lock/Unlock Account`, `Manage Banned Words`, `View System Audit Logs`, and `View Notifications / Gym Announcements`.

## 5. UI Handling for Updated VFMS Workflows

### 5.1 Unified Multi-Service Cart, Order, Voucher Cap & VietQR Checkout
- Render single-service and combo packages from `Package` and `PackageComponent` (`GYM`, `YOGA`, `PT`), clearly displaying duration, session count, access hours, and freeze allowances.
- Allow adding multiple packages into a single `Order` cart, selecting activation start dates (immediate or deferred up to 30 days per `GBR-35`), and specifying `BeneficiaryUserId` (buying for self or gifting per `GBR-36`).
- On the Checkout screen, support applying a single voucher code (`GBR-45`). Display the subtotal, minimum spend requirement (`MinimumSpend`), percentage cap (`MaxDiscountAmount` per `GBR-46`), and calculated `DiscountAmount` returned by the API.
- Display the dynamic VietQR code (`API-04`) with a live **15-minute countdown timer** (`GBR-48`, `JOB-01`) and poll/listen for SePay webhook confirmation (`API-03`) to transition the screen to Payment Success (`MSG-23`) and enable PDF Invoice download (`Export Invoice`).

### 5.2 Membership Freeze, PT Scheduling & PT Freeze
- Display remaining freeze quota, min/max days (`GBR-40`, `GBR-41`), and advance notice rules (`GBR-42`). Disable freeze actions for weekly Yoga packages (`GBR-85`).
- For PT booking (`UC-27`, `UC-29`), check that the user holds an active gym membership (`GBR-10`, `MSG-31`). Render available PT schedule options and temporary freeze-window availability notices (`GBR-17`).
- Provide PT Freeze review screens (`UC-34`) for **both `Manager` and `PT`** roles.

### 5.3 Yoga Timetable, Waitlist, No-Show Penalty & Attendance
- Display weekly Yoga sessions (`View Yoga Schedule`) with studio name, floor, instructor, difficulty, and live free-seat counter.
- When `Confirmed` bookings reach `MaxParticipants`, change the primary CTA from `"Book Class"` to `"Join Waitlist (Free)"` (`GBR-80`, `GBR-82`).
- When cancelling a booking `< 2 hours` before start time, display a confirmation modal with `MSG-34` warning that late cancellation counts as a no-show (`GBR-83`) and that 3 no-shows within 30 days suspend Yoga booking for 7 days (`GBR-84`).
- Provide the `Take Yoga Attendance` roster screen for `PT` (Instructor) with a 30-minute submission countdown (`GBR-86`).

### 5.4 Locker Rental, Kiosk Biometrics & AI Workout Plan
- **Locker Rental:** Member views active locker details, 100,000 VND deposit status, and 7-day overdue grace warnings (`View My Locker Rental`). Staff manages the interactive locker grid (`Available`, `Occupied`, `Overdue`, `Maintenance`), key handover, deposit refund/forfeiture (`MSG-40`), and forced clearance with Manager.
- **Biometrics & Kiosk (`UC-52` to `UC-57`):** Require an unchecked-by-default **PDPL Biometric Consent checkbox** before opening the camera on the Face ID registration screen. On the Kiosk screen, display `MSG-35` on success, `MSG-36` on inactive/frozen membership or face mismatch, `MSG-37` on 15-minute anti-passback violation, and `MSG-38` when a PT session is auto-completed.
- **AI Workout Plan (`UC-58`, `UC-59`):** Require PDPL Health Survey consent, show a non-blocking loading state while Gemini (`API-02`) generates the weekly routine, notify the user if a fallback standard plan was used (`GBR-95`), and allow daily exercise completion toggling.

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
  - **Layout:** Sticky Header, Minimalist Announcement Bar, Footer with generous padding.
- **Admin Back-Office:**
  - **Sidebar Background:** `#0A0A0A` (Pitch black).
  - **Accent Color:** `#FF3E3E` (Vivid Red for authority and system alerts).
- **Manager / Owner Back-Office:**
  - **Sidebar Background:** `#0F172A` (Deep Slate / Navy).
  - **Accent Color:** `#6366F1` (Indigo for revenue, reports, and business metrics).

### B. Typography
- Headings (`h1`–`h6`, nav links, banners, primary CTAs): `'Montserrat', sans-serif`, bold (`700`–`800`), `text-transform: uppercase`, `letter-spacing: 0.05em`.
- Body copy, tables, inputs, validation messages: `'Inter', sans-serif`, `font-weight: 400`–`500`, normal case for maximum readability.

### C. Component Styling, Strict Geometry & Status Badges (`StatusBadge.razor`)
- **Strict Geometry:** `border-radius: 0` (`rounded-none`). No rounded corners allowed anywhere. Flat, sharp, and brutal design.
- **No Drop-Shadows:** Do not use `shadow-md`, `shadow-lg`, etc. Depth and hierarchy are achieved entirely via high-contrast backgrounds and typography.
- **Buttons (`GymButton`):** Rectangular, heavy padding (`py-4 px-8`), uppercase, bold font. Hover states invert or darken sharply (e.g., `#0A0A0A` to `#000000`).
- **Cards (`ClassCard`):** No visible outer borders unless necessary, no shadow. Emphasize large sharp images (`aspect-[3/4]`), slight hover zoom (`group-hover:scale-105`), and left-aligned text.
- **Status Colors (always paired with text/icon, never color alone):**
  - *Active / Approved / Confirmed / Completed / Paid / Available:* Emerald Green (`#10B981`)
  - *Pending / Processing / Frozen / Scheduled / Waitlisted:* Amber Orange (`#F59E0B`)
  - *Rejected / Expired / NoShow / Overdue / Locked / Escalated:* Warning Red (`#EF4444`)
  - *Cancelled / Inactive / Archived / Retired:* Neutral Grey (`#4B5563`)

---

## 7. AI Agent Prompt (For UI Code Generation)

*When using an AI assistant (Cursor, GitHub Copilot, Claude, ChatGPT, Gemini) to generate Blazor UI components, always provide this section as context to ensure the output strictly matches the project's architecture and design system.*

**[BEGIN PROMPT FOR AI AGENT]**

Bạn là một Chuyên gia Blazor WebAssembly Frontend Developer (.NET 8 + Tailwind CSS) và UI/UX Designer. Khi viết mã Razor Components (`.razor`), Scoped CSS (`.razor.css`), Layouts, hoặc bất kỳ thành phần giao diện Blazor nào cho dự án **VFMS (`VFMS.Client`)**, bạn PHẢI tuân thủ tuyệt đối kiến trúc Clean Architecture (chỉ dùng DTO/Validator từ `VFMS.Contracts`) và Hệ thống Thiết kế **"Nike Brutal Minimalism / Gymshark"** với các quy tắc NGHIÊM NGẶT dưới đây:

**1. PHONG CÁCH THIẾT KẾ CỐT LÕI (BRUTAL MINIMALISM)**
- **YÊU CẦU TỐI THƯỢNG:** Sạch sẽ, tương phản cao, góc cạnh.
- **Hình khối (Geometry):** KHÔNG bao giờ dùng `border-radius` (luôn luôn `rounded-none`). Mọi nút bấm, thẻ card, ô input đều phải vuông vức sắc lẹm.
- **Hiệu ứng (Shadows):** KHÔNG sử dụng bóng đổ (`shadow-md`, `shadow-lg`, drop shadows). Chiều sâu được thể hiện qua màu nền và typography.
- **Tương tác (Hover):** Có hiệu ứng chuyển đổi mượt mà `transition-colors duration-200` ở nút bấm và zoom ảnh `group-hover:scale-105` cho thẻ card.

**2. TYPOGRAPHY (KIỂU CHỮ)**
- **Tiêu đề (Headings h1-h6, NavLink, Primary CTA):**
  + **Font:** Dùng font sans-serif `'Montserrat'`.
  + **Font-weight:** Bắt buộc in đậm (`700` hoặc `800`).
  + **Transform:** Bắt buộc viết hoa toàn bộ (`text-transform: uppercase`).
  + **Letter-spacing:** Gọn gàng (`tracking-wide` / `0.05em`) thể hiện tinh thần thể thao.
- **Nội dung & Form (Body text, Input, `<ValidationMessage>`, Table):**
  + **Font:** `'Inter'`.
  + **Font-weight:** `400`, `500`.
  + Tuyệt đối không uppercase ở phần nội dung dài để giữ độ dễ đọc.

**3. QUY TẮC MÀU SẮC & LAYOUTS (Tailwind Config / CSS Variables)**
- **Giao diện Người dùng (Customer/Guest/Member):** Nền trắng `#FFFFFF`, phân vùng nền xám nhạt `#F7F7F7`. Chữ đen tuyền `#252525` và chữ phụ `#7D7D7D`. Đường kẻ divider `#CFCFCF`. Nút bấm CTA màu đen `#0A0A0A` chữ trắng.
- **Giao diện Admin:** Dùng Layout có Sidebar nền Đen `#0A0A0A`, text trắng, màu accent Đỏ rực `#FF3E3E`.
- **Giao diện Owner/Manager:** Dùng Layout có Sidebar nền Xanh Slate `#0F172A`, text trắng, màu accent Chàm Indigo `#6366F1`.

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