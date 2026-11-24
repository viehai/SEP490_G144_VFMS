# Pending business rules: yoga plans by difficulty level

> **PENDING: add to the BR list / SRS when approved.** These rules are built on branch `dev/haiduc`
> (commits 7968328 to 97292c9). Numbers are placeholders (`GBR-Y1`…); give them the next free GBR numbers
> (the highest used in code today is GBR-102) when they move into the SRS, then delete this file.

## Rules

| ID | Rule | Where it is enforced |
|---|---|---|
| GBR-Y1 | Every yoga plan has a difficulty level: Cơ bản (Beginner), Trung bình (Intermediate) or Nâng cao (Advanced). The Manager must choose it when creating or editing a yoga plan. | `MembershipPlanRequestValidator`, `MembershipPlanService.CreateAsync/UpdateAsync` (`YOGA_LEVEL_REQUIRED`), Manager plan form |
| GBR-Y2 | A higher level includes the lower ones: a Cơ bản plan books Cơ bản classes; a Trung bình plan books Cơ bản and Trung bình classes; a Nâng cao plan books every class. | `YogaRules.PlanCovers` |
| GBR-Y3 | A member can book or join the waitlist of a class only with a usable yoga plan whose level covers the class level. Without any usable yoga plan the error is `MEMBERSHIP_REQUIRED` (MSG-31); with a plan of a lower level it is `YOGA_LEVEL_NOT_COVERED`. | `YogaBookingService.BookAsync` |
| GBR-Y4 | Memberships bought before levels existed (no level on the plan) keep booking every class until they end. | `YogaRules.PlanCovers` (null level) |
| GBR-Y5 | A yoga plan without a level is not for sale: it is hidden from the catalogs and refused at checkout until the Manager sets a level. The Manager list shows it as "Chưa có cấp độ · không bán". | `MembershipPlanRepository.Plans`, `SearchByComponentTypeAsync`, `MembershipPurchaseService.CreateAsync` |
| GBR-Y6 | A yoga plan is on sale only while at least one bookable class it covers exists (replaces "while any yoga class is bookable"). | `MembershipPlanRepository.Plans`, `MembershipPurchaseRepository.AnyBookableYogaClassAsync(now, maxLevel)` |
| GBR-Y7 | Anyone can view the yoga timetable. A visitor who is signed out or has no usable yoga plan sees "Mua gói và đăng ký"; a member whose plan does not cover the class sees "Mua gói có độ khó cao hơn để được đăng ký." and "Mua gói". Both open the plans page filtered to plans that cover that class. | `YogaPlanAccess.For`, `YogaSchedule.razor`, `GET api/yoga/my-plan` |
| GBR-Y8 | Buying from the timetable does not book automatically. After the order is paid (and the plan activated) the checkout page links back to the chosen class, where the member picks a mat and confirms. | `PendingYogaClass`, `MembershipCheckout.razor` |
| GBR-Y9 | Several yoga classes may run at the same time as long as each has a different studio and a different instructor (restates GBR-74/75: only a studio or instructor clash is a conflict). | `YogaClassService.FindConflict` |
| GBR-Y10 | A yoga plan no longer limits bookings to one class type ("Dòng lớp"); only the level decides which classes it books. | removed `PackageComponent.YogaClassTypeId` |

## New error codes

| Code | HTTP | Message |
|---|---|---|
| `YOGA_LEVEL_NOT_COVERED` | 403 | Gói yoga của bạn chưa bao gồm lớp {cấp độ}. Hãy mua gói có độ khó cao hơn để được đăng ký. |
| `YOGA_LEVEL_REQUIRED` | 400 | Hãy chọn cấp độ yoga: Cơ bản, Trung bình hoặc Nâng cao. |

## Data model (ERD) changes

- `PackageComponent`: added `YogaDifficultyLevel nvarchar(20) NULL`; removed `YogaClassTypeId` and its link to `YogaClassType`.
- `YogaWaitlist`: removed `YogaClassTypeId` (the class type is reached through the session's class).
- Migration `YogaPlanLevelReplacesClassType`. The team ERD in Google Drive was updated the same way.

## New endpoint and page

- `GET api/yoga/my-plan` (any signed-in user): `{ hasPlan, maxLevel }`.
- `/membership-plans/{id}`: plan details, with the classes a yoga plan can book in the next 4 weeks.
