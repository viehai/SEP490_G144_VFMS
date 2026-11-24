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
  - Format: `VFMS API <method> <path> -> <status> <code>: <message>`, for example `VFMS API POST api/check-ins/manual -> 409 Conflict ALREADY_CHECKED_IN: …`.
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
| Member (yoga Trung bình) | yoga2@example.com | Test@1234 |

Member user id: `b7a2e8b5-23e2-4415-8679-357bb5c33ad3`. Use an InPrivate window to be signed in as two roles at once.

The three demo members (Demo Hội Viên Một / Hai / Ba, phones `0900000011`–`0900000013`) were made for the live check-in demo through the real flow: register, buy "Gym 1 Month" with cash, cash confirmed by staff (16/11/2026). The gym plan is valid to 15/12/2026. On 20/11/2026 each also bought "Yoga 1 Tháng" the same way (cash, confirmed by staff), valid 20/11 to 19/12/2026. They exist only in this local database. On 16/11 Demo Hai was left checked in and Demo Một checked out; on 17/11 Demo Một was checked in again at the front desk (gym check-in test).

Demo Yoga (`yoga1@example.com`, phone `0900000021`) has only a yoga plan. It was registered on 16/11/2026 and bought "Yoga 1 Tháng" (600.000 ₫, 30 days) on the home page with cash; staff confirmed the cash on `/staff/membership-cash-receipt` (order ORD-20261116-72A8729070BF). The yoga plan is valid 16/11 to 15/12/2026, and the account has booked Hatha Morning on mat 1.

Demo Trung Bình (`yoga2@example.com`, phone `0900000022`) was added on 25/11/2026 to test yoga plan levels, through the app's own API (no SQL, no DbInitializer): registered, bought **Yoga Trung bình 1 Tháng** (plan 6, level Trung bình, 800.000 ₫, 30 days, created on `/manager/membership-plans`) with cash (order ORD-20261125-30BF9D34F6A1, confirmed by staff), activated 25/11, valid to 24/12/2026, and booked Vinyasa Dynamic on mat 1 from the timetable. On `/yoga/schedule` it sees **Đặt chỗ** on Cơ bản and Trung bình classes and the "Mua gói có độ khó cao hơn…" note with **Mua gói** on Ashtanga Flow (Nâng cao).

Data that already exists:
- The member has "Gym 1 Month" (order ORD-20261112-367A858627B3, paid in cash), valid 12/11 to 11/12/2026, and "Yoga 1 Tháng" (order ORD-20261120-D5FCE39ABCD6, paid in cash, confirmed by staff), valid 20/11 to 19/12/2026.
- The member's face is registered.
- Yoga: Studio A (floor 2, 20 mats) and Studio B (floor 1, 20 mats); class types Hatha, Vinyasa, Ashtanga and Yin; one instructor, Test Yoga Instructor. Classes (all published, sessions to 31/12/2026). The "registration closes" time is gym time:

  | Class | Type, level | When | Studio | Mats | First session | Registration closes |
  |---|---|---|---|---|---|---|
  | Hatha Morning | Hatha, Beginner | Thu 08:00–09:00 | A | 10 | 19/11 | 18/11 17:00 (closed) |
  | Zen Weekend | Yin, Beginner | Sat, Sun 08:00–09:30 | A | 20 | 28/11 | 27/11 22:00 |
  | Ashtanga Flow | Ashtanga, Advanced | Mon, Wed, Fri 06:30–07:45 | B | 12 | 30/11 | 29/11 22:00 |
  | Vinyasa Dynamic | Vinyasa, Intermediate | Tue, Thu 18:30–19:45 | A | 15 | 01/12 | 30/11 22:00 |
  | Yin Restore | Yin, Beginner | Mon, Wed 20:00–21:00 | B | 10 | 07/12 | 06/12 22:00 |

  The last four were added on 19/11/2026 through the Manager API (not SQL, not DbInitializer), so they exist only in this local database. A class must be published at least 7 days before its first session (GBR-77), and its registration deadline must be on or before its start date. That is why they start from 28/11. The yoga plan keeps selling until the last deadline, 06/12 22:00.
  - The member's active booking is on **mat 5** (booking 1, mat 3, was cancelled during testing).
  - Session 3 was added for 16/11 at 19:07 for an attendance test; its roster is submitted (member Có mặt), so it's locked. Add a new "now" session (section 5) to test attendance again.

