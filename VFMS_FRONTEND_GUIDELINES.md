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

## 6. Design System & UI Vibe (Nike-Inspired Athletic Theme)

### A. Color Palette & Dark Mode Vibe
- **App Background (`--bg-main`):** `#0A0A0C` (Deep charcoal black).
- **Card & Container Background (`--bg-card`):** `#141417` (Dark grey surface).
- **Borders & Dividers (`--border-card`):** `#232329` (Subtle 1px dark borders, no heavy drop-shadows).
- **Brand Accent Color (`--color-accent`):** `#FF6B00` (Energetic athletic orange for primary CTAs, active states, and key highlights).
- **Typography Colors:** Primary `#FFFFFF` (pure white), Secondary `#9CA3AF` (light grey).

### B. Typography
- Headings (`h1`–`h6`, nav links, banners, CTAs): `'Montserrat'` or `'Inter'`, bold (`700`–`800`), `text-transform: uppercase`, `letter-spacing: 0.05em`.
- Body copy, tables, inputs, validation messages: `'Inter'` or `'Roboto'`, `font-weight: 400`–`500`, normal case for readability.

### C. Component Styling & Status Badges (`StatusBadge.razor`)
- Cards use `border-radius: 12px` (or `16px`), `1px solid var(--border-card)`, and `transition: all 0.2s ease` on interactive hover.
- **Status Colors (always paired with text/icon, never color alone):**
  - *Active / Approved / Confirmed / Completed / Paid / Available:* Emerald Green (`#10B981`)
  - *Pending / Processing / Frozen / Scheduled / Waitlisted:* Amber Orange (`#F59E0B`)
  - *Rejected / Expired / NoShow / Overdue / Locked / Escalated:* Warning Red (`#EF4444`)
  - *Cancelled / Inactive / Archived / Retired:* Neutral Grey (`#4B5563`)

---

## 7. AI Agent Prompt (For UI Code Generation)

*When using an AI assistant (Cursor, GitHub Copilot, Claude, ChatGPT, Gemini) to generate Blazor UI components, always provide this section as context to ensure the output strictly matches the project's architecture and design system.*

**[BEGIN PROMPT FOR AI AGENT]**

Bạn là một Chuyên gia Blazor WebAssembly Frontend Developer (.NET 8 + Tailwind CSS) và UI/UX Designer. Khi viết mã Razor Components (`.razor`), Scoped CSS (`.razor.css`), Layouts, hoặc bất kỳ thành phần giao diện Blazor nào cho dự án **VFMS (`VFMS.Client`)**, bạn PHẢI tuân thủ tuyệt đối kiến trúc Clean Architecture (chỉ dùng DTO/Validator từ `VFMS.Contracts`) và Hệ thống Thiết kế **"Nike-Inspired Athletic Theme"** với các quy tắc nghiêm ngặt dưới đây:

**1. PHONG CÁCH THIẾT KẾ CỐT LÕI (NIKE-INSPIRED ATHLETIC THEME)**
- **Hình khối:** Bắt buộc sử dụng thẻ phẳng (flat rectangular cards) với góc bo tròn nhẹ `border-radius: 12px` hoặc `16px`.
- **Hiệu ứng:** KHÔNG sử dụng bóng đổ nặng nề (excessive drop shadows).
- **Viền:** Sử dụng viền mỏng `1px solid var(--border-card)` cho các thẻ card.
- **Tương tác (Hover):** Bắt buộc có hiệu ứng chuyển đổi mượt mà `transition: all 0.2s ease` khi hover qua các thẻ card, nút bấm, hoặc hàng (row) có tính tương tác, làm nổi bật viền hoặc các chi tiết màu accent.

**2. TYPOGRAPHY (KIỂU CHỮ)**
- **Tiêu đề (Headings h1-h6, Logo, NavLink, Banner, Primary CTA):**
  + **Font:** Dùng font sans-serif hiện đại (`'Montserrat'` hoặc `'Inter'`).
  + **Font-weight:** Bắt buộc in đậm (`700` hoặc `800`).
  + **Transform:** Bắt buộc viết hoa toàn bộ (`text-transform: uppercase`).
  + **Letter-spacing:** Gọn gàng (`0.05em`) để thể hiện tinh thần thể thao mạnh mẽ, trực diện.
