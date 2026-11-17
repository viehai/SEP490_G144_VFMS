# How to test dev/haiduc: check-in, yoga, workout plans

Branch `dev/haiduc`. Every feature below has a screen, in the shared Soft Black / Muted Red / Warm Neutral style (`VFMS_FRONTEND_GUIDELINES.md` §6). Swagger is only needed for the extra checks marked **API**.

## 1. Run it

Build once, then start each server in its own terminal, from the repo root. A server started this way keeps running until you press Ctrl+C.

```powershell
dotnet build src/VFMS.API/VFMS.API.csproj; dotnet build src/VFMS.Client/VFMS.Client.csproj
```
```powershell
# API (https://localhost:7148). Uses Windows login to the local VfmsDb, because the sa login in appsettings fails here.
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=VfmsDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true'; dotnet run --project src/VFMS.API --launch-profile https --no-build
```
```powershell
# Client (https://localhost:7246)
dotnet run --project src/VFMS.Client --launch-profile https --no-build
```

- Open the client in **Edge or Chrome**. The camera (Face ID and QR scanning) doesn't work in Claude's built-in browser.
- After a rebuild, press **Ctrl+F5** so the browser picks up the new code.
- **When a page shows an error, open the browser console (F12).** Every failed call on these pages logs one line that names it:
  - Format: `VFMS API <method> <path> -> <status> <code>: <message>`, for example `VFMS API POST api/check-ins/manual -> 409 Conflict ANTI_PASSBACK: …`.
  - A 4xx line is a warning: a business rule refused the request. No response, a 5xx or a bad body is an error.
  - Codes made on the client: `NETWORK_ERROR` (API not running, TLS or CORS), `TIMEOUT`, `EMPTY_RESPONSE`, `INVALID_RESPONSE`. With `INVALID_RESPONSE` the line also says `(expected <DTO>)`: the API's response no longer matches the client's contract, usually after a change on one side.
  - Any other code comes from the API.
  - Camera and QR failures log the JavaScript function that failed, for example `Kiosk: vfmsQr.scan (qr-code.js) failed`.
- Swagger: https://localhost:7148/swagger. Log in with `POST /api/auth/login` (`{ "email", "password" }`), copy `accessToken` and paste it under **Authorize**.

### Gemini key (AI workout plans)