## 3. Check-in and check-out

### Kiosk: `/kiosk`

Open to Staff, Manager, Customer and Member.
- **Staff and Manager** can scan anyone.
- **Customers and Members** can only scan themselves: a face or QR that belongs to someone else is refused.

**One scan decides the direction.** A member who is outside gets **CHECK-IN**; a member who is inside gets **CHECK-OUT**.

**Scanning is hands-free (23/11/2026).** There are no scan buttons.
1. Open `/kiosk` and allow camera access. The camera starts by itself; the face models take a few seconds to load the first time ("Đang mở camera và tải mô hình nhận diện…"). If the browser refuses the camera, press **Bật camera**.
2. While it shows "Đang chờ khuôn mặt hoặc mã QR…", stand in front of the camera **or** hold a QR code in the middle of the frame. It checks the camera a few times a second: QR first, then face.
3. After a successful scan it shows "Mời bạn rời khỏi khung hình để người tiếp theo quét." and waits until that person (same face) or that QR code has been out of view for about 1.5 seconds. **Step out of the frame before the next scan**; otherwise the same person would be checked straight back out.
4. After a refused scan it waits 3 seconds and tries again. A QR code it already sent is never sent twice.

Test it in Edge with a real camera: check in with your face, step away, come back: you are checked out. Try it with a QR from `/my-qr` on a phone too.

Things that look like errors but are the rules:
- **No wait between visits (23/11/2026):** the old 15-minute anti-passback rule is gone. A member can check in again right after checking out. Checking in while already inside is refused (409, "Hội viên này đang ở trong phòng tập. Hãy check-out trước.").
- **QR codes** work once, expire after **2 minutes**, and are **replaced each time `/my-qr` is opened**. To scan a photo of the code: open `/my-qr` once, take the photo, don't reload, and scan within 2 minutes.
- "Từ chối truy cập: không tìm thấy gói hội viên còn hiệu lực hoặc không xác thực được khuôn mặt/mã QR…" means one of these:
  - the face didn't match, or the QR was used, expired or replaced (code `SCAN_NOT_RECOGNISED`; for a QR the kiosk adds how to make a new code);
  - the person has no usable membership (code `ACCESS_DENIED`, and the message adds "Trạng thái gói: …", for example "Chưa có gói Gym").

### Member pages

- `/my-qr` (Customer, Member): the check-in QR image, its expiry time and a **Tạo mã mới** button.
- `/face-id` (Customer, Member): consent, camera capture, delete.
- The profile page's **TIỆN ÍCH HỘI VIÊN** box links to these pages, the kiosk, the yoga schedule and the workout plan.

### Front desk: `/staff/check-ins` (Staff, Manager)

1. **Check-in thủ công:** search by name, phone (`0900000004`) or order number, then press **Check-in**. The button is off for members without an active membership; a member already inside shows "Đang trong phòng tập".
2. **Đang có mặt:** who is inside, the occupancy, today's check-ins and the peak flag. **Check-out** on a row asks once, then records the staff member who did it.
   - The list **updates itself every 5 seconds**, including check-ins and check-outs made at the kiosk or another desk. The line under the title shows "Trực tiếp · cập nhật lúc HH:mm:ss", or "Mất kết nối, đang thử lại…" while the API is down. A new row is tinted.
   - A filter that matches no one shows "Không tìm thấy kết quả phù hợp." (MSG-01); the occupancy cards still count everyone.
3. A member can be checked in again right after being checked out; there is no waiting time.