- **Nội dung & Form (Body text, Paragraph, Input, `<ValidationMessage>`, Table):**
  + **Font:** `'Inter'` hoặc `'Roboto'`.
  + **Font-weight:** `300`, `400`, `500`.
  + Tuyệt đối không uppercase bừa bãi ở phần nội dung dài để giữ độ dễ đọc.

**3. QUY TẮC MÀU SẮC (Sử dụng CSS Variables toàn cục kết hợp Tailwind CSS)**
Bắt buộc sử dụng các biến CSS sau thay vì mã màu Hex cứng trong các file `.razor.css`:
- `--bg-main`: `#0A0A0C` (Màu nền toàn trang - Đen than chì sâu, mang lại cảm giác cao cấp).
- `--bg-card`: `#141417` (Màu nền của các block nội dung, form, thẻ).
- `--border-card`: `#232329` (Màu viền tối, tinh tế thay cho drop-shadow).
- `--color-accent`: `#FF6B00` (Màu cam thể thao năng động - Dùng DUY NHẤT cho Nút CTA chính, link đang active, và các icon cần nổi bật).
- **Typography:** Chữ chính: `#FFFFFF` (pure white). Chữ phụ/caption/placeholder: `#9CA3AF` (light grey).

**4. QUY CHUẨN MÀU SẮC TRẠNG THÁI (STATUS BADGES)**
Khi thiết kế component hiển thị trạng thái (`StatusBadge.razor`), phải tuân thủ bảng màu sau:
- **Active / Approved / Confirmed / Completed / Paid / Available:** Emerald Green (`#10B981`)
- **Pending / Processing / Frozen / Scheduled / Waitlisted:** Amber Orange (`#F59E0B`)
- **Rejected / Expired / NoShow / Overdue / Locked / Escalated:** Warning Red (`#EF4444`)
- **Cancelled / Inactive / Archived / Retired:** Neutral Grey (`#4B5563`)

**5. YÊU CẦU KỸ THUẬT & NGHIỆP VỤ ĐẦU RA CHO BLAZOR (`VFMS.Client`)**
1. **Cấu trúc Component:** Phân tách rõ ràng phần Directive (`@page`, `@inject`, `@using VFMS.Contracts...`), phần HTML UI, và khối `@code { }`.
2. **Quản lý Trạng thái UI:** Mọi màn hình tải dữ liệu hoặc gửi form bắt buộc phải xử lý đủ 4 trạng thái: `Loading`, `Empty` (hiển thị `MSG-01`), `Error` (hiển thị thông báo thân thiện từ Problem Details / `MSG-xx`), và `Success`. Vô hiệu hóa nút bấm (`disabled`) khi đang gửi request để chống bấm đúp.
3. **Tuân thủ Nghiệp vụ Mới Nhất:**
   - **Mua gói tập & Giỏ hàng (`Order` & `OrderItem`):** Hỗ trợ chọn gói đơn lẻ hoặc Combo (`Package` / `PackageComponent`: `GYM`, `PT`, `YOGA`), chọn người thụ hưởng (`BeneficiaryUserId`), chọn ngày kích hoạt (tối đa 30 ngày), áp 1 mã Voucher (hiển thị rõ `MinimumSpend` và trần giảm giá `MaxDiscountAmount`), và hiển thị mã VietQR động kèm đồng hồ đếm ngược 15 phút.
   - **Phân quyền 7 Roles (`Guest`, `Customer`, `Member`, `PT`, `Staff`, `Manager`, `Admin`):** Sử dụng `<AuthorizeView Roles="...">` và `@attribute [Authorize(Roles = "...")]` bám sát Permission Matrix trong SRS.
   - **PDPL Consent:** Các màn hình Đăng ký tài khoản, Đăng ký khuôn mặt Face ID, và Khảo sát sức khỏe AI Workout bắt buộc có checkbox đồng ý điều khoản (mặc định chưa tick).

**[END PROMPT FOR AI AGENT]**