Without a key, plan generation falls back to the built-in standard plan. The key goes in **user-secrets**, which are stored in your Windows profile, outside the repo:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY" --project src/VFMS.API
```

- Get a key at https://aistudio.google.com/app/apikey, then restart the API.
- The model is `gemini-3.5-flash-lite` (`Gemini:Model` in appsettings.json). `gemini-2.0-flash` and `gemini-2.5-flash` return 404 for new keys.
- Never put the key in `appsettings*.json`; those files are committed.

## 2. Test accounts (local VfmsDb)

| Role           | Email           | Password  |
|----------------|-----------------|-----------|
| Admin          | admin@vfms.vn   | Admin@123 |
| Manager        | manager@test.vn | Test@1234 |
| Staff          | staff@test.vn   | Test@1234 |
| YogaInstructor | yoga@test.vn    | Test@1234 |
| Member         | member@test.vn  | Test@1234 |
| Member (demo)  | demo1@example.com, demo2@example.com, demo3@example.com | Test@1234 |
| Member (yoga only) | yoga1@example.com | Test@1234 |

Member user id: `b7a2e8b5-23e2-4415-8679-357bb5c33ad3`. Use an InPrivate window to be signed in as two roles at once.

The three demo members (Demo Hội Viên Một / Hai / Ba, phones `0900000011`–`0900000013`) were made for the live check-in demo through the real flow: register, buy "Gym 1 Month" with cash, cash confirmed by staff (16/11/2026). They are valid to 15/12/2026, with gym and yoga. They exist only in this local database. On 16/11 Demo Hai was left checked in and Demo Một checked out; on 17/11 Demo Một was checked in again at the front desk (gym check-in test).

Demo Yoga (`yoga1@example.com`, phone `0900000021`) has only a yoga plan. It was registered on 16/11/2026 and bought "Yoga 1 Tháng" (600.000 ₫, 30 days) on the home page with cash; staff confirmed the cash on `/staff/membership-cash-receipt` (order ORD-20261116-72A8729070BF). The yoga plan is valid 16/11 to 15/12/2026, and the account has booked Hatha Morning on mat 1.

Data that already exists:
- The member has "Gym 1 Month" (order ORD-20261112-367A858627B3, paid in cash), valid 12/11 to 11/12/2026. The member also has a YOGA membership, added by SQL (see section 6).
- The member's face is registered.
- Yoga: Studio A and Studio B, and class "Hatha Morning" (Open) with sessions from 19/11 at 08:00.
  - The member's active booking is on **mat 5** (booking 1, mat 3, was cancelled during testing).
  - Session 3 was added for 16/11 at 19:07 for an attendance test; its roster is submitted (member Có mặt), so it's locked. Add a new "now" session (section 5) to test attendance again.

## 3. Check-in and check-out

### Kiosk: `/kiosk`

Open to Staff, Manager, Customer and Member.
- **Staff and Manager** can scan anyone.
- **Customers and Members** can only scan themselves: a face or QR that belongs to someone else is refused.

**One scan decides the direction.** A member who is outside gets **CHECK-IN**; a member who is inside gets **CHECK-OUT**.

1. Press **Bật camera** and allow camera access.
2. **Face ID:** look at the camera and press **Quét khuôn mặt**. It tries for about 3 seconds.
3. **QR:** press **Quét mã QR** and hold the member's QR in front of the camera, for up to 10 seconds.

Things that look like errors but are the rules:
- **Anti-passback:** a new check-in within **15 minutes** of the last check-in is refused (409, "Anti-passback cooldown").
- **QR codes** work once, expire after **2 minutes**, and are **replaced each time `/my-qr` is opened**. To scan a photo of the code: open `/my-qr` once, take the photo, don't reload, and scan within 2 minutes.
- "No active membership found or biometric verification failed" means one of these:
  - the face didn't match;
  - the QR was used, expired or replaced;
  - the person has no usable membership (then the message adds "Membership status: X").

### Member pages

- `/my-qr` (Customer, Member): the check-in QR image, its expiry time and a **Tạo mã mới** button.
- `/face-id` (Customer, Member): consent, camera capture, delete.
- The profile page's **TIỆN ÍCH HỘI VIÊN** box links to these pages, the kiosk, the yoga schedule and the workout plan.

### Front desk: `/staff/check-ins` (Staff, Manager)

1. **Check-in thủ công:** search by name, phone (`0900000004`) or order number, then press **Check-in**. The button is off for members without an active membership; a member already inside shows "Đang trong phòng tập".
2. **Đang có mặt:** who is inside, the occupancy, today's check-ins and the peak flag. **Check-out** on a row asks once, then records the staff member who did it.
   - The list **updates itself every 5 seconds**, including check-ins and check-outs made at the kiosk or another desk. The line under the title shows "Trực tiếp · cập nhật lúc HH:mm:ss", or "Mất kết nối, đang thử lại…" while the API is down. A new row is tinted.
   - A filter that matches no one shows "Không tìm thấy kết quả phù hợp." (MSG-01); the occupancy cards still count everyone.
3. A second check-in within 15 minutes of the last one is refused (anti-passback).

**Live demo (two front desks):**
1. Edge window A (normal): sign in as `staff@test.vn`, open `/staff/check-ins`. Don't touch it from now on.
2. Edge window B (InPrivate): sign in as `manager@test.vn`, open `/staff/check-ins`.
3. In B, search `0900000011` and press **Check-in**. Within 5 seconds A shows Demo Hội Viên Một, and "Đang có mặt" goes up by 1.
4. In B, press **Check-out** on that row. Within 5 seconds the row leaves A.
5. Repeat with `0900000012` and `0900000013`. Each demo member can only check in again 15 minutes after their last check-in (anti-passback), which is why there are three.
6. The kiosk works the same way: a scan at `/kiosk` shows up in A within 5 seconds.

### API (Swagger)

- **Kiosk endpoints:**
  - `POST /api/kiosk/scan` checks in or out on its own.
  - `POST /api/kiosk/check-in` and `POST /api/kiosk/check-out` still exist.
  - Body for all three: `{ "qrToken": "<token from POST /api/my-qr>" }` or `{ "descriptor": [128 numbers] }`.

## 4. Workout plan: `/workout` (Member)

1. Fill in the survey.
2. Tick the **Gemini consent** box. The button changes to **Lưu khảo sát & tạo giáo án**.
3. Press it. The survey is saved and a new plan is generated, and the old plan is archived. Without consent, the button only saves the survey.

Then try the rest:
- tick exercises done (the progress counter goes up);
- **Đổi bài tập** to swap an exercise;
- check the plan history.

How to tell which plan you got:
- **AI plan:** its name follows your goal, and the header doesn't say "Giáo án tiêu chuẩn".
- **Standard plan:** shows "Dịch vụ AI tạm thời không khả dụng…". It means there's no key, Gemini failed, or it took longer than 25 s. The API log line `Gemini returned HTTP …` gives the reason.

## 5. Yoga

| Page | Sign in as | Try |
|---|---|---|
| `/manager/yoga` | Manager | Studios (create, edit, deactivate), class types, classes (create, publish, cancel), sessions |
| `/yoga/schedule` | anyone | A week timetable (days across, start times down; a day-by-day list on phones) with free mats, filters and next/previous week |
| `/pt/yoga` | YogaInstructor | This week's assigned sessions with booked members, and **Điểm danh** per session |
| `/yoga/my-bookings` | Member | Upcoming (with cancel), past (Có mặt / Vắng) and waitlist tabs |
| `/staff/yoga` | Staff | The day's sessions; **Điểm danh** opens the roster |

**Yoga plans (sell and buy):**
- Manager, `/manager/membership-plans` → **Thêm gói mới**: pick **Loại dịch vụ: Yoga**. The type can't be changed after the plan is created. The list has a **Dịch vụ** column and a filter.
- Customer or Member, home page → **GÓI THẺ & LỊCH TẬP** → **YOGA STUDIO** tab: only yoga plans are listed (the GYM MEMBERSHIP tab lists only gym plans). **Thêm gói tập** → choose **Tiền mặt tại quầy** or VietQR → **Tạo đơn thanh toán**. Signed-out visitors see the same plans from the database (the catalog API `GET /api/membership-plans` is public), with **Tạo tài khoản** (to `/auth/register`) and a small **Đăng nhập để mua** link instead of **Thêm gói tập**; Staff, Manager and other roles see the plans with a note that only customers and members can buy. The old hard-coded sample cards are gone, but the cards keep their look and are filled from the plan:
  - the plan's **Mô tả** (description) gives the text: the first line is the short tagline under the name, and every further line is one ✓ benefit. A one-line description shows only the tagline.
  - "Tương đương X ₫ / tháng" is the price divided by the term in 30-day months (365 days = 12 months).
  - "TIẾT KIỆM N%" is worked out against the dearest per-month plan on the same tab (rounded down), so it is never typed by hand. There is no "PHỔ BIẾN NHẤT" badge any more, because no plan field says which one is popular.
  - Cards are sorted from the shortest term to the longest.
- **Gym plans in the local database (17/11/2026).** Three gym plans copied from the old sample cards were created through the Manager API: **3 Tháng** (2.400.000 ₫, 90 days), **6 Tháng** (4.200.000 ₫, 180 days) and **1 Năm (12 Tháng)** (7.200.000 ₫, 365 days). "Gym 1 Month" (500.000 ₫) was set to **INACTIVE**, so it is no longer sold; members who bought it keep their memberships. The old freeze-day lines ("Bảo lưu thẻ (Freeze): tối đa 15/30/60 ngày") were left out, because no freeze policy is set up in this database (FreezePolicies and FreezePolicyTierQuotas are empty). These plans are only in this local database, not in DbInitializer.
- Yoga plans are sold only while at least one yoga class can be booked: a published class (Open, or Full with a waitlist) whose registration deadline hasn't passed (`YogaClassRules.Bookable`, the same rule as booking). Otherwise the yoga tab says "Hiện chưa có gói Yoga mở bán.", the plan's catalog page is not found, and buying it is refused with `PLAN_NOT_PURCHASABLE`. The Manager still sees and edits the plan. Gym plans are not affected. To test: cancel every yoga class on `/manager/yoga`, or let their registration deadlines pass. In the local database the only bookable class, Hatha Morning, closes registration on **18/11/2026 17:00** (gym time), so "Yoga 1 Tháng" stops selling then: create and publish another class on `/manager/yoga` to keep selling yoga plans.
- Payment is the same as for gym plans. With cash, staff enter the order number and amount on `/staff/membership-cash-receipt`; VietQR is confirmed by the bank webhook. After payment a Customer becomes a Member, and `/my-memberships` shows the plan with **Dịch vụ: Yoga**.
- A yoga plan lets the member book every yoga class but gives no gym access. The kiosk and the front desk only count gym plans (`MembershipRules.Gym` in `GymAccessServices.cs`): a yoga-only member is refused with "Membership status: No gym plan.", and the front-desk search shows **Chưa có gói Gym**. A member with gym and yoga checks in on the gym plan. Check-out is never blocked.
  - Try it: front desk `/staff/check-ins`, search `0900000021` (Demo Yoga) → Chưa có gói Gym, check-in refused; `0900000011` (Demo Một) → Active.
  - A yoga-only member still sees and can buy gym plans (home → GYM MEMBERSHIP tab). Other features that need a gym plan refuse them with a message saying so: PT packages ("Cần gói Gym đang hoạt động…") and locker rental ("Hội viên cần có gói Gym đang hiệu lực.").
  - Still open to Customers and to yoga-only members, because they need no plan: `/workout` (AI workout plan), `/face-id`, and `/my-qr` (yoga attendance scans it). Equipment-issue reports stay open to every Member.
- API: `POST /api/manager/membership-plans` takes `"serviceType": "YOGA"` (default `GYM`); `GET /api/membership-plans?serviceType=YOGA` and the manager list filter by it.

**Book (Member, on `/yoga/schedule`):**
- Each session shows **Đặt chỗ**, or **Vào danh sách chờ (miễn phí)** when the class is full.
- A booking is **per class**: one booking holds the mat for every session of that class. Pick a mat or leave it to the system.
- For a signed-in Member, sessions of a class they already booked or are waitlisted for are tinted red and show **Đã đặt · thảm N** or **Đang chờ · #N**, with a link to `/yoga/my-bookings` instead of a booking button (GBR-79: one booking per class). The API refuses a second booking anyway (`ALREADY_BOOKED`).
- There is no time-clash check between different classes; only the same class is refused.
- To test the waitlist, use a class with maxParticipants 1 and a second member.

**Cancel (Member, on `/yoga/my-bookings`):**
- More than 2 hours before the next session it cancels after one confirm.
- Within 2 hours a warning explains it counts as a no-show (3 in 30 days suspend booking for 7 days). **Giữ chỗ** keeps it; **Vẫn hủy** cancels.

**Attendance (YogaInstructor on `/pt/yoga`, Staff on `/staff/yoga`):**
- Mark each member **Có mặt** or **Vắng**; **Gửi danh sách** stays off until everyone is marked. After submitting, the roster is locked.
- **Quét mã QR** opens the camera and marks the member whose `/my-qr` code is shown.
- Attendance opens 30 minutes before the start. A submit more than 30 minutes after the start is flagged "trễ"; the Manager sees it on the class's sessions.

To make a session "now" for attendance tests (the API needs 7+ days' notice for sessions of a published class):
```sql
-- gym local time (UTC+7); change the date and times
INSERT YogaClassSchedules (YogaClassId, YogaStudioId, ScheduledDate, StartTime, EndTime, ScheduleStatus, CreatedAt, UpdatedAt, IsDeleted, InstructorUserId)
SELECT YogaClassId, YogaStudioId, CAST(GETDATE() AS date), '21:00', '22:00', ScheduleStatus, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, InstructorUserId
FROM YogaClassSchedules WHERE Id = 1;
```
The roster only lists bookings made before the session starts.

## 6. Known gaps

- **One integration test from dev/viehai fails on SQLite.** `MembershipPurchaseApiTests.InvoicePdfPreservesMaximumPersistedDecimalPrecisionWithLongHistoricalItemName` comes from dev/viehai (commit 0642777, invoice PDF export) and tests that feature at the largest `decimal(18,2)` amount. The test's own SQLite database can't hold that many digits (`9999999999999999.99` comes back as `10000000000000000`), so it fails on main too. It isn't caused by this branch. Expected result: Unit 489, Client 298 and Integration 453 pass, with that 1 failing and 21 skipped (SQL Server-only tests). To run the rest without it:
  ```powershell
  dotnet test tests/VFMS.IntegrationTests --filter "FullyQualifiedName!~InvoicePdfPreservesMaximumPersistedDecimalPrecision"
  ```

- **Yoga plans can't be renewed yet.** Renewal (`/my-memberships` → gia hạn) still only covers gym plans, so a yoga member buys a new yoga plan instead; it starts after the current one ends.
- **Old SQL yoga parts.** The member and the three demo members got their YOGA part by SQL inside the gym package "Gym 1 Month", before yoga plans existed. So `/my-memberships` shows two cards named "Gym 1 Month"; the second says **Dịch vụ: Yoga**. New yoga plans don't need this.
- **Self check-in from anywhere.** Because the kiosk is open to Customers and Members, one can check in from anywhere with `/my-qr` plus `/kiosk` on one phone. That check-in can also mark a PT session as done.
- **Phones can't open localhost.** To test QR with a phone, photograph the code shown on the laptop.