**Live demo (two front desks):**
1. Edge window A (normal): sign in as `staff@test.vn`, open `/staff/check-ins`. Don't touch it from now on.
2. Edge window B (InPrivate): sign in as `manager@test.vn`, open `/staff/check-ins`.
3. In B, search `0900000011` and press **Check-in**. Within 5 seconds A shows Demo Hội Viên Một, and "Đang có mặt" goes up by 1.
4. In B, press **Check-out** on that row. Within 5 seconds the row leaves A.
5. Repeat with `0900000012` and `0900000013`, or check the same member in again right away: there is no waiting time any more.
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
| `/manager/yoga` | Manager | Studios (create, edit, deactivate), class types, classes (create, publish, cancel), sessions. Sessions are made by weekday: after **Lưu** on a new class its **Buổi học** panel opens; pick days (T2…CN), start time and room, then **Tạo lịch** makes one session per chosen day from the class start to end date (`POST /api/yoga/classes/{id}/sessions/weekly`). Days that already have a session, are past, or (published class) are within 7 days are skipped and counted. If any date clashes (room or trainer busy, room too small) nothing is saved and the message names the date. **Sửa** still changes one session's date |
| `/yoga/schedule` | anyone | A week timetable (days across, start times down; a day-by-day list on phones) with free mats, filters and next/previous week. A week with no classes still shows the empty grid. `?date=yyyy-MM-dd&classId=N` opens the week of that date, scrolls to class N and outlines it in red |
| `/pt/yoga` | YogaInstructor | This week's assigned sessions with booked members, and **Điểm danh** per session |
| `/yoga/my-bookings` | Member | Upcoming (with cancel), past (Có mặt / Vắng) and waitlist tabs |
| `/staff/yoga` | Staff | The day's sessions; **Điểm danh** opens the roster |

**Yoga plan levels (25/11/2026; rules waiting for the BR list in `docs/BR-pending-yoga-plan-levels.md`):**
- Every yoga plan has a level: **Cơ bản**, **Trung bình** or **Nâng cao**. A higher level includes the lower ones: Cơ bản books Cơ bản classes, Trung bình books Cơ bản and Trung bình, Nâng cao books every class.
- Manager, `/manager/membership-plans` → **Thêm gói mới** → **Loại dịch vụ: Yoga** shows a required **Cấp độ yoga** dropdown; its hint lists the classes the level books. Saving without a level is refused ("Hãy chọn cấp độ yoga…", `YOGA_LEVEL_REQUIRED`). The list shows **Yoga · Trung bình**.
- **Buyers keep the level they bought.** Once a yoga plan has an order or a membership, its level can't be changed (`YOGA_LEVEL_LOCKED`, "Gói đã có người mua nên không đổi được cấp độ…"); create a new plan for another level. Name, price, term and status can still be edited.
- **The local plan "Yoga 1 Tháng" has no level after the migration and has buyers**, so it shows **Yoga · Chưa có cấp độ · không bán**, stays hidden from every catalog and can't get a level. Its buyers (Demo Yoga and the demo members) keep booking every class until their membership ends, and can't buy or extend it again.
- A yoga plan sells only while a bookable class **at or below its level** exists. Example: if only Nâng cao classes are open, Cơ bản and Trung bình plans are hidden and refused at checkout (`PLAN_NOT_PURCHASABLE`).
- **Timetable `/yoga/schedule`, for everyone.** Each class you have not booked shows one of:
  - signed out, or signed in without a usable yoga plan (a Customer, or a Member with only a gym plan): **Mua gói và đăng ký**;
  - a yoga plan of a lower level: the amber note "Mua gói có độ khó cao hơn để được đăng ký." with an up-arrow icon, and an outline **Mua gói** button;
  - a plan that covers the class: **Đặt chỗ** / **Vào danh sách chờ**, as before.
  Both buy buttons open `/membership-plans?type=yoga&level=…&classId=…&date=…&className=…`: only the plans that book that class, under "Các gói dưới đây cho phép đăng ký lớp X (level)", with **Xem tất cả gói Yoga**. The timetable asks `GET /api/yoga/my-plan` (any signed-in user) for `{ hasPlan, maxLevel }`.
- Booking a class above your plan's level through the API is refused with 403 `YOGA_LEVEL_NOT_COVERED` ("Gói yoga của bạn chưa bao gồm lớp Nâng cao…"); no yoga plan at all is still `MEMBERSHIP_REQUIRED`. Joining the waitlist follows the same rule.
- **Coming back to the class.** The filtered plans page remembers the class (sessionStorage `vfms.yoga.pendingClass`). When an order with one of those plans is **Paid**, the checkout page shows "Kích hoạt gói trong Gói của tôi, rồi đăng ký lớp đã chọn." and **Đăng ký lớp X ngay**, which opens the timetable on that class. Booking is not automatic: activate the plan in `/my-memberships`, then pick a mat.
- **Plan details (two pages since the 25/11 merge of main):**
  - **Main's page `/membership-plans/{type}/{id}`** (for example `/membership-plans/yoga/6`) opens when you click a plan name on `/membership-plans` or on the home page cards. For a yoga plan it shows **Cấp độ Yoga** (it used to show "Dòng lớp Yoga"), plus reviews and suggested plans.
  - **This branch's page `/membership-plans/{id}`** (for example `/membership-plans/6`) is no longer linked; open it by typing the URL. It shows the price, term, description and, for yoga, the level, "Đăng ký được lớp: …" and the classes it can book in the next 4 weeks, each linking to the timetable. A plan that is not on sale shows "Gói này không còn mở bán hoặc không tồn tại."
