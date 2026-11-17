using System.Text.Json;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    /// <summary>GBR-99: trainers who accept new members and have the package's trainer level.</summary>
    public async Task<Result<IReadOnlyList<PTReplacementTrainer>>> ReplacementTrainers(string actor, int contractId, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<IReadOnlyList<PTReplacementTrainer>>();
        var contract = await ContractsWithSessions.SingleOrDefaultAsync(store, p => p.Id == contractId && p.MemberUserId == actor, ct);
        if (contract is null)
            return Missing<IReadOnlyList<PTReplacementTrainer>>();
        var gymId = contract.PTUser.GymId;
        var yoga = ActiveYogaAssignments.Select(c => c.InstructorUserId);
        var trainers = await (from profile in store.Query<TrainerProfile>()
                              join user in ActiveUsers on profile.PTUserId equals user.UserId
                              where profile.IsActive && profile.AcceptingNewMembers && profile.Specialty != "Yoga" &&
                                  profile.Level == contract.TrainerLevelSnapshot && profile.PTUserId != contract.PTUserId &&
                                  user.GymId == gymId && !yoga.Contains(profile.PTUserId) &&
                                  user.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.PT))
                              orderby user.FullName
                              select new PTReplacementTrainer(user.UserId, profile.Id, user.FullName, profile.Level.ToString(),
                                  profile.Specialty)).ToListAsync(store, ct);
        return Result<IReadOnlyList<PTReplacementTrainer>>.Success(trainers);
    }

    private async Task<Result<(PTPurchase Contract, FreezePlan Plan)>> PlanTrainerChange(string actor, int contractId,
        PTTrainerChangeRequest request, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<(PTPurchase, FreezePlan)>();
        var contract = await ContractsWithSessions.SingleOrDefaultAsync(store, p => p.Id == contractId && p.MemberUserId == actor, ct);
        if (contract is null)
            return Missing<(PTPurchase, FreezePlan)>();
        // UC 4.13 outside a freeze request: only inside the one-week window opened by UC 4.12.
        if (EffectiveStatus(contract) != PTPurchaseStatus.Active || contract.TrainerChangeDeadline is not { } deadline || deadline <= Now)
            return Result<(PTPurchase, FreezePlan)>.Failure(
                "Chỉ đổi huấn luyện viên trong 1 tuần sau khi huấn luyện viên ngừng nhận học viên, hoặc khi xin bảo lưu.",
                "PT_TRAINER_CHANGE_CLOSED", 409);
        if (request.StartDate <= Today)
            return Invalid<(PTPurchase, FreezePlan)>("Chọn ngày bắt đầu với huấn luyện viên mới từ ngày mai trở đi.");
        var plan = await PlanTransfer(contract, Today, request.StartDate, request.TrainerId, request.TemplateIds, ct);
        return plan.IsSuccess
            ? Result<(PTPurchase, FreezePlan)>.Success((contract, plan.Data!))
            : Result<(PTPurchase, FreezePlan)>.Failure(plan.ErrorMessage!, plan.ErrorCode!, plan.StatusCode);
    }

    public async Task<Result<PTTrainerChangePreview>> PreviewTrainerChange(string actor, int contractId,
        PTTrainerChangeRequest request, CancellationToken ct)
    {
        var result = await PlanTrainerChange(actor, contractId, request, ct);
        if (!result.IsSuccess)
            return Result<PTTrainerChangePreview>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
        var plan = result.Data.Plan;
        return Result<PTTrainerChangePreview>.Success(new(plan.End,
            plan.Sessions.Select(r => Planned(r, plan.TrainerId)).ToArray(),
            plan.Conflicts.Select(r => Planned(r, plan.TrainerId)).ToArray()));
    }

    /// <summary>
    /// UC 4.13: moves the remaining sessions to the new trainer. Taught sessions and notes stay with the previous trainer;
    /// total sessions, price and freeze status are unchanged and no payment is created (GBR-99).
    /// </summary>
    public Task<Result<PTContractResponse>> ChangeTrainer(string actor, int contractId, PTTrainerChangeRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var result = await PlanTrainerChange(actor, contractId, request, ct);
        if (!result.IsSuccess)
            return Result<PTContractResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
        var (contract, plan) = result.Data;
        if (plan.Conflicts.Count > 0)
            return Result<PTContractResponse>.Failure("Một số ca đã có người giữ hoặc trùng lịch. Hãy chọn mẫu lịch khác.",
                "PT_TRAINER_SLOT_UNAVAILABLE", 409);
        var now = Now;
        var previousTrainer = contract.PTUserId;
        foreach (var (original, slot) in plan.Sessions)
        {
            if (original.SessionStatus != PTSessionStatus.Cancelled)
            {
                ClearProposal(original);
                original.SessionStatus = PTSessionStatus.Cancelled;
                original.CancelledAt = now;
            }
            contract.Sessions.Add(new PTSession
            {
                PTUserId = plan.TrainerId,
                MemberUserId = contract.MemberUserId,
                PTAvailabilityId = slot.TemplateId,
                SessionDate = slot.Date,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                CreditUnits = original.CreditUnits,
                ReplacementForSessionId = original.Id
            });
        }
        contract.PTUserId = plan.TrainerId;
        contract.WeeklyPatternJson = JsonSerializer.Serialize(new SessionBasedPattern(plan.Sessions
            .Select(r => new PTSlotInput(r.Slot.Date.DayOfWeek, r.Slot.StartTime)).Distinct().ToArray()));
        contract.TrainerChangeDeadline = null;
        contract.EndDate = LastSessionDate(contract);
        Audit(actor, nameof(PTPurchase), contract.Id, $"TrainerChanged:{previousTrainer}->{plan.TrainerId}");
        var message = $"Các buổi còn lại đã chuyển sang huấn luyện viên mới từ {request.StartDate:dd/MM/yyyy}.";
        Notify(actor, "Đã đổi huấn luyện viên", message, nameof(PTPurchase), contract.Id);
        Notify(previousTrainer, "Hội viên đổi huấn luyện viên", "Các buổi chưa dạy của một hợp đồng đã chuyển sang huấn luyện viên khác.",
            nameof(PTPurchase), contract.Id);
        Notify(plan.TrainerId, "Nhận hội viên mới", message, nameof(PTPurchase), contract.Id);
        await store.Save(ct);
        var refreshed = await ContractsWithSessions.SingleOrDefaultAsync(store, p => p.Id == contract.Id, ct);
        return Result<PTContractResponse>.Success(ContractResponse(refreshed!));
    }, ct);
}
