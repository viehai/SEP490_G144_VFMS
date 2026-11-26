using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    /// <summary>GBR-105: trainers who accept new members and have the package's trainer level.</summary>
    public async Task<Result<IReadOnlyList<PTReplacementTrainer>>> ReplacementTrainers(string actor, int contractId, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<IReadOnlyList<PTReplacementTrainer>>();
        var contract = await MemberContract(actor, contractId, ct);
        if (contract is null)
            return Missing<IReadOnlyList<PTReplacementTrainer>>();
        var gymId = contract.PTUser.GymId;
        var trainers = await repo.GetReplacementTrainersAsync(gymId, contract.TrainerLevelSnapshot, contract.PTUserId, ct);
        return Result<IReadOnlyList<PTReplacementTrainer>>.Success(trainers);
    }
}
