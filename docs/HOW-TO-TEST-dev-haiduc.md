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

Member user id: `b7a2e8b5-23e2-4415-8679-357bb5c33ad3`. Use an InPrivate window to be signed in as two roles at once.

Data that already exists:
- The member has "Gym 1 Month" (order ORD-20261112-367A858627B3, paid in cash), valid 12/11 to 11/12/2026. The member also has a YOGA membership, added by SQL (see section 6).
- The member's face is registered.
- Yoga: Studio A and Studio B, and class "Hatha Morning" (Open) with sessions from 19/11 at 08:00. Booking 1 is the member on mat 3.

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
3. A second check-in within 15 minutes of the last one is refused (anti-passback).

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
| `/yoga/schedule` | anyone | The week's published sessions with free mats, instructor filter, next/previous week ("Hatha Morning" is from 19/11) |
| `/pt/yoga` | YogaInstructor | This week's assigned sessions with booked members, and **Điểm danh** per session |
| `/yoga/my-bookings` | Member | Upcoming (with cancel), past (Có mặt / Vắng) and waitlist tabs |
| `/staff/yoga` | Staff | The day's sessions; **Điểm danh** opens the roster |

**Book (Member, on `/yoga/schedule`):**
- Each session shows **Đặt chỗ**, or **Vào danh sách chờ (miễn phí)** when the class is full.
- A booking is **per class**: one booking holds the mat for every session of that class. Pick a mat or leave it to the system.
- A clash in the same time slot is refused ("trùng khung giờ"). To test the waitlist, use a class with maxParticipants 1 and a second member.

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

- **No yoga membership through the API.** Plans made through `POST /api/manager/membership-plans` only get a GYM component, so a real member can't book yoga. To give a member yoga for testing:
  ```sql
  DECLARE @pkg int = (SELECT PackageId FROM PackageComponents WHERE Id = (SELECT PackageComponentId FROM MemberMemberships WHERE Id = <gymMembershipId>));
  INSERT PackageComponents (PackageId, ComponentType, DurationDays, AllowFreeze, CreatedAt, UpdatedAt, IsDeleted) VALUES (@pkg, 'YOGA', 30, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0);
  INSERT MemberMemberships (OrderItemId, PackageComponentId, MemberUserId, MembershipStatus, StartDate, EndDate, TotalFrozenDays, FreezeCount, CreatedAt, UpdatedAt, IsDeleted)
  SELECT OrderItemId, SCOPE_IDENTITY(), MemberUserId, MembershipStatus, StartDate, EndDate, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0 FROM MemberMemberships WHERE Id = <gymMembershipId>;
  ```
- **Self check-in from anywhere.** Because the kiosk is open to Customers and Members, one can check in from anywhere with `/my-qr` plus `/kiosk` on one phone. That check-in can also mark a PT session as done.
- **Phones can't open localhost.** To test QR with a phone, photograph the code shown on the laptop.
