using System.Linq.Expressions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>When a yoga class can still be booked or waitlisted (YogaBookingService, GBR-78). Yoga plans are only sold while one exists.</summary>
public static class YogaClassRules
{
    /// <summary>
    /// A yoga trainer who can teach: account Active, not banned, still holding the YogaInstructor role.
    /// A class whose trainer fails this is hidden from the timetable and cannot be booked or published.
    /// </summary>
    public static readonly Expression<Func<ApplicationUser, bool>> ActiveInstructor = user =>
        user.AccountStatus == AccountStatus.Active && !user.IsBanned &&
        user.UserRoles.Any(userRole => userRole.Role.RoleName == YogaClassService.InstructorRole);

    public static Expression<Func<YogaClass, bool>> Bookable(DateTime nowUtc) => yogaClass =>
        !yogaClass.IsTemplate &&
        (yogaClass.ClassStatus == YogaClassStatus.Open || yogaClass.ClassStatus == YogaClassStatus.Full) &&
        nowUtc < yogaClass.RegistrationDeadline &&
        yogaClass.Instructor.AccountStatus == AccountStatus.Active && !yogaClass.Instructor.IsBanned &&   // same as ActiveInstructor
        yogaClass.Instructor.UserRoles.Any(userRole => userRole.Role.RoleName == YogaClassService.InstructorRole);
}
