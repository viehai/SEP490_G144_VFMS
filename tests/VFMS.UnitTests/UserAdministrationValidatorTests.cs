using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Users;
using VFMS.Contracts.Users.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class UserAdministrationValidatorTests
{
    [Theory]
    [InlineData("PT")]
    [InlineData("Staff")]
    [InlineData("Manager")]
    public void CreateStaffAcceptsAssignableRoles(string role)
    {
        var validator = new CreateStaffRequestValidator();
        var request = new CreateStaffRequest(
            "Alex Smith",
            "alex@example.com",
            "0912345678",
            "Secure@123",
            "Secure@123",
            role);

        Assert.True(validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public void CreateStaffRejectsNonAssignableRoles(string role)
    {
        var validator = new CreateStaffRequestValidator();
        var request = new CreateStaffRequest(
            "Alex Smith",
            "alex@example.com",
            "0912345678",
            "Secure@123",
            "Secure@123",
            role);

        Assert.False(validator.Validate(request).IsValid);
    }

    [Fact]
    public void WalkInCustomerRequestHasNoRoleInput()
    {
        Assert.DoesNotContain(
            typeof(WalkInCustomerRequest).GetProperties(),
            property => string.Equals(property.Name, "Role", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "Password", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "ConfirmPassword", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WalkInValidatorRequiresOnlyNameAndPhoneWhenEmailIsOmitted()
    {
        var validator = new WalkInCustomerRequestValidator();

        Assert.True(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912345678")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("", "0912345678")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("Alex Smith", "")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912345678", "invalid")).IsValid);
    }

    [Fact]
    public void LoginValidatorAcceptsPhoneNumberAsIdentifier()
    {
        var validator = new LoginRequestValidator();

        Assert.True(validator.Validate(new LoginRequest("0912345678", "Customer@123")).IsValid);
        Assert.True(validator.Validate(new LoginRequest("customer@example.com", "Customer@123")).IsValid);
        Assert.False(validator.Validate(new LoginRequest("not-an-identifier", "Customer@123")).IsValid);
    }

    [Fact]
    public void ChangePasswordValidatorRequiresStrongDifferentPassword()
    {
        var validator = new ChangePasswordRequestValidator();

        Assert.True(validator.Validate(new ChangePasswordRequest("OldPass@123", "NewPass@123")).IsValid);
        Assert.False(validator.Validate(new ChangePasswordRequest("OldPass@123", "123")).IsValid);
        Assert.False(validator.Validate(new ChangePasswordRequest("OldPass@123", "OldPass@123")).IsValid);
    }

    [Fact]
    public void UpdateProfileValidatorChecksPhoneAndAvatarUrl()
    {
        var validator = new UpdateUserProfileRequestValidator();

        Assert.True(validator.Validate(new UpdateUserProfileRequest(
            "Alex Smith", "0912345678", "https://example.com/avatar.png")).IsValid);
        Assert.False(validator.Validate(new UpdateUserProfileRequest(
            "Alex Smith", "invalid", "javascript:alert(1)")).IsValid);
    }

    [Fact]
    public async Task WalkInCreatesCustomerWithGeneratedPasswordAndNoEmail()
    {
        var repository = new UserAdministrationRepositoryFake();
        var service = CreateService(repository);

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678"));

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.Email);
        Assert.Equal(new[] { "Customer" }, result.Data.Roles);
        Assert.Matches(@"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,32}$", result.Data.GeneratedPassword);
        Assert.Equal(result.Data.GeneratedPassword, repository.CreatedUser!.PasswordHash);
        Assert.EndsWith("@walkin.invalid", repository.CreatedUser.Email);
    }

    [Fact]
    public async Task ProfileCanBeReadUpdatedAndPasswordChangedOnlyAfterVerification()
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Alex Smith",
                Email = "alex@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "OldPass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole
                    {
                        RoleId = 1,
                        Role = new ApplicationRole { Id = 1, RoleName = "Customer" }
                    }
                ]
            }
        };
        var service = CreateService(repository);

        var profile = await service.GetMyProfileAsync("user-1");
        var updatedProfile = await service.UpdateMyProfileAsync(
            "user-1",
            new UpdateUserProfileRequest("Alex Updated", "0912345679", "https://example.com/avatar.png"));
        var invalidChange = await service.ChangeMyPasswordAsync(
            "user-1",
            new ChangePasswordRequest("Wrong@123", "NewPass@123"));
        var validChange = await service.ChangeMyPasswordAsync(
            "user-1",
            new ChangePasswordRequest("OldPass@123", "NewPass@123"));

        Assert.True(profile.IsSuccess);
        Assert.Equal("Alex Smith", profile.Data!.FullName);
        Assert.True(updatedProfile.IsSuccess);
        Assert.Equal("Alex Updated", updatedProfile.Data!.FullName);
        Assert.Equal("https://example.com/avatar.png", updatedProfile.Data.AvatarUrl);
        Assert.False(invalidChange.IsSuccess);
        Assert.True(validChange.IsSuccess);
        Assert.Equal("NewPass@123", repository.ExistingUser.PasswordHash);
    }

    private static UserAdministrationService CreateService(UserAdministrationRepositoryFake repository)
    {
        var mapperConfig = new TypeAdapterConfig();
        mapperConfig.Scan(typeof(UserAdministrationMapping).Assembly);
        IMapper mapper = new ServiceMapper(new EmptyServiceProvider(), mapperConfig);
        return new UserAdministrationService(repository, new PasswordHasherFake(), mapper);
    }

    [Fact]
    public async Task AdminCannotLockTheirOwnAccount()
    {
        var repository = new UserAdministrationRepositoryFake();
        var mapperConfig = new TypeAdapterConfig();
        mapperConfig.Scan(typeof(UserAdministrationMapping).Assembly);
        IMapper mapper = new ServiceMapper(new EmptyServiceProvider(), mapperConfig);
        var service = new UserAdministrationService(repository, new PasswordHasherFake(), mapper);

        var result = await service.LockUserAsync("admin-user", "ADMIN-USER", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("USER_SELF_LOCK_DENIED", result.ErrorCode);
        Assert.Equal(0, repository.GetUserByIdCalls);
    }

    [Fact]
    public void UserAdministrationMapsterProfileMapsAccountStatusAndRoles()
    {
        var mapperConfig = new TypeAdapterConfig();
        mapperConfig.Scan(typeof(UserAdministrationMapping).Assembly);
        IMapper mapper = new ServiceMapper(new EmptyServiceProvider(), mapperConfig);
        var user = new ApplicationUser
        {
            UserId = "user-1",
            FullName = "Alex Smith",
            Email = "alex@example.com",
            PhoneNumber = "0912345678",
            AccountStatus = AccountStatus.Locked,
            UserRoles =
            [
                new ApplicationUserRole
                {
                    Role = new ApplicationRole { RoleName = "Staff" }
                }
            ]
        };

        var response = mapper.Map<UserAccountResponse>(user);

        Assert.Equal(user.UserId, response.UserId);
        Assert.Equal("Locked", response.AccountStatus);
        Assert.Equal(new[] { "Staff" }, response.Roles);
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class UserAdministrationRepositoryFake : IUserAdministrationRepository
    {
        public int GetUserByIdCalls { get; private set; }
        public ApplicationUser? ExistingUser { get; init; }
        public ApplicationUser? CreatedUser { get; private set; }
        private readonly ApplicationRole _customerRole = new() { Id = 1, RoleName = "Customer" };

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> PhoneNumberExistsAsync(
            string phoneNumber,
            string? excludingUserId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Gym?>(new Gym { Id = 1 });

        public Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationRole?>(roleName == "Customer" ? _customerRole : null);

        public Task<IReadOnlyList<ApplicationRole>> GetRolesByNamesAsync(
            IReadOnlyCollection<string> roleNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRole>>([]);

        public Task<ApplicationUser?> GetUserByIdAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            GetUserByIdCalls++;
            return Task.FromResult(ExistingUser?.UserId == userId ? ExistingUser : null);
        }

        public Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
            string? role,
            AccountStatus? accountStatus,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<ApplicationUser>, int)>(([], 0));

        public Task CreateUserAsync(
            ApplicationUser user,
            int roleId,
            AuditLog? auditLog = null,
            CancellationToken cancellationToken = default)
        {
            CreatedUser = user;
            return Task.CompletedTask;
        }

        public Task ReplaceUserRolesAsync(
            ApplicationUser user,
            IReadOnlyCollection<int> roleIds,
            AuditLog auditLog,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAccountStatusAsync(
            ApplicationUser user,
            AccountStatus status,
            bool revokeActiveRefreshTokens,
            AuditLog auditLog,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
