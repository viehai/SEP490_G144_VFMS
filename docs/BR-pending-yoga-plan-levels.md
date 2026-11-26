# Pending business rules: yoga plans by difficulty level

> **PENDING: add to the BR list / SRS when approved.** These rules are built on branch `dev/haiduc`
> (commits 7968328 onward). Numbers are placeholders (`GBR-Y1`…); give them the next free GBR numbers
> (the highest used in code today is GBR-102) when they move into the SRS, then delete this file.

## Rules

| ID | Rule | Where it is enforced |
|---|---|---|
| GBR-Y1 | Every yoga plan has a difficulty level: Cơ bản (Beginner), Trung bình (Intermediate) or Nâng cao (Advanced). The Manager must choose it when creating or editing a yoga plan. | `MembershipPlanRequestValidator`, `MembershipPlanService.CreateAsync/UpdateAsync` (`YOGA_LEVEL_REQUIRED`), Manager plan form |
| GBR-Y2 | A higher level includes the lower ones: a Cơ bản plan books Cơ bản classes; a Trung bình plan books Cơ bản and Trung bình classes; a Nâng cao plan books every class. | `YogaRules.PlanCovers` |
| GBR-Y3 | A member can book or join the waitlist of a class only with a usable yoga plan whose level covers the class level. Without any usable yoga plan the error is `MEMBERSHIP_REQUIRED` (MSG-31); with a plan of a lower level it is `YOGA_LEVEL_NOT_COVERED`. | `YogaBookingService.BookAsync` |
| GBR-Y4 | Memberships bought before levels existed (no level on the plan) keep booking every class until they expire. That plan cannot be bought or extended again (it is not for sale, GBR-Y5). | `YogaRules.PlanCovers` (null level) |
| GBR-Y5 | A yoga plan without a level is not for sale: it is hidden from the catalogs and refused at checkout until the Manager sets a level. The Manager list shows it as "Chưa có cấp độ · không bán". | `MembershipPlanRepository.Plans`, `SearchByComponentTypeAsync`, `MembershipPurchaseService.CreateAsync` |
| GBR-Y6 | A yoga plan is on sale only while at least one bookable class it covers exists (replaces "while any yoga class is bookable"). | `MembershipPlanRepository.Plans`, `MembershipPurchaseRepository.AnyBookableYogaClassAsync(now, maxLevel)` |
| GBR-Y7 | Anyone can view the yoga timetable. A visitor who is signed out or has no usable yoga plan sees "Mua gói và đăng ký"; a member whose plan does not cover the class sees "Mua gói có độ khó cao hơn để được đăng ký." and "Mua gói". Both open the plans page filtered to plans that cover that class. | `YogaPlanAccess.For`, `YogaSchedule.razor`, `GET api/yoga/my-plan` |
| GBR-Y8 | Buying from the timetable does not book automatically. After the order is paid (and the plan activated) the checkout page links back to the chosen class, where the member picks a mat and confirms. | `PendingYogaClass`, `MembershipCheckout.razor` |
| GBR-Y9 | Several yoga classes may run at the same time as long as each has a different studio and a different instructor (restates GBR-74/75: only a studio or instructor clash is a conflict). | `YogaClassService.FindConflict` |
| GBR-Y11 | Buyers keep the level they bought: once a yoga plan has an order or a membership, its level cannot be changed (`YOGA_LEVEL_LOCKED`). The Manager creates a new plan for another level. An old no-level plan that was bought therefore stays unsold; one that was never bought must get a level when it is edited. | `MembershipPlanService.UpdateAsync` |
| GBR-Y10 | A yoga plan no longer limits bookings to one class type ("Dòng lớp"); only the level decides which classes it books. | removed `PackageComponent.YogaClassTypeId` |
| GBR-Y12 | When the Manager creates a class's sessions by weekday, the number of weekdays picked must equal the class's sessions per week (`YOGA_SESSION_DAYS_COUNT`). The picker disables the other days once that many are picked. | `YogaClassService.CreateWeeklySessionsAsync`, `YogaManagement.razor` |
| GBR-Y13 | When a member's yoga plan ends before a class they booked ends, the booking is **held**: it stays Confirmed and keeps its mat, but on any session date without a usable yoga plan that covers the class level the member can't be marked present (QR scan or manual, `YOGA_PLAN_ON_HOLD`), is not counted absent, a late cancel is not a no-show, and they are skipped for waitlist seat offers. Buying a new covering plan lifts the hold at once; nothing is stored, the hold is worked out from the member's plans. | `MembershipRules.CoversYogaClass`, `YogaAttendanceService` (roster `OnHold`, scan, submit), `YogaBookingService` (late cancel, waitlist offer, `GetMyBookingsAsync`) |
| GBR-Y14 | Yoga plans are never extended: an expired yoga plan shows **Mua gói mới** on `/my-memberships`, and a held booking shows it under **Sắp tới**. Both open `/membership-plans?type=yoga&level={level}`, which lists only plans that book that level (the expired plan's level, or the held class's level). | `MyMemberships.razor`, `MyYogaBookings.razor` (`BuyNewHref`), `MembershipPlans.razor` (`ForLevel`) |
| GBR-C1 | A member's check-in QR code works all gym day, for check-in, check-out and yoga attendance, until 24:00 gym time. Opening "Mã QR của tôi" again shows the same code; "Tạo mã mới" replaces it and the old one stops working. (Replaces "single use, 2 minutes".) | `MemberQrService`, `GET/POST api/my-qr` |