- **Classes at the same time.** Two classes may share a time slot when both the studio and the trainer differ (same studio or same trainer is still `YOGA_SESSION_CONFLICT`). On the desktop timetable the cell shows an **N lớp cùng giờ** chip and lists them by studio; the phone list orders a day by time, then studio.
- The class-type link on yoga plans ("Dòng lớp") is gone: only the level decides which classes a plan books.

**Yoga plans (sell and buy):**
- Manager, `/manager/membership-plans` → **Thêm gói mới**: pick **Loại dịch vụ: Yoga**, then **Cấp độ yoga** (required). The type can't be changed after the plan is created. The list has a **Dịch vụ** column and a filter.
- Customer or Member, home page → **GÓI THẺ & LỊCH TẬP** → **YOGA STUDIO** tab: only yoga plans are listed (the GYM MEMBERSHIP tab lists only gym plans). **Thêm gói tập** → choose **Tiền mặt tại quầy** or VietQR → **Tạo đơn thanh toán**. Signed-out visitors see the same plans from the database (the catalog API `GET /api/membership-plans` is public), with **Tạo tài khoản** (to `/auth/register`) and a small **Đăng nhập để mua** link instead of **Thêm gói tập**; Staff, Manager and other roles see the plans with a note that only customers and members can buy. The old hard-coded sample cards are gone, but the cards keep their look and are filled from the plan:
  - the plan's **Mô tả** (description) gives the text: the first line is the short tagline under the name, and every further line is one ✓ benefit. A one-line description shows only the tagline.
  - "Tương đương X ₫ / tháng" is the price divided by the term in 30-day months (365 days = 12 months).
  - "TIẾT KIỆM N%" is worked out against the dearest per-month plan on the same tab (rounded down), so it is never typed by hand. There is no "PHỔ BIẾN NHẤT" badge any more, because no plan field says which one is popular.
  - Cards are sorted from the shortest term to the longest.
- **YOGA STUDIO tab: real yoga classes, then the yoga plans.** The tab first shows one card per real yoga class in the old class-card style, for everyone (signed in or out), from the public timetable `GET /api/yoga/schedule` for today plus 27 days. Under them, "Gói Yoga" lists the yoga plans to buy, because a class needs a yoga plan to book. Each class card shows:
  - the status pill on the "YOGA STUDIO" row: **HẾT HẠN** (grey) once the class's registration deadline has passed; **HẾT CHỖ** (red) when the class is Full or has no free mat, so new bookings go on the waitlist; the labels are kept short so the pill stays on one line beside "YOGA STUDIO" even on a tablet; **SẮP ĐẦY** (amber) when at most 20% of the mats are free; otherwise **CÒN CHỖ** (green).
  - the class type and instructor, the weekday chips (T2…CN) with the time for each time slot, the studio and floor, and the difficulty in Vietnamese (Cơ bản / Trung bình / Nâng cao).
  - free mats ("Còn 8 / 10 chỗ") and the registration deadline in gym time.
  - **Đăng ký lớp**: signed in it goes to `/yoga/schedule` on the week of the class's first session, with the class outlined, to pick a mat; signed out it goes to `/auth/register`. After the deadline there is no button, only "Đã hết hạn đăng ký".
  - There is no price on a class card: classes have no price of their own, you pay for the yoga plan. The old sample price lines ("1.450.000 ₫ / tháng (12 buổi)") were invented.
  - To support the deadline pill, the schedule response (`YogaSessionResponse`) now also carries `registrationDeadline` (UTC).
  - In the local database there are five classes (see section 2): Hatha Morning shows **HẾT HẠN** (its registration closed on 18/11), the other four show **CÒN CHỖ** until their registration closes between 27/11 and 06/12.
