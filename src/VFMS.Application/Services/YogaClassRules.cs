using System.Linq.Expressions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>When a yoga class can still be booked or waitlisted (YogaBookingService, GBR-78). Yoga plans are only sold while one exists.</summary>
public static class YogaClassRules
{
    public static Expression<Func<YogaClass, bool>> Bookable(DateTime nowUtc) => yogaClass =>
        !yogaClass.IsTemplate &&
        (yogaClass.ClassStatus == YogaClassStatus.Open || yogaClass.ClassStatus == YogaClassStatus.Full) &&
        nowUtc < yogaClass.RegistrationDeadline;
}
