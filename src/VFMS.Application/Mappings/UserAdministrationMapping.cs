using Mapster;
using VFMS.Contracts.Users;
using VFMS.Domain.Entities;

namespace VFMS.Application.Mappings;

public sealed class UserAdministrationMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ApplicationUser, UserAccountResponse>()
            .Map(destination => destination.Email, source => source.Email.EndsWith("@walkin.invalid")
                ? null
                : source.Email)
            .Map(destination => destination.AccountStatus, source => source.AccountStatus.ToString())
            .Map(destination => destination.Roles, source =>
                source.UserRoles.Select(userRole => userRole.Role.RoleName).Order().ToArray());

        config.NewConfig<ApplicationUser, UserProfileResponse>()
            .Map(destination => destination.Email, source => source.Email.EndsWith("@walkin.invalid")
                ? null
                : source.Email)
            .Map(destination => destination.AccountStatus, source => source.AccountStatus.ToString())
            .Map(destination => destination.Roles, source =>
                source.UserRoles.Select(userRole => userRole.Role.RoleName).Order().ToArray());
    }
}
