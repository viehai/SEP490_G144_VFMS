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
            .Include(p => p.Member).Include(p => p.Sessions).Include(p => p.Order!).ThenInclude(o => o.Items)
            .AsNoTracking().AsSplitQuery().ToListAsync(ct);
        var progress = assigned.Select(p =>
        {
            var total = p.TotalSessions;
            return new MemberProgress(p.Id, p.Member.FullName, p.PackageNameSnapshot,
                total, Math.Max(0, total - PersonalTrainingRules.Credits(p.Sessions).Used));
        }).ToArray();
        var sessions = TeachingSessions.Where(s => s.PTUserId == user);
        var todaySessions = await sessions.Where(s => s.SessionDate == today).OrderBy(s => s.StartTime).ThenBy(s => s.Id)
            .Select(s => new { s.StartTime, s.EndTime, s.PTPurchase.Member.FullName, s.SessionStatus }).ToListAsync(ct);
        var pt = todaySessions.Select(s => new TrainingSlot(s.StartTime, s.EndTime, "PT", "Huấn luyện cá nhân",
            s.FullName, PersonalTrainingRules.PublicSessionStatus(s.SessionStatus).ToString())).ToArray();
        var yoga = yogaInstructor ? await YogaSlots(YogaOn(today)
            .Where(s => (s.InstructorUserId ?? s.YogaClass.InstructorUserId) == user)).ToListAsync(ct) : [];
        var upcoming = await sessions.CountAsync(s => (s.SessionDate > today || s.SessionDate == today && s.StartTime > time) &&
            (s.SessionStatus == PTSessionStatus.Scheduled || s.SessionStatus == PTSessionStatus.ReschedulePending ||
             s.SessionStatus == PTSessionStatus.RescheduleRequested || s.SessionStatus == PTSessionStatus.CancellationPending ||
             s.SessionStatus == PTSessionStatus.CancellationRequested), ct);
        return new(today, await contracts.Select(p => p.MemberUserId).Distinct().CountAsync(ct), pt.Length, yoga.Count,
            upcoming, pt.Concat(yoga).OrderBy(s => s.Start).ThenBy(s => s.Name).ToArray(), progress);
    }
}
