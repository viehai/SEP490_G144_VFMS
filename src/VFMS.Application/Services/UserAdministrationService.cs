using VFMS.Application.Interfaces;
using MapsterMapper;
using VFMS.Contracts.Common;
using VFMS.Contracts.Users;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using System.Security.Cryptography;

namespace VFMS.Application.Services;

public sealed class UserAdministrationService : IUserAdministrationService
{
    private const string CustomerRole = "Customer";
    private static readonly string[] AssignableRoles = ["PT", "YogaInstructor", "Staff", "Manager"];
    private static readonly string[] FilterableRoles = ["Customer", "Member", "PT", "YogaInstructor", "Staff", "Manager", "Admin"];

    private readonly IUserAdministrationRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;

    public UserAdministrationService(
        IUserAdministrationRepository repository,
        IPasswordHasher passwordHasher,
        IMapper mapper)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
    }

    public async Task<Result<WalkInCustomerResponse>> CreateWalkInCustomerAsync(
        WalkInCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var generatedPassword = GenerateTemporaryPassword();
        var createResult = await CreateUserAsync(
            request.FullName,
            request.Email,
            request.PhoneNumber,
            generatedPassword,
            CustomerRole,
            null,
            cancellationToken);
        if (!createResult.IsSuccess)
            return Result<WalkInCustomerResponse>.Failure(
                createResult.ErrorMessage!, createResult.ErrorCode!, createResult.StatusCode);

        var user = createResult.Data!;

        return Result<WalkInCustomerResponse>.Success(
            new WalkInCustomerResponse(
                user.UserId,
                user.FullName,
                string.IsNullOrWhiteSpace(request.Email) ? null : user.Email,
                user.PhoneNumber,
                user.AccountStatus.ToString(),
                user.UserRoles.Select(userRole => userRole.Role.RoleName).ToArray(),
                generatedPassword),
            201);
    }

    public async Task<Result<UserProfileResponse>> GetMyProfileAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        return user is null
            ? Result<UserProfileResponse>.Failure("User was not found.", "USER_NOT_FOUND", 404)
            : Result<UserProfileResponse>.Success(_mapper.Map<UserProfileResponse>(user));
    }

    public async Task<Result<UserProfileResponse>> UpdateMyProfileAsync(
        string userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<UserProfileResponse>.Failure("User was not found.", "USER_NOT_FOUND", 404);

        var phoneNumber = request.PhoneNumber.Trim();
        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal) &&
            await _repository.PhoneNumberExistsAsync(phoneNumber, userId, cancellationToken))
            return Result<UserProfileResponse>.Failure(
                "A user with this phone number already exists.", "USER_PHONE_EXISTS", 409);

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = phoneNumber;
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        await _repository.UpdateUserAsync(user, cancellationToken);

        return Result<UserProfileResponse>.Success(_mapper.Map<UserProfileResponse>(user));
    }

    public async Task<Result<bool>> ChangeMyPasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<bool>.Failure("User was not found.", "USER_NOT_FOUND", 404);

        if (!_passwordHasher.Verify(request.OldPassword, user.PasswordHash))
            return Result<bool>.Failure("The current password is incorrect.", "AUTH_INVALID_PASSWORD", 400);

        if (string.Equals(request.OldPassword, request.NewPassword, StringComparison.Ordinal))
            return Result<bool>.Failure("New password must differ from the old password.", "AUTH_PASSWORD_UNCHANGED", 400);

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _repository.UpdateUserAsync(user, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<PagedUsersResponse>> GetUsersAsync(
        UserListRequest request,
        CancellationToken cancellationToken = default)
    {
        AccountStatus? accountStatus = null;
        if (!string.IsNullOrWhiteSpace(request.AccountStatus))
        {
            if (!Enum.TryParse(request.AccountStatus, ignoreCase: true, out AccountStatus parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
                return Result<PagedUsersResponse>.Failure("Account status filter is invalid.", "USER_INVALID_STATUS");
            accountStatus = parsedStatus;
        }

        string? role = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            role = FilterableRoles.FirstOrDefault(
                name => string.Equals(name, request.Role.Trim(), StringComparison.OrdinalIgnoreCase));
            if (role is null)
                return Result<PagedUsersResponse>.Failure("Role filter is invalid.", "USER_INVALID_ROLE");
        }
        var (users, totalCount) = await _repository.GetUsersAsync(
            role,
            accountStatus,
            request.Search?.Trim(),
            request.Page,
            request.PageSize,
            cancellationToken);

        var items = users.Select(MapUser).ToArray();
        return Result<PagedUsersResponse>.Success(
            new PagedUsersResponse(request.Page, request.PageSize, totalCount, items));
    }

    public async Task<Result<UserAccountResponse>> CreateStaffAsync(
        CreateStaffRequest request,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var role = AssignableRoles.FirstOrDefault(
            name => string.Equals(name, request.RoleName, StringComparison.OrdinalIgnoreCase));
        if (role is null)
            return Result<UserAccountResponse>.Failure("Role must be PT, YogaInstructor, Staff, or Manager.", "USER_INVALID_ROLE");

        var auditLog = CreateAudit(
            actorUserId,
            AuditActionType.Create,
            null,
            ipAddress);

        var createResult = await CreateUserAsync(
            request.FullName,
            request.Email,
            request.PhoneNumber,
            request.Password,
            role,
            auditLog,
            cancellationToken);
        if (!createResult.IsSuccess)
            return Result<UserAccountResponse>.Failure(
                createResult.ErrorMessage!, createResult.ErrorCode!, createResult.StatusCode);

        return Result<UserAccountResponse>.Success(MapUser(createResult.Data!), 201);
    }

    public async Task<Result<UserAccountResponse>> UpdateRolesAsync(
        string userId,
        UpdateUserRolesRequest request,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
            return UserNotFound();

        var requestedRoleNames = request.Roles
            .Select(role => AssignableRoles.FirstOrDefault(
                name => string.Equals(name, role, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (requestedRoleNames.Any(role => role is null))
            return Result<UserAccountResponse>.Failure("Roles may only contain PT, YogaInstructor, Staff, or Manager.", "USER_INVALID_ROLE");

        var requestedRoles = requestedRoleNames.Cast<string>().ToArray();
        var roles = await _repository.GetRolesByNamesAsync(requestedRoles, cancellationToken);
        if (roles.Count != requestedRoles.Length)
            return Result<UserAccountResponse>.Failure("One or more roles are not configured.", "USER_ROLE_NOT_FOUND", 500);

        var oldRoles = user.UserRoles.Select(userRole => userRole.Role.RoleName).Order().ToArray();
        var newRoles = roles.Select(role => role.RoleName).Order().ToArray();
        if (oldRoles.SequenceEqual(newRoles, StringComparer.OrdinalIgnoreCase))
            return Result<UserAccountResponse>.Success(MapUser(user));

        var auditLog = CreateAudit(
            actorUserId,
            AuditActionType.RoleChange,
            string.Join(",", oldRoles),
            ipAddress,
            string.Join(",", newRoles));
        auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";

        await _repository.ReplaceUserRolesAsync(user, roles.Select(role => role.Id).ToArray(), auditLog, cancellationToken);
        user.UserRoles = roles.Select(role => new ApplicationUserRole
        {
            UserId = user.UserId,
            RoleId = role.Id,
            Role = role
        }).ToList();
        return Result<UserAccountResponse>.Success(MapUser(user));
    }

    public Task<Result<UserAccountResponse>> LockUserAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, actorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result<UserAccountResponse>.Failure(
                "Administrators cannot lock their own account.", "USER_SELF_LOCK_DENIED", 403));
        }

        return SetAccountStatusAsync(
            userId, AccountStatus.Locked, true, actorUserId, ipAddress, cancellationToken);
    }

    public Task<Result<UserAccountResponse>> UnlockUserAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default) =>
        SetAccountStatusAsync(userId, AccountStatus.Active, false, actorUserId, ipAddress, cancellationToken);

    private async Task<Result<ApplicationUser>> CreateUserAsync(
        string fullName,
        string? email,
        string phoneNumber,
        string password,
        string roleName,
        AuditLog? auditLog,
        CancellationToken cancellationToken)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (email is not null && await _repository.EmailExistsAsync(email, cancellationToken))
            return Result<ApplicationUser>.Failure("Email is already in use.", "AUTH_EMAIL_EXISTS", 409);

        if (email is null && await _repository.PhoneNumberExistsAsync(
            phoneNumber.Trim(), cancellationToken: cancellationToken))
            return Result<ApplicationUser>.Failure(
                "A user with this phone number already exists. Provide a unique phone number or an email address.",
                "USER_PHONE_EXISTS",
                409);

        var role = await _repository.GetRoleByNameAsync(roleName, cancellationToken);
        if (role is null)
            return Result<ApplicationUser>.Failure("Role is not configured.", "USER_ROLE_NOT_FOUND", 500);

        var gym = await _repository.GetDefaultGymAsync(cancellationToken);
        if (gym is null)
            return Result<ApplicationUser>.Failure("No gym configured in the system.", "AUTH_NO_GYM", 500);

        var userId = Guid.NewGuid().ToString();
        var user = new ApplicationUser
        {
            UserId = userId,
            GymId = gym.Id,
            FullName = fullName.Trim(),
            Email = email ?? $"walkin-{userId}@walkin.invalid",
            PhoneNumber = phoneNumber.Trim(),
            PasswordHash = _passwordHasher.Hash(password),
            AccountStatus = AccountStatus.Active
        };

        if (auditLog is not null)
        {
            auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";
            auditLog.NewValue = $"UserId={user.UserId}; Role={role.RoleName}; Status={user.AccountStatus}";
        }
        await _repository.CreateUserAsync(user, role.Id, auditLog, cancellationToken);
        user.UserRoles = [new ApplicationUserRole { UserId = user.UserId, RoleId = role.Id, Role = role }];

        return Result<ApplicationUser>.Success(user, 201);
    }

    private async Task<Result<UserAccountResponse>> SetAccountStatusAsync(
        string userId,
        AccountStatus status,
        bool revokeActiveRefreshTokens,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
            return UserNotFound();

        if (status == AccountStatus.Locked && user.AccountStatus == AccountStatus.Locked)
            return Result<UserAccountResponse>.Failure("Account is already locked.", "USER_ALREADY_LOCKED", 409);

        if (status == AccountStatus.Active && user.AccountStatus != AccountStatus.Locked)
            return Result<UserAccountResponse>.Failure("Only locked accounts can be unlocked.", "USER_NOT_LOCKED", 409);

        var action = status == AccountStatus.Locked ? AuditActionType.AccountLock : AuditActionType.AccountUnlock;
        var auditLog = CreateAudit(
            actorUserId,
            action,
            user.AccountStatus.ToString(),
            ipAddress,
            status.ToString());
        auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";

        await _repository.UpdateAccountStatusAsync(
            user, status, revokeActiveRefreshTokens, auditLog, cancellationToken);
        user.AccountStatus = status;
        return Result<UserAccountResponse>.Success(MapUser(user));
    }

    private static AuditLog CreateAudit(
        string actorUserId,
        AuditActionType action,
        string? oldValue,
        string? ipAddress,
        string? newValue = null) =>
        new()
        {
            ActorUserId = actorUserId,
            ActionType = action,
            AuditEntity = "ApplicationUser",
            OldValue = oldValue,
            NewValue = newValue,
            OccurredAt = DateTime.UtcNow,
            IpAddress = ipAddress
        };

    private UserAccountResponse MapUser(ApplicationUser user) =>
        _mapper.Map<UserAccountResponse>(user);

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%";
        var alphabet = upper + lower + digits + special;
        var password = new char[12];

        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (var index = 4; index < password.Length; index++)
            password[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        for (var index = password.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[swapIndex]) = (password[swapIndex], password[index]);
        }

        return new string(password);
    }

    private static Result<UserAccountResponse> UserNotFound() =>
        Result<UserAccountResponse>.Failure("User was not found.", "USER_NOT_FOUND", 404);
}
