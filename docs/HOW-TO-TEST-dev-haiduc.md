# How to test dev/haiduc: check-in, yoga, workout plans

Branch `dev/haiduc`. These features have screens; the rest is API only (test it in Swagger):
- **Screens:** kiosk check-in/out (Face ID and QR), My QR, Face ID registration, who is inside, yoga setup and schedules, workout plan.
- **API only:** front-desk manual check-in/out, yoga booking, cancellation, my bookings and attendance.

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

### Staff pages

`/staff/check-ins` (Staff, Manager) shows who is inside, the occupancy and the peak flag. You can search by name, phone or order number.

### API only (Swagger, as Staff)

- **Search the front desk:** `GET /api/check-ins/members?search=` with a name, a phone (`0900000004`) or an order number.
- **Manual check-in:** `POST /api/check-ins/manual` with `{ "memberUserId": "b7a2e8b5-..." }`. The 15-minute anti-passback rule applies here too.
- **Manual check-out:** `POST /api/check-ins/{id}/check-out`, where `id` comes from `GET /api/check-ins/active`. The staff member who does it is recorded.
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
| `/pt/yoga` | YogaInstructor | This week's assigned sessions with booked members |

**API only (Swagger):**
- **Book (Member):**
  - `GET /api/yoga/bookings/classes/1/mats` lists the mats.
  - `POST /api/yoga/bookings` with `{ "yogaClassId": 1, "matNumber": 5 }`.
  - A clash in the same time slot gives 409 ALREADY_BOOKED.
  - A full class puts the member on the waitlist. To test it, use a class with maxParticipants 1 and a second member.
- **My bookings:** `GET /api/yoga/bookings/mine` shows upcoming, past (attended or no-show) and the waitlist.
- **Cancel:** `POST /api/yoga/bookings/{id}/cancel` with `{}`.
  - Under 2 hours before the session it returns 409 LATE_CANCELLATION_CONFIRM.
  - Send `{ "confirmLateCancellation": true }` to cancel anyway. It counts as a no-show, and 3 no-shows suspend booking.
- **Attendance (YogaInstructor or Staff):**
  - `GET /api/yoga/sessions/{id}/attendance` shows the roster.
  - `POST .../attendance/qr` with `{ "qrToken": "<member my-qr token>" }` marks one member.
  - `POST .../attendance` with `{ "marks": [ { "bookingId": 1, "attended": true } ] }` submits. Every booking must be marked.
  - Writes open 30 minutes before the start, and a second submit gives 409 ATTENDANCE_CLOSED.
  - A submit more than 30 minutes after the start is flagged late; the Manager sees it in `GET /api/yoga/classes/{id}/sessions`.

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
