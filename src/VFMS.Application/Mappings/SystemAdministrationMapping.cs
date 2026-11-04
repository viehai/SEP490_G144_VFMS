using Mapster;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;

namespace VFMS.Application.Mappings;

public sealed class SystemAdministrationMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<BannedWordList, BannedWordResponse>()
            .Map(destination => destination.CreatedByName, source => source.Admin == null ? null : source.Admin.FullName);

        config.NewConfig<AuditLog, AuditLogResponse>()
            .Map(destination => destination.ActionType, source => source.ActionType.ToString())
            .Map(destination => destination.ActorName, source => source.ActorUser == null ? null : source.ActorUser.FullName);
    }
}
