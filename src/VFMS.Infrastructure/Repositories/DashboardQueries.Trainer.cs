using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Dashboards;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class DashboardQueries
{
    private IQueryable<YogaClassSchedule> YogaOn(DateOnly date) => db.YogaClassSchedules.Where(s => s.ScheduledDate == date &&
        s.ScheduleStatus != YogaScheduleStatus.Cancelled && s.ScheduleStatus != YogaScheduleStatus.Rescheduled &&
        s.YogaClass.ClassStatus != YogaClassStatus.Draft && s.YogaClass.ClassStatus != YogaClassStatus.Cancelled && !s.YogaClass.IsTemplate);

    private static IQueryable<TrainingSlot> YogaSlots(IQueryable<YogaClassSchedule> rows) =>
        rows.OrderBy(s => s.StartTime).ThenBy(s => s.Id).Select(s => new TrainingSlot(s.StartTime, s.EndTime,
            "Yoga", s.YogaClass.ClassName, s.YogaStudio.StudioName, s.ScheduleStatus.ToString()));

    public async Task<TrainerOverview> TrainerAsync(string user, bool yogaInstructor, DateTime now, CancellationToken ct)
    {
        var today = GymDay(now);
        var time = TimeOnly.FromDateTime(now.AddHours(7));
        var contracts = db.PTPurchases.Where(p => p.PTUserId == user && (p.OrderId == null || p.Order!.OrderStatus == OrderStatus.Paid))
            .Where(PersonalTrainingRules.StillRunning(today));
        var assigned = await contracts.OrderBy(p => p.EndDate).ThenBy(p => p.Id).Take(5)
            .Select(p => new { p.Id, p.Member.FullName, p.PackageNameSnapshot, p.TotalSessions,
                Sessions = p.Sessions.Select(s => new PTSession { SessionStatus = s.SessionStatus,
                    CreditUnits = s.CreditUnits, IsCreditForfeited = s.IsCreditForfeited }).ToList() }).ToListAsync(ct);
        var progress = assigned.Select(p => new MemberProgress(p.Id, p.FullName, p.PackageNameSnapshot ?? "Gói PT",
            p.TotalSessions, Math.Max(0, p.TotalSessions - PersonalTrainingRules.Credits(p.Sessions).Used))).ToArray();
        var sessions = TeachingSessions.Where(s => s.PTUserId == user);
        var pt = await sessions.Where(s => s.SessionDate == today).OrderBy(s => s.StartTime).ThenBy(s => s.Id)
            .Select(s => new TrainingSlot(s.StartTime, s.EndTime, "PT", "Huấn luyện cá nhân", s.Member.FullName, s.SessionStatus.ToString())).ToListAsync(ct);
        var yoga = yogaInstructor ? await YogaSlots(YogaOn(today)
            .Where(s => (s.InstructorUserId ?? s.YogaClass.InstructorUserId) == user)).ToListAsync(ct) : [];
        var upcoming = await sessions.CountAsync(s => (s.SessionDate > today || s.SessionDate == today && s.StartTime > time) &&
            (s.SessionStatus == PTSessionStatus.Scheduled || s.SessionStatus == PTSessionStatus.ReschedulePending ||
             s.SessionStatus == PTSessionStatus.RescheduleRequested), ct);
        return new(today, await contracts.Select(p => p.MemberUserId).Distinct().CountAsync(ct), pt.Count, yoga.Count,
            upcoming, pt.Concat(yoga).OrderBy(s => s.Start).ThenBy(s => s.Name).ToArray(), progress);
    }
}