- **Gym plans in the local database (17/11/2026).** Three gym plans copied from the old sample cards were created through the Manager API: **3 Tháng** (2.400.000 ₫, 90 days), **6 Tháng** (4.200.000 ₫, 180 days) and **1 Năm (12 Tháng)** (7.200.000 ₫, 365 days). "Gym 1 Month" (500.000 ₫) was set to **INACTIVE**, so it is no longer sold; members who bought it keep their memberships. The old freeze-day lines ("Bảo lưu thẻ (Freeze): tối đa 15/30/60 ngày") were left out, because no freeze policy is set up in this database (FreezePolicies and FreezePolicyTierQuotas are empty). These plans are only in this local database, not in DbInitializer.
- **A yoga class needs an active yoga trainer.** A class's trainer counts as active only while the account status is **Active**, the account is not banned, and it still has the **YogaInstructor** role (`YogaClassRules.ActiveInstructor`). If the trainer fails any of these, the whole class is hidden. It disappears from the public timetable `/yoga/schedule` and the home page YOGA STUDIO cards. Booking it, or opening its mat map, returns "Yoga class not found." (404). Publishing it is refused with `YOGA_INSTRUCTOR_INACTIVE`. It also no longer counts as a bookable class, so on its own it does not keep yoga plans on sale. The Manager still sees it on `/manager/yoga` and can assign an active trainer. Members already booked keep their bookings. The trainer dropdown already lists only Active accounts, and now leaves out banned ones too. To test: there is no Admin page for this in the client yet. Signed in as Admin, call `PATCH /api/admin/users/{userId}/lock` in Swagger for "Test Yoga Instructor" (user id `c85e1592-122e-4809-bdc6-839aefb9dd9b`), or remove the role with `PUT /api/admin/users/{userId}/roles`. Every local class disappears from the Yoga tab and "Yoga 1 Tháng" stops selling. `PATCH /api/admin/users/{userId}/unlock` brings them back.
- Yoga plans are sold only while at least one yoga class **their level covers** can be booked: a published class (Open, or Full with a waitlist) whose registration deadline hasn't passed (`YogaClassRules.Bookable`, the same rule as booking). Otherwise the yoga tab says "Hiện chưa có gói Yoga mở bán.", the plan's catalog page is not found, and buying it is refused with `PLAN_NOT_PURCHASABLE`. The Manager still sees and edits the plan. Gym plans are not affected. To test: cancel every yoga class on `/manager/yoga`, or let their registration deadlines pass. In the local database the last registration deadline is Yin Restore's, **06/12/2026 22:00** (gym time), so "Yoga 1 Tháng" stops selling then: create and publish another class on `/manager/yoga` to keep selling yoga plans.
- Payment is the same as for gym plans. With cash, staff enter the order number and amount on `/staff/membership-cash-receipt`; VietQR is confirmed by the bank webhook. After payment a Customer becomes a Member, and `/my-memberships` shows the plan with **Dịch vụ: Yoga**.
- A yoga plan lets the member book the yoga classes its level covers but gives no gym access. The kiosk and the front desk only count gym plans (`MembershipRules.Gym` in `GymAccessServices.cs`): a yoga-only member is refused with "Membership status: No gym plan.", and the front-desk search shows **Chưa có gói Gym**. A member with gym and yoga checks in on the gym plan. Check-out is never blocked.
  - Try it: front desk `/staff/check-ins`, search `0900000021` (Demo Yoga) → Chưa có gói Gym, check-in refused; `0900000011` (Demo Một) → Active.
  - A yoga-only member still sees and can buy gym plans (home → GYM MEMBERSHIP tab). Other features that need a gym plan refuse them with a message saying so: PT packages ("Cần gói Gym đang hoạt động…") and locker rental ("Hội viên cần có gói Gym đang hiệu lực.").
  - Still open to Customers and to yoga-only members, because they need no plan: `/workout` (AI workout plan), `/face-id`, and `/my-qr` (yoga attendance scans it). Equipment-issue reports stay open to every Member.
- API: `POST /api/manager/membership-plans` takes `"serviceType": "YOGA"` (default `GYM`) and, for yoga, `"yogaDifficultyLevel": "Beginner" | "Intermediate" | "Advanced"`; `GET /api/membership-plans?serviceType=YOGA` and the manager list filter by it.

