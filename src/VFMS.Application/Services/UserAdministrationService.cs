using System.Security.Cryptography;
using System.Text;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Common;
using VFMS.Contracts.Users;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class UserAdministrationService : IUserAdministrationService
{
    private const string CustomerRole = "Customer";
    private const string WalkInPlaceholderEmailSuffix = "@walkin.invalid";
    private static readonly string[] AssignableRoles = ["PT", "YogaInstructor", "Staff", "Manager"];
    private static readonly string[] FilterableRoles = ["Customer", "Member", "PT", "YogaInstructor", "Staff", "Manager", "Admin"];

    private readonly IUserAdministrationRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly IEmailService? _emailService;
    private readonly CustomerPasswordSetupSettings _setupSettings;
    private readonly TimeProvider _clock;

    public UserAdministrationService(
        IUserAdministrationRepository repository,
        IPasswordHasher passwordHasher,
        IMapper mapper,
        IEmailService? emailService = null,
        CustomerPasswordSetupSettings? setupSettings = null,
        TimeProvider? clock = null)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
        _emailService = emailService;
        _setupSettings = setupSettings ?? new CustomerPasswordSetupSettings();
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<Result<WalkInCustomerResponse>> CreateWalkInCustomerAsync(
        WalkInCustomerRequest request,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Result<WalkInCustomerResponse>.Failure(
                "Email is required to send the password setup link.",
                "USER_EMAIL_REQUIRED",
                400);

        if (!IsSetupEmailAddress(request.Email.Trim()))
            return Result<WalkInCustomerResponse>.Failure(
                "Enter a valid email address that can receive the setup link.", "USER_EMAIL_INVALID", 400);

        if (!CanSendCustomerSetupEmail())
            return SetupEmailUnavailable<WalkInCustomerResponse>();

        var now = _clock.GetUtcNow().UtcDateTime;
        var rawToken = GenerateSetupToken();
        var tokenHash = ComputeCustomerSetupHash(rawToken);
        var expiresAt = now.AddHours(_setupSettings.TokenLifetimeHours);
        var placeholderSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var auditLog = CreateAudit(
            actorUserId,
            AuditActionType.Create,
            null,
            ipAddress,
            occurredAt: now);

        var createResult = await CreateUserAsync(
            request.FullName,
            request.Email,
            request.PhoneNumber,
            placeholderSecret,
            CustomerRole,
            AccountStatus.PendingVerification,
            tokenHash,
            expiresAt,
            auditLog,
            cancellationToken);

        if (!createResult.IsSuccess)
            return Result<WalkInCustomerResponse>.Failure(
                createResult.ErrorMessage!, createResult.ErrorCode!, createResult.StatusCode);

        var user = createResult.Data!;
        try
        {
            await SendCustomerSetupEmailAsync(user.FullName, user.Email, rawToken, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var audit = SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupEmailAborted");
            await _repository.ClearPasswordSetupTokenAsync(user.UserId, tokenHash, audit.OccurredAt, audit, CancellationToken.None);
            throw;
        }
        catch
        {
            var audit = SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupEmailFailed");
            await _repository.ClearPasswordSetupTokenAsync(user.UserId, tokenHash, audit.OccurredAt, audit, CancellationToken.None);
            return Result<WalkInCustomerResponse>.Success(
                    MapPendingCustomer(user, emailSent: false, expiresAt: null,
                    "Customer account was created, but the setup email could not be sent. Please resend the setup link."),
                201);
        }

        await _repository.SaveSetupAuditAsync(
            [SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupEmailAccepted")], CancellationToken.None);
        return Result<WalkInCustomerResponse>.Success(
            MapPendingCustomer(user, emailSent: true, expiresAt,
                "Customer account created. A 48-hour password setup link has been sent to the customer's email."),
            201);
    }

    public async Task<Result<WalkInCustomerResponse>> FindPendingCustomerAsync(
        FindPendingCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        string? email = null;
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            email = UserIdentityRules.NormalizeEmail(request.Email);
            if (!IsSetupEmailAddress(email))
                return Result<WalkInCustomerResponse>.Failure("Enter a valid email address.", "USER_EMAIL_INVALID", 400);
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            if (!UserIdentityRules.TryNormalizePhone(request.PhoneNumber, out var normalizedPhone))
                return Result<WalkInCustomerResponse>.Failure("Enter a valid phone number.", "USER_PHONE_INVALID", 400);
            phone = normalizedPhone;
        }

        if (email is null && phone is null)
            return Result<WalkInCustomerResponse>.Failure(
                "Enter the customer's email or phone number.", "USER_SETUP_LOOKUP_REQUIRED", 400);

        var user = await _repository.FindPendingCustomerAsync(email, phone, cancellationToken);
        return user is null || !IsSetupEmailAddress(user.Email)
            ? NoPendingCustomer<WalkInCustomerResponse>()
            : Result<WalkInCustomerResponse>.Success(MapPendingCustomer(user, emailSent: false, expiresAt: null,
                "Pending Customer account found. You can resend the setup link."));
    }

    public async Task<Result<ResendSetupEmailResponse>> ResendCustomerSetupEmailAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!CanSendCustomerSetupEmail())
            return SetupEmailUnavailable<ResendSetupEmailResponse>();
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
            return NoPendingCustomer<ResendSetupEmailResponse>(409);

        AuditLog? failureAudit = null;
        var result = await _repository.ExecuteCustomerSetupResendAsync(userId, async () =>
        {
            var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
            if (user is null || !IsEligiblePendingCustomer(user) || !IsSetupEmailAddress(user.Email))
                return NoPendingCustomer<ResendSetupEmailResponse>(409);

            var rawToken = GenerateSetupToken();
            var issued = SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupLinkIssued");
            var expiresAt = issued.OccurredAt.AddHours(_setupSettings.TokenLifetimeHours);
            var rotated = await _repository.TryRotateCustomerSetupTokenAsync(
                user.UserId, user.Email, ComputeCustomerSetupHash(rawToken), expiresAt, issued.OccurredAt, cancellationToken);
            if (!rotated)
                return NoPendingCustomer<ResendSetupEmailResponse>(409);

            try
            {
                await SendCustomerSetupEmailAsync(user.FullName, user.Email, rawToken, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failureAudit = SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupEmailFailed");
                return Result<ResendSetupEmailResponse>.Failure(
                    "Unable to send the password setup email. Please try again later.",
                    "CUSTOMER_SETUP_EMAIL_FAILED", 503);
            }

            await _repository.SaveSetupAuditAsync(
                [issued, SetupAudit(actorUserId, user.UserId, ipAddress, "CustomerSetupEmailAccepted")], CancellationToken.None);
            return Result<ResendSetupEmailResponse>.Success(new ResendSetupEmailResponse(
                user.UserId, user.Email, expiresAt, "A new 48-hour setup link has been sent to the customer's email."));
        }, cancellationToken);

        if (failureAudit is not null)
            await _repository.SaveSetupAuditAsync([failureAudit], CancellationToken.None);
        return result;
    }

    public async Task<Result<SetupPasswordResponse>> SetupCustomerPasswordAsync(
        SetupPasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!new SetupPasswordRequestValidator().Validate(request).IsValid)
            return Result<SetupPasswordResponse>.Failure(
                "Check the setup token, password strength, and password confirmation.",
                "AUTH_INVALID_SETUP_REQUEST",
                400);

        var tokenHash = ComputeCustomerSetupHash(request.Token);
        var user = await _repository.GetUserByPasswordSetupTokenHashAsync(tokenHash, cancellationToken);
        if (user is null ||
            !user.PasswordSetupExpiresAt.HasValue ||
            user.PasswordSetupExpiresAt.Value <= _clock.GetUtcNow().UtcDateTime ||
            !IsEligiblePendingCustomer(user))
        {
            return InvalidSetupToken();
        }

        var newPasswordHash = _passwordHasher.Hash(request.NewPassword);
        var now = _clock.GetUtcNow().UtcDateTime;
        var auditLog = CreateAudit(
            user.UserId,
            AuditActionType.PasswordChange,
            $"Status={AccountStatus.PendingVerification}",
            ipAddress,
            $"Action=CustomerPasswordSetup; Status={AccountStatus.Active}",
            now);
        auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";

        var completed = await _repository.TryCompletePasswordSetupAsync(
            user.UserId,
            tokenHash,
            newPasswordHash,
            auditLog,
            cancellationToken);

        return completed
            ? Result<SetupPasswordResponse>.Success(
                new SetupPasswordResponse("Password set successfully. You can now log in with your new password."))
            : InvalidSetupToken();
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

        if (!UserIdentityRules.TryNormalizePhone(request.PhoneNumber, out var phoneNumber))
            return Result<UserProfileResponse>.Failure("Invalid phone number format.", "USER_INVALID_PHONE");
        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal) &&
            await _repository.PhoneNumberExistsAsync(phoneNumber, userId, cancellationToken))
            return Result<UserProfileResponse>.Failure(
                "A user with this phone number already exists.", "USER_PHONE_EXISTS", 409);

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = phoneNumber;
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        if (request.DateOfBirth.HasValue)
        {
            user.PhysicalProfile ??= new PhysicalProfile { MemberUserId = user.UserId };
            user.PhysicalProfile.DateOfBirth = request.DateOfBirth.Value;
        }
        else if (user.PhysicalProfile is not null)
        {
            user.PhysicalProfile.DateOfBirth = null;
        }
        var updated = await _repository.UpdateUserAsync(user, cancellationToken);
        return updated.IsSuccess
            ? Result<UserProfileResponse>.Success(_mapper.Map<UserProfileResponse>(user))
            : Result<UserProfileResponse>.Failure(updated.ErrorMessage!, updated.ErrorCode!, updated.StatusCode);
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
        return await _repository.UpdateUserAsync(user, cancellationToken);
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
            ipAddress,
            occurredAt: _clock.GetUtcNow().UtcDateTime);

        var createResult = await CreateUserAsync(
            request.FullName,
            request.Email,
            request.PhoneNumber,
            request.Password,
            role,
            AccountStatus.Active,
            passwordSetupTokenHash: null,
            passwordSetupExpiresAt: null,
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
        if (requestedRoles.Contains(nameof(UserRole.PT)) &&
            await _repository.HasActiveYogaAssignmentsAsync(userId, cancellationToken))
            return Result<UserAccountResponse>.Failure("A Yoga instructor with active classes cannot be assigned the PT role.",
                "TRAINER_TEACHING_ROLE_CONFLICT", 409);
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
            string.Join(",", newRoles),
            _clock.GetUtcNow().UtcDateTime);
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
            userId, lockAccount: true, actorUserId, ipAddress, cancellationToken);
    }

    public Task<Result<UserAccountResponse>> UnlockUserAsync(
        string userId,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default) =>
        SetAccountStatusAsync(userId, lockAccount: false, actorUserId, ipAddress, cancellationToken);

    private Task<Result<ApplicationUser>> CreateUserAsync(
        string fullName,
        string email,
        string phoneNumber,
        string password,
        string roleName,
        AccountStatus initialStatus,
        string? passwordSetupTokenHash,
        DateTime? passwordSetupExpiresAt,
        AuditLog? auditLog,
        CancellationToken cancellationToken) =>
        _repository.ExecuteProvisioningAsync(async () =>
        {
            var normalizedEmail = UserIdentityRules.NormalizeEmail(email);
            if (!UserIdentityRules.IsValidEmail(normalizedEmail))
                return Result<ApplicationUser>.Failure("Invalid email format.", "USER_INVALID_EMAIL");
            if (!UserIdentityRules.TryNormalizePhone(phoneNumber, out var normalizedPhone))
                return Result<ApplicationUser>.Failure("Invalid phone number format.", "USER_INVALID_PHONE");

            if (await _repository.EmailExistsAsync(normalizedEmail, cancellationToken))
                return Result<ApplicationUser>.Failure("Email is already in use.", "AUTH_EMAIL_EXISTS", 409);

            if (await _repository.PhoneNumberExistsAsync(normalizedPhone, cancellationToken: cancellationToken))
                return Result<ApplicationUser>.Failure(
                    "A user with this phone number already exists.",
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
                Email = normalizedEmail,
                PhoneNumber = normalizedPhone,
                PasswordHash = _passwordHasher.Hash(password),
                PasswordSetupTokenHash = passwordSetupTokenHash,
                PasswordSetupExpiresAt = passwordSetupExpiresAt,
                AccountStatus = initialStatus
            };

            if (auditLog is not null)
            {
                auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";
                auditLog.NewValue = $"UserId={user.UserId}; Role={role.RoleName}; Status={user.AccountStatus}";
                if (passwordSetupTokenHash is not null)
                    auditLog.NewValue += "; Action=CustomerSetupLinkIssued";
            }

            user.UserRoles = [new ApplicationUserRole { UserId = user.UserId, RoleId = role.Id, Role = role }];
            await _repository.CreateUserAsync(user, auditLog, cancellationToken);

            return Result<ApplicationUser>.Success(user, 201);
        }, cancellationToken);

    private async Task<Result<UserAccountResponse>> SetAccountStatusAsync(
        string userId,
        bool lockAccount,
        string actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
            return UserNotFound();

        if (lockAccount && user.AccountStatus == AccountStatus.Locked)
            return Result<UserAccountResponse>.Failure("Account is already locked.", "USER_ALREADY_LOCKED", 409);

        if (!lockAccount && user.AccountStatus != AccountStatus.Locked)
            return Result<UserAccountResponse>.Failure("Only locked accounts can be unlocked.", "USER_NOT_LOCKED", 409);

        var status = lockAccount ? AccountStatus.Locked : await GetRestoredAccountStatusAsync(user, cancellationToken);
        var action = lockAccount ? AuditActionType.AccountLock : AuditActionType.AccountUnlock;
        var auditLog = CreateAudit(
            actorUserId,
            action,
            user.AccountStatus.ToString(),
            ipAddress,
            status.ToString(),
            _clock.GetUtcNow().UtcDateTime);
        auditLog.AuditEntity = $"ApplicationUser:{user.UserId}";

        if (!await _repository.TryUpdateAccountStatusAsync(user, status, lockAccount, auditLog, cancellationToken))
            return Result<UserAccountResponse>.Failure("Account status changed. Refresh and try again.", "USER_ACCOUNT_STATUS_CHANGED", 409);

        return Result<UserAccountResponse>.Success(MapUser(user) with { AccountStatus = status.ToString() });
    }

    private async Task<AccountStatus> GetRestoredAccountStatusAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.AccountStatusBeforeLock is AccountStatus previousStatus && previousStatus != AccountStatus.Locked)
            return previousStatus;

        // Older locks did not preserve the previous status on the user row.
        if (user.PasswordSetupTokenHash is not null || user.PasswordSetupExpiresAt.HasValue)
            return AccountStatus.PendingVerification;

        return await _repository.GetStatusBeforeLatestLockAsync(user.UserId, cancellationToken) ?? AccountStatus.Inactive;
    }

    private Task SendCustomerSetupEmailAsync(
        string fullName,
        string recipientEmail,
        string rawToken,
        CancellationToken cancellationToken)
    {
        var link = $"{_setupSettings.SetupUrl}#token={rawToken}";
        var body =
            $"Xin chào {fullName},\n\n" +
            "Tài khoản khách hàng Victor Fitness của bạn đã được tạo tại quầy lễ tân.\n\n" +
            $"Mở liên kết sau để thiết lập mật khẩu và kích hoạt tài khoản:\n{link}\n\n" +
            $"Liên kết chỉ dùng một lần và hết hạn sau {_setupSettings.TokenLifetimeHours} giờ.\n" +
            "Nếu bạn không yêu cầu tạo tài khoản này, vui lòng liên hệ quầy lễ tân Victor Fitness.";

        return _emailService!.SendAsync(
            recipientEmail,
            "VICTOR FITNESS - Thiết lập mật khẩu tài khoản",
            body,
            cancellationToken);
    }

    private static bool IsEligiblePendingCustomer(ApplicationUser user)
    {
        if (user.AccountStatus != AccountStatus.PendingVerification || user.IsBanned || user.IsDeleted)
            return false;

        var activeRoles = user.UserRoles
            .Where(ur => !ur.IsDeleted && ur.Role is not null && !ur.Role.IsDeleted)
            .Select(ur => ur.Role.RoleName)
            .ToArray();

        return activeRoles.Length == 1 && string.Equals(activeRoles[0], CustomerRole, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanSendCustomerSetupEmail() => _setupSettings.IsValid && _emailService?.IsConfigured == true;

    private static bool IsSetupEmailAddress(string email) =>
        !email.EndsWith(WalkInPlaceholderEmailSuffix, StringComparison.OrdinalIgnoreCase) &&
        UserIdentityRules.IsValidEmail(email);

    private static WalkInCustomerResponse MapPendingCustomer(
        ApplicationUser user, bool emailSent, DateTime? expiresAt, string message) =>
        new(user.UserId, user.FullName, user.Email, user.PhoneNumber, user.AccountStatus.ToString(),
            [CustomerRole], expiresAt, emailSent, message);

    private static Result<T> NoPendingCustomer<T>(int statusCode = 404) => Result<T>.Failure(
        "No eligible pending Customer account was found.", "USER_SETUP_RESEND_NOT_ELIGIBLE", statusCode);

    private static Result<T> SetupEmailUnavailable<T>() => Result<T>.Failure(
        "Password setup email service is currently unavailable. Please contact an administrator.",
        "CUSTOMER_SETUP_EMAIL_UNAVAILABLE", 503);

    private AuditLog SetupAudit(string actorUserId, string userId, string? ipAddress, string action)
    {
        var audit = CreateAudit(actorUserId, AuditActionType.Update, null, ipAddress,
            $"Action={action}", _clock.GetUtcNow().UtcDateTime);
        audit.AuditEntity = $"ApplicationUser:{userId}";
        return audit;
    }

    private static AuditLog CreateAudit(
        string actorUserId,
        AuditActionType action,
        string? oldValue,
        string? ipAddress,
        string? newValue = null,
        DateTime? occurredAt = null) =>
        new()
        {
            ActorUserId = actorUserId,
            ActionType = action,
            AuditEntity = "ApplicationUser",
            OldValue = oldValue,
            NewValue = newValue,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
            IpAddress = ipAddress
        };

    private UserAccountResponse MapUser(ApplicationUser user) =>
        _mapper.Map<UserAccountResponse>(user);

    private static string GenerateSetupToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string ComputeCustomerSetupHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("VFMS:customer-password-setup:v1:" + token)));

    private static Result<UserAccountResponse> UserNotFound() =>
        Result<UserAccountResponse>.Failure("User was not found.", "USER_NOT_FOUND", 404);

    private static Result<SetupPasswordResponse> InvalidSetupToken() =>
        Result<SetupPasswordResponse>.Failure(
            "The password setup link is invalid, expired, or already used.",
            "AUTH_INVALID_SETUP_TOKEN",
            400);
}