## New error codes

| Code | HTTP | Message |
|---|---|---|
| `YOGA_PLAN_ON_HOLD` | 403 | Gói yoga của {hội viên} đã hết hạn nên chỗ đang được tạm giữ. Hội viên cần mua gói mới để tham gia buổi học. |
| `YOGA_SESSION_DAYS_COUNT` | 400 | Lớp này có N buổi/tuần: hãy chọn đúng N ngày trong tuần. |
| `YOGA_LEVEL_NOT_COVERED` | 403 | Gói yoga của bạn chưa bao gồm lớp {cấp độ}. Hãy mua gói có độ khó cao hơn để được đăng ký. |
| `YOGA_LEVEL_REQUIRED` | 400 | Hãy chọn cấp độ yoga: Cơ bản, Trung bình hoặc Nâng cao. |
| `YOGA_LEVEL_LOCKED` | 409 | Gói đã có người mua nên không đổi được cấp độ. Hãy tạo gói mới với cấp độ khác. |

## Data model (ERD) changes

- `PackageComponent`: added `YogaDifficultyLevel nvarchar(20) NULL`; removed `YogaClassTypeId` and its link to `YogaClassType`.
- `YogaWaitlist`: removed `YogaClassTypeId` (the class type is reached through the session's class).
- Migration `YogaPlanLevelReplacesClassType`. The team ERD in Google Drive was updated the same way.

## New endpoint and page

- `GET api/yoga/my-plan` (any signed-in user): `{ hasPlan, maxLevel }`.
- `/membership-plans/{id}`: plan details, with the classes a yoga plan can book in the next 4 weeks.

## Decided rule: a booked class when the yoga plan expires (TC-YEXP, GBR-Y13)

**Decided 26/11/2026: option B, hold until a new plan is bought** (options A "keep until the class ends" and C "remove
from the class" were rejected). Tested by `YogaPersistenceTests.ExpiredYogaPlanHoldsTheBookingUntilANewPlanIsBought`.

**Setup for every case:** member M has a usable yoga plan, books class C (sessions run past the plan's end date), then
the plan reaches its end date (status Expired). C still has sessions after that date.

| ID | Steps | Expected |
|---|---|---|
| TC-YEXP-01 | M opens `/yoga/my-bookings` → **Sắp tới** | Booking still Confirmed with its mat, badge **Tạm giữ**, note "Gói yoga của bạn đã hết hạn nên chỗ đang được tạm giữ…" and a **Mua gói mới** button (plans of the class level). It is never the highlighted "Buổi yoga tiếp theo". |
| TC-YEXP-02 | Instructor opens the roster of C's next session; M shows their QR | M is listed with "Tạm giữ chỗ · gói yoga đã hết hạn" instead of the Có mặt / Vắng buttons. The scan is refused with `YOGA_PLAN_ON_HOLD`; a submitted "Có mặt" for M is refused too |
| TC-YEXP-03 | M tries to book another class (or C again after cancelling) | Refused, `MEMBERSHIP_REQUIRED` |
| TC-YEXP-04 | M was on C's waitlist; a seat frees after M's plan ended | M is skipped; the next waiting member with a covering plan is offered the seat |
| TC-YEXP-05 | M buys a covering plan, then the next session comes | The hold is lifted at once: Sắp tới shows the normal card and countdown, the scan marks Có mặt. A plan of a lower level than the class does not lift it |
| TC-YEXP-06 | No-show counting for sessions while held | The roster submits without a mark for M and writes no row, so M is not counted absent; a late cancel while held is not a no-show |
| TC-YEXP-07 | M opens `/my-memberships` | The expired yoga plan shows **Mua gói mới** (no **Gia hạn gói**), which opens the yoga plans filtered to its level |

**Still open:** how long a hold may last. Today a held booking keeps its mat until the class ends, so a class can look
full because of held members who never come back. If that becomes a problem, add a deadline (e.g. release the mat after
N days on hold).

## Known limits

- The plans page `/membership-plans` lists from `api/packages`, which hides yoga plans without a level but does not check that a class the plan covers can be booked (it never checked bookable classes, even before levels). Such a plan shows there but is refused at checkout with `PLAN_NOT_PURCHASABLE`. The home page catalog and the details page do apply the full rule.
- The "N lớp cùng giờ" chip appears only for classes with the same start time; classes that overlap but start at different times sit in different rows.