**Book (Member, on `/yoga/schedule`):**
- Each session shows **Đặt chỗ**, or **Vào danh sách chờ (miễn phí)** when the class is full, when your yoga plan's level covers the class (see "Yoga plan levels" above for the other cases).
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

## 6. Dashboards and home page: real data only

The demo lists that were written into the pages are gone. Each figure loads on its own; if one module fails, its card shows "—" and a short error, the others still show.

| Page | Shows | From |
|------|-------|------|
| Home, **CÁC LỚP HỌC NỔI BẬT** | Up to 4 real yoga classes of the next 4 weeks (open ones first), with the status pill, type, minutes, sessions per week and difficulty; each card opens `/yoga/schedule` on the week of that class's first session, with the class outlined. Classes have no photo field yet, so the 4 old photos are reused in turn. | `GET /api/yoga/schedule` |
| `/staff/dashboard` | People inside now and today's check-ins; equipment issues waiting (Reported); open consultations; the 8 latest check-ins not checked out (an older one shows its date). Quick actions open the real pages. | `api/check-ins/active`, `api/equipment-issues?status=Reported`, `api/consultations?status=Open` |
| `/manager/dashboard` | Revenue and active memberships from the **newest generated business report** (none yet: "—" and a hint to make one); waiting freeze reviews plus submitted purchase proposals; the 5 newest trainer feedback items waiting for review. | `api/business-reports`, `api/manager/membership-freeze-requests`, `api/purchase-proposals?status=Submitted`, `api/feedback/manager?status=Pending` |
| `/admin/dashboard` | Staff accounts, locked accounts, audit entries since midnight; the 8 latest audit entries. | `api/admin/users`, `api/admin/audit-logs` |

The dashboards (and `/staff/membership-cash-receipt`, `/manager/competitions`) no longer show the "BẢN XEM TRƯỚC" banner.

Try it: as Staff, check a member in on `/staff/check-ins`, then open `/staff/dashboard`: the count and the table include them. As Manager, generate a report for a past day on `/manager/business-reports`, then open `/manager/dashboard`.

## 7. Trainer roles (database)

The `TrainerRoles` table from the updated ERD (*VFMS Erd v2 - TrainerRole* on Drive) is in main since PR #15. The migration `AddTrainerRoles` runs by itself the next time the API starts, so there is nothing to run by hand. No screen changes, and the login roles are still `PT` and `YogaInstructor`.

Check it in SQL after starting the API once:
```sql
-- Two rows: PT -> PT, Yoga -> YogaInstructor
SELECT t.Id, t.RoleName, r.RoleName AS LoginRole, t.IsActive
FROM TrainerRoles t JOIN ApplicationRoles r ON r.Id = t.RoleId;

-- Profiles that existed before the migration got a role: Yoga if the user is only a yoga instructor, otherwise PT
SELECT u.Email, tr.RoleName
FROM TrainerProfiles p
JOIN ApplicationUsers u ON u.UserId = p.PTUserId
LEFT JOIN TrainerRoles tr ON tr.Id = p.TrainerRoleId;
```
Every trainer application that existed before the migration is set to PT.

## 8. Language and text limits

**Yoga messages (23/11/2026).** On the yoga pages (Yoga Management, timetable, my yoga bookings, sessions, attendance, assigned classes, the class cards), success and error messages have no box or background: a green tick icon with green text, or a red warning icon with red text. To see one, book a class on `/yoga/schedule`, or stop the API and reload the timetable. Teammates' pages keep the shared `Alerts` and toast styles. Clicking a class name in **Lịch yoga của tôi** opens the timetable on the week of its next session, with the class outlined.

**Vietnamese (23/11/2026).** Every message the API sends to the check-in, kiosk, FaceID, yoga and workout plan screens is in Vietnamese, and so are the validation messages of those forms. Error codes (`ALREADY_CHECKED_IN`, `MAT_TAKEN`, …) are unchanged. Teammates' modules are not translated.

Try it: book a mat someone else just took, cancel a yoga booking less than 2 hours before class, or save a yoga class with an end date before the start date. Each message is in Vietnamese.

**Longest text each field accepts** (the API refuses longer text with "Tối đa N ký tự."):

| Field | Max characters |
|-------|----------------|
| Yoga class description | 1000 |
| Yoga class type description | 500 |
| Yoga class name | 256 |
| Yoga studio name | 200 |
| Yoga class type name | 100 |
| Yoga class image URL | 500 |
| Workout survey: health conditions | 500 |
| Workout plan: exercise note | 500 |

## 9. Known gaps

- **One integration test from dev/viehai fails on SQLite.** `MembershipPurchaseApiTests.InvoicePdfPreservesMaximumPersistedDecimalPrecisionWithLongHistoricalItemName` comes from dev/viehai (commit 0642777, invoice PDF export) and tests that feature at the largest `decimal(18,2)` amount. The test's own SQLite database can't hold that many digits (`9999999999999999.99` comes back as `10000000000000000`), so it fails on main too. It isn't caused by this branch. To run the rest without it:
  ```powershell
  dotnet test tests/VFMS.IntegrationTests --filter "FullyQualifiedName!~InvoicePdfPreservesMaximumPersistedDecimalPrecision"
  ```

- **Three teammates' integration tests still fail (25/11/2026, after merging main into this branch).** Main's latest changes fixed most of the phone-number failures from 23/11. These three are left to their owners:
  - `ConsultationPersistenceTests`: a duplicate phone number (`UNIQUE constraint failed: ApplicationUsers.PhoneNumber`).
  - `InvoicePdfPreservesMaximumPersistedDecimalPrecision…`: SQLite decimal precision, see above.
  - `TransactionHistoryRetainsPaidPurchases…`: a duplicate user-role row.

  Expected result now:
  - Unit: 701 pass.
  - Client: 511 pass.
  - Integration: 611 pass, 3 fail, 47 skipped.
- **PR #21 to main is open (25/11/2026):** https://github.com/viehai/SEP490_G144_VFMS/pull/21. Once it is merged, teammates who pull main get the migration `YogaPlanLevelReplacesClassType`. It drops `PackageComponents.YogaClassTypeId` and `YogaWaitlists.YogaClassTypeId`, so code or tests that still use `YogaClassType` on a plan no longer build, and yoga plans in test seeds need `YogaDifficultyLevel`.

- **Yoga plans can't be renewed yet.** Renewal (`/my-memberships` → gia hạn) still only covers gym plans, so a yoga member buys a new yoga plan instead; it starts after the current one ends.
- **Old SQL yoga parts removed (20/11/2026).** Before yoga plans existed, a YOGA part was added by SQL to the gym package "Gym 1 Month", so its buyers (the member and the three demo members) also got a yoga membership. That part and those 4 yoga memberships are now soft-deleted (`IsDeleted = 1`, by SQL, because no API removes a membership); nothing used them. The 4 accounts bought "Yoga 1 Tháng" through the real flow instead, so `/my-memberships` shows one Gym card and one Yoga card.
- **The yoga instructor account was made by SQL.** `yoga@test.vn` (Test Yoga Instructor) was created by SQL before any screen could make one, and every local class and session points to it, so it stays. Hiring a yoga trainer through recruitment still gives the PT role, not YogaInstructor (left as is: changing it touches the recruitment module and database). Without SQL, an Admin can make one with `POST /api/admin/users/staff` (role `YogaInstructor`) or add the role with `PUT /api/admin/users/{userId}/roles`; there is no Admin user page in the client for this yet.
- **Trainer roles are only in the database so far.** Profiles and applications created after the migration get no trainer role (`TrainerRoleId` is NULL) until `TrainerService` sets it, and merging `PT` and `YogaInstructor` into one `Trainer` login role is not done. Both belong to the trainer module owner.
- **Still showing written-in demo content (not changed here):** the PT dashboard `/pt/dashboard` (PT module, another team member), the home page equipment showcase (static), and the template pages Weather and Counter with their menu links.
- **Business reports made while testing.** On 20/11/2026 two business reports were generated on the local database to check the Manager dashboard: Monthly 2026-10 (empty) and Daily 2026-11-16. The dashboard shows the newest one.
- **Self check-in from anywhere.** Because the kiosk is open to Customers and Members, one can check in from anywhere with `/my-qr` plus `/kiosk` on one phone. That check-in can also mark a PT session as done.
- **Phones can't open localhost.** To test QR with a phone, photograph the code shown on the laptop.
