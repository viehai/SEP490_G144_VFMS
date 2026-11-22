using System.Security.Cryptography;
using System.Text;
using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Common;
using VFMS.Contracts.Users;
using VFMS.Contracts.Users.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class UserAdministrationValidatorTests
{
    [Theory]
    [InlineData("PT")]
    [InlineData("YogaInstructor")]
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
    public void WalkInCustomerRequestHasNoRoleOrPasswordInputAndResponseHasNoPlaintextSecrets()
    {
        Assert.DoesNotContain(
            typeof(WalkInCustomerRequest).GetProperties(),
            property => string.Equals(property.Name, "Role", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "RoleId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "RoleName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "Password", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "ConfirmPassword", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            typeof(WalkInCustomerResponse).GetProperties(),
            property => string.Equals(property.Name, "GeneratedPassword", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "Password", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "Token", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "SetupToken", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "PasswordSetupTokenHash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WalkInValidatorRequiresNamePhoneAndValidEmail()
    {
        var validator = new WalkInCustomerRequestValidator();

        Assert.True(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912345678", "alex@example.com")).IsValid);
        Assert.True(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912-345-678", "alex@example.com")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("", "0912345678", "alex@example.com")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("Alex Smith", "", "alex@example.com")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912345678", "")).IsValid);
        Assert.False(validator.Validate(new WalkInCustomerRequest("Alex Smith", "0912345678", "invalid")).IsValid);
    }

    [Fact]
    public void PhoneNormalizationCanonicalizesFormattingVariants()
    {
        Assert.True(UserIdentityRules.TryNormalizePhone(" 0912-345-678 ", out var hyphenated));
        Assert.True(UserIdentityRules.TryNormalizePhone("(0912) 345 678", out var parenthesized));
        Assert.True(UserIdentityRules.TryNormalizePhone("+84 912-345-678", out var international));
        Assert.Equal("0912345678", hyphenated);
        Assert.Equal(hyphenated, parenthesized);
        Assert.Equal("+84912345678", international);
    }

    [Theory]
    [InlineData("++84912345678")]
    [InlineData("0912+345678")]
    [InlineData("(0912 345678")]
    [InlineData("0912)345678")]
    [InlineData("0912--345678")]
    [InlineData("0912A345678")]
    [InlineData("(--) --")]
    public void IdentityInputsRejectMalformedPhoneNumbers(string phoneNumber)
    {
        Assert.False(UserIdentityRules.TryNormalizePhone(phoneNumber, out _));
        Assert.False(new WalkInCustomerRequestValidator().Validate(
            new WalkInCustomerRequest("Walk-in User", phoneNumber, "walkin@example.test")).IsValid);
        Assert.False(new UpdateUserProfileRequestValidator().Validate(new UpdateUserProfileRequest("Profile User", phoneNumber)).IsValid);
        Assert.False(new LoginRequestValidator().Validate(new LoginRequest(phoneNumber, "Customer@123")).IsValid);
    }

    [Fact]
    public void SetupPasswordValidatorEnforcesTokenFormatAndGbr26PasswordRules()
    {
        var validator = new SetupPasswordRequestValidator();
        var validToken = new string('A', 64);

        Assert.True(validator.Validate(new SetupPasswordRequest(validToken, "Strong@123", "Strong@123")).IsValid);
        Assert.False(validator.Validate(new SetupPasswordRequest("short", "Strong@123", "Strong@123")).IsValid);
        Assert.False(validator.Validate(new SetupPasswordRequest(new string('a', 64), "Strong@123", "Strong@123")).IsValid);
        Assert.False(validator.Validate(new SetupPasswordRequest(validToken, "weakpass", "weakpass")).IsValid);
        Assert.False(validator.Validate(new SetupPasswordRequest(validToken, "Strong@123", "Mismatch@123")).IsValid);
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
    public async Task WalkInCreatesPendingCustomerWith48HourSetupLinkAuditAndNoPlaintextPassword()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var repository = new UserAdministrationRepositoryFake();
        var email = new EmailServiceFake();
        var service = CreateService(repository, email, new FixedTimeProvider(now));

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912-345-678", "alex@example.com"),
            actorUserId: "staff-1",
            ipAddress: "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal("alex@example.com", result.Data.Email);
        Assert.Equal("0912345678", result.Data.PhoneNumber);
        Assert.Equal(AccountStatus.PendingVerification.ToString(), result.Data.AccountStatus);
        Assert.Equal(new[] { "Customer" }, result.Data.Roles);
        Assert.True(result.Data.SetupEmailSent);
        Assert.Equal(now.UtcDateTime.AddHours(48), result.Data.PasswordSetupExpiresAt);

        Assert.NotNull(repository.CreatedUser);
        Assert.Equal(AccountStatus.PendingVerification, repository.CreatedUser.AccountStatus);
        Assert.Equal("0912345678", repository.CreatedUser.PhoneNumber);
        Assert.NotNull(repository.CreatedUser.PasswordSetupTokenHash);
        Assert.Equal(64, repository.CreatedUser.PasswordSetupTokenHash.Length);
        Assert.Equal(now.UtcDateTime.AddHours(48), repository.CreatedUser.PasswordSetupExpiresAt);

        Assert.Single(email.SentMessages);
        var sent = email.SentMessages[0];
        Assert.Equal("alex@example.com", sent.Recipient);
        Assert.Contains("https://localhost:7246/auth/setup-password#token=", sent.Body);
        var rawToken = ExtractTokenFromEmailBody(sent.Body);
        Assert.NotEqual(rawToken, repository.CreatedUser.PasswordSetupTokenHash);
        Assert.Equal(ComputeCustomerSetupHash(rawToken), repository.CreatedUser.PasswordSetupTokenHash);

        Assert.NotNull(repository.LastAuditLog);
        Assert.Equal("staff-1", repository.LastAuditLog.ActorUserId);
        Assert.Equal(AuditActionType.Create, repository.LastAuditLog.ActionType);
        Assert.Equal($"ApplicationUser:{repository.CreatedUser.UserId}", repository.LastAuditLog.AuditEntity);
        Assert.DoesNotContain(rawToken, repository.LastAuditLog.NewValue ?? string.Empty);
    }

    [Fact]
    public async Task WalkInRejectsDuplicatePhoneEvenWhenUniqueEmailIsProvided()
    {
        var repository = new UserAdministrationRepositoryFake { PhoneAlreadyExists = true };
        var email = new EmailServiceFake();
        var service = CreateService(repository, email);

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678", "unique@example.com"),
            actorUserId: "staff-1",
            ipAddress: "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("USER_PHONE_EXISTS", result.ErrorCode);
        Assert.Null(repository.CreatedUser);
        Assert.Empty(email.SentMessages);
    }

    [Fact]
    public async Task WalkInRejectsDuplicateEmailWith409()
    {
        var repository = new UserAdministrationRepositoryFake { EmailAlreadyExists = true };
        var email = new EmailServiceFake();
        var service = CreateService(repository, email);

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678", "dup@example.com"),
            actorUserId: "staff-1",
            ipAddress: "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("AUTH_EMAIL_EXISTS", result.ErrorCode);
        Assert.Null(repository.CreatedUser);
        Assert.Empty(email.SentMessages);
    }

    [Fact]
    public async Task WalkInFailsFastWith503WhenSmtpIsNotConfigured()
    {
        var repository = new UserAdministrationRepositoryFake();
        var email = new EmailServiceFake { IsConfigured = false };
        var service = CreateService(repository, email);

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678", "alex@example.com"),
            actorUserId: "staff-1",
            ipAddress: "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("CUSTOMER_SETUP_EMAIL_UNAVAILABLE", result.ErrorCode);
        Assert.Null(repository.CreatedUser);
    }

    [Theory]
    [InlineData("https://localhost:7246/auth/setup-password", true, true)]
    [InlineData("http://127.0.0.1:5273/auth/setup-password", true, true)]
    [InlineData("https://localhost:7246/auth/setup-password", false, false)]
    [InlineData("https://127.0.0.1/auth/setup-password", false, false)]
    [InlineData("https://[::1]/auth/setup-password", false, false)]
    [InlineData("https://vfms.internal/auth/setup-password", false, false)]
    [InlineData("https://vfms.example.com/auth/reset-password", false, false)]
    [InlineData("http://vfms.example.com/auth/setup-password", false, false)]
    [InlineData("https://vfms.example/auth/setup-password", false, false)]
    [InlineData("https://vfms.example.com/auth/setup-password", false, true)]
    [InlineData(null, false, false)]
    public void CustomerSetupUrlIsEnvironmentBound(string? setupUrl, bool development, bool expected) =>
        Assert.Equal(expected, CustomerPasswordSetupSettings.FromConfiguration(setupUrl, development).IsValid);

    [Fact]
    public async Task WalkInRejectsAddressReservedForLegacyPlaceholders()
    {
        var repository = new UserAdministrationRepositoryFake();
        var service = CreateService(repository, new EmailServiceFake());

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678", "alex@WALKIN.INVALID "), "staff-1", null);

        Assert.Equal("USER_EMAIL_INVALID", result.ErrorCode);
        Assert.Null(repository.CreatedUser);
    }

    [Fact]
    public async Task WalkInClearsTokenAndReportsSetupEmailSentFalseWhenSmtpThrows()
    {
        var repository = new UserAdministrationRepositoryFake();
        var email = new EmailServiceFake { ThrowOnSend = true };
        var service = CreateService(repository, email);

        var result = await service.CreateWalkInCustomerAsync(
            new WalkInCustomerRequest("Alex Smith", "0912345678", "alex@example.com"),
            actorUserId: "staff-1",
            ipAddress: "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.False(result.Data!.SetupEmailSent);
        Assert.Null(result.Data.PasswordSetupExpiresAt);
        Assert.Null(repository.CreatedUser!.PasswordSetupTokenHash);
        Assert.Null(repository.CreatedUser.PasswordSetupExpiresAt);
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

    private static UserAdministrationService CreateService(
        UserAdministrationRepositoryFake repository,
        IEmailService? emailService = null,
        TimeProvider? clock = null) =>
        CreateService((IUserAdministrationRepository)repository, emailService, clock);

    private static UserAdministrationService CreateService(
        IUserAdministrationRepository repository,
        IEmailService? emailService = null,
        TimeProvider? clock = null)
    {
        var mapperConfig = new TypeAdapterConfig();
        mapperConfig.Scan(typeof(UserAdministrationMapping).Assembly);
        IMapper mapper = new ServiceMapper(new EmptyServiceProvider(), mapperConfig);
        var setupSettings = CustomerPasswordSetupSettings.FromConfiguration(
            "https://localhost:7246/auth/setup-password", isDevelopment: true);
        return new UserAdministrationService(repository, new PasswordHasherFake(), mapper, emailService, setupSettings, clock);
    }

    private static string ExtractTokenFromEmailBody(string body)
    {
        const string marker = "#token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Setup email must contain #token= fragment.");
        return body.Substring(start + marker.Length, 64);
    }

    private static string ComputeCustomerSetupHash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("VFMS:customer-password-setup:v1:" + raw)));

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

    // ── Profile-specific unit tests ──────────────────────────────────────────

    [Fact]
    public async Task Profile_ReturnsStoredCreatedAt()
    {
        var expectedCreatedAt = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Alex Smith",
                Email = "alex@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                CreatedAt = expectedCreatedAt,
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

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedCreatedAt, result.Data!.CreatedAt);
    }

    [Fact]
    public async Task Profile_ReturnsActiveRoles_NotHardCoded()
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Staff Member",
                Email = "staff@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole
                    {
                        RoleId = 2,
                        Role = new ApplicationRole { Id = 2, RoleName = "Staff" }
                    }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Contains("Staff", result.Data!.Roles);
        Assert.DoesNotContain("Member", result.Data.Roles);
    }

    [Fact]
    public async Task Profile_Ownership_EachUserSeesOnlyTheirOwnData()
    {
        var userA = new ApplicationUser
        {
            UserId = "user-A",
            GymId = 1,
            FullName = "User Alpha",
            Email = "alpha@example.com",
            PhoneNumber = "0911111111",
            PasswordHash = "Pass@123",
            AccountStatus = AccountStatus.Active,
            UserRoles =
            [
                new ApplicationUserRole { RoleId = 1, Role = new ApplicationRole { Id = 1, RoleName = "Customer" } }
            ]
        };
        var userB = new ApplicationUser
        {
            UserId = "user-B",
            GymId = 1,
            FullName = "User Beta",
            Email = "beta@example.com",
            PhoneNumber = "0922222222",
            PasswordHash = "Pass@123",
            AccountStatus = AccountStatus.Active,
            UserRoles =
            [
                new ApplicationUserRole { RoleId = 2, Role = new ApplicationRole { Id = 2, RoleName = "Staff" } }
            ]
        };
        var repository = new MultiUserRepositoryFake([userA, userB]);
        var service = CreateService(repository);

        var resultA = await service.GetMyProfileAsync("user-A");
        var resultB = await service.GetMyProfileAsync("user-B");
        var resultX = await service.GetMyProfileAsync("user-X");

        Assert.True(resultA.IsSuccess);
        Assert.Equal("User Alpha", resultA.Data!.FullName);
        Assert.Equal("user-A", resultA.Data.UserId);

        Assert.True(resultB.IsSuccess);
        Assert.Equal("User Beta", resultB.Data!.FullName);
        Assert.Equal("user-B", resultB.Data.UserId);

        Assert.False(resultX.IsSuccess);
        Assert.Equal(404, resultX.StatusCode);
    }

    [Fact]
    public async Task Profile_UpdatePersistsRealValues()
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Old Name",
                Email = "user@example.com",
                PhoneNumber = "0900000000",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole { RoleId = 1, Role = new ApplicationRole { Id = 1, RoleName = "Customer" } }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.UpdateMyProfileAsync(
            "user-1",
            new UpdateUserProfileRequest("New Name", "0911111111"));

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Data!.FullName);
        Assert.Equal("0911111111", result.Data.PhoneNumber);
        Assert.Equal("New Name", repository.ExistingUser.FullName);
        Assert.Equal("0911111111", repository.ExistingUser.PhoneNumber);

        var reloaded = await service.GetMyProfileAsync("user-1");
        Assert.True(reloaded.IsSuccess);
        Assert.Equal("New Name", reloaded.Data!.FullName);
        Assert.Equal("0911111111", reloaded.Data.PhoneNumber);
    }

    [Theory]
    [InlineData(AccountStatus.Active, "Active")]
    [InlineData(AccountStatus.Locked, "Locked")]
    public async Task Profile_AccountStatusMapsToString(AccountStatus status, string expected)
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
                PasswordHash = "Pass@123",
                AccountStatus = status,
                UserRoles =
                [
                    new ApplicationUserRole { RoleId = 1, Role = new ApplicationRole { Id = 1, RoleName = "Customer" } }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Data!.AccountStatus);
    }

    [Fact]
    public async Task Profile_MultipleRoles_AllReturnedInResponse()
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Multi-Role User",
                Email = "multi@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole { RoleId = 2, Role = new ApplicationRole { Id = 2, RoleName = "Staff" } },
                    new ApplicationUserRole { RoleId = 3, Role = new ApplicationRole { Id = 3, RoleName = "Manager" } }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Contains("Staff", result.Data!.Roles);
        Assert.Contains("Manager", result.Data.Roles);
    }

    [Fact]
    public async Task Profile_ExcludesSoftDeletedUserRolesAndSoftDeletedRoles()
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Role Filter User",
                Email = "roles@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole
                    {
                        RoleId = 1,
                        IsDeleted = true,
                        Role = new ApplicationRole { Id = 1, RoleName = "Customer", IsDeleted = false }
                    },
                    new ApplicationUserRole
                    {
                        RoleId = 2,
                        IsDeleted = false,
                        Role = new ApplicationRole { Id = 2, RoleName = "Staff", IsDeleted = false }
                    },
                    new ApplicationUserRole
                    {
                        RoleId = 3,
                        IsDeleted = false,
                        Role = new ApplicationRole { Id = 3, RoleName = "Manager", IsDeleted = true }
                    }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "Staff" }, result.Data!.Roles);
    }

    [Fact]
    public async Task Profile_DateOfBirth_MapsFromActivePhysicalProfile()
    {
        var expectedDateOfBirth = new DateOnly(1996, 8, 21);
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "Member With Profile",
                Email = "member@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                PhysicalProfile = new PhysicalProfile
                {
                    MemberUserId = "user-1",
                    DateOfBirth = expectedDateOfBirth,
                    IsDeleted = false
                },
                UserRoles =
                [
                    new ApplicationUserRole
                    {
                        RoleId = 1,
                        Role = new ApplicationRole { Id = 1, RoleName = "Member" }
                    }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedDateOfBirth, result.Data!.DateOfBirth);
    }

    [Fact]
    public void UpdateProfileValidatorChecksDateOfBirth()
    {
        var validator = new UpdateUserProfileRequestValidator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        Assert.True(validator.Validate(new UpdateUserProfileRequest("Alex Smith", "0912345678", null, new DateOnly(1998, 5, 10))).IsValid);
        Assert.True(validator.Validate(new UpdateUserProfileRequest("Alex Smith", "0912345678", null, null)).IsValid);
        Assert.False(validator.Validate(new UpdateUserProfileRequest("Alex Smith", "0912345678", null, today)).IsValid);
        Assert.False(validator.Validate(new UpdateUserProfileRequest("Alex Smith", "0912345678", null, today.AddDays(1))).IsValid);
        Assert.False(validator.Validate(new UpdateUserProfileRequest("Alex Smith", "0912345678", null, new DateOnly(1899, 12, 31))).IsValid);
    }

    [Fact]
    public async Task Profile_UpdateDateOfBirth_CreatesAndUpdatesPhysicalProfileWithoutLosingOtherFields()
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
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                UserRoles =
                [
                    new ApplicationUserRole { RoleId = 1, Role = new ApplicationRole { Id = 1, RoleName = "Member" } }
                ]
            }
        };
        var service = CreateService(repository);

        var dob1 = new DateOnly(1998, 8, 21);
        var created = await service.UpdateMyProfileAsync("user-1", new UpdateUserProfileRequest("Alex Smith", "0912345678", null, dob1));
        Assert.True(created.IsSuccess);
        Assert.Equal(dob1, created.Data!.DateOfBirth);
        Assert.NotNull(repository.ExistingUser.PhysicalProfile);

        repository.ExistingUser.PhysicalProfile!.WeightKg = 70.5m;
        var dob2 = new DateOnly(1999, 2, 14);
        var updated = await service.UpdateMyProfileAsync("user-1", new UpdateUserProfileRequest("Alex Smith", "0912345678", null, dob2));
        Assert.True(updated.IsSuccess);
        Assert.Equal(dob2, updated.Data!.DateOfBirth);
        Assert.Equal(70.5m, repository.ExistingUser.PhysicalProfile.WeightKg);

        var cleared = await service.UpdateMyProfileAsync("user-1", new UpdateUserProfileRequest("Alex Smith", "0912345678", null, null));
        Assert.True(cleared.IsSuccess);
        Assert.Null(cleared.Data!.DateOfBirth);
        Assert.Equal(70.5m, repository.ExistingUser.PhysicalProfile.WeightKg);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_DateOfBirth_IsNullWhenPhysicalProfileAbsentOrSoftDeleted(bool hasSoftDeletedProfile)
    {
        var repository = new UserAdministrationRepositoryFake
        {
            ExistingUser = new ApplicationUser
            {
                UserId = "user-1",
                GymId = 1,
                FullName = "No Active Physical Profile",
                Email = "nodob@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = "Pass@123",
                AccountStatus = AccountStatus.Active,
                PhysicalProfile = hasSoftDeletedProfile
                    ? new PhysicalProfile
                    {
                        MemberUserId = "user-1",
                        DateOfBirth = new DateOnly(1996, 8, 21),
                        IsDeleted = true
                    }
                    : null,
                UserRoles =
                [
                    new ApplicationUserRole
                    {
                        RoleId = 1,
                        Role = new ApplicationRole { Id = 1, RoleName = "Staff" }
                    }
                ]
            }
        };
        var service = CreateService(repository);

        var result = await service.GetMyProfileAsync("user-1");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.DateOfBirth);
    }

    // ── Helper fake that serves multiple users by ID ──────────────────────────

    private sealed class MultiUserRepositoryFake(IReadOnlyList<ApplicationUser> users)
        : IUserAdministrationRepository
    {
        private readonly IReadOnlyList<ApplicationUser> _users = users;
        private readonly ApplicationRole _customerRole = new() { Id = 1, RoleName = "Customer" };

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> PhoneNumberExistsAsync(string phoneNumber, string? excludingUserId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Gym?>(new Gym { Id = 1 });

        public Task<ApplicationRole?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationRole?>(roleName == "Customer" ? _customerRole : null);

        public Task<IReadOnlyList<ApplicationRole>> GetRolesByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRole>>([]);

        public Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            var user = _users.FirstOrDefault(u => u.UserId == userId);
            return Task.FromResult(user);
        }

        public Task<ApplicationUser?> GetUserByPasswordSetupTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.FirstOrDefault(u => u.PasswordSetupTokenHash == tokenHash));

        public Task<ApplicationUser?> FindPendingCustomerAsync(string? canonicalEmail, string? canonicalPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(string? role, AccountStatus? accountStatus, string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<ApplicationUser>, int)>(([], 0));

        public Task<Result<T>> ExecuteProvisioningAsync<T>(Func<Task<Result<T>>> action, CancellationToken cancellationToken = default) =>
            action();

        public Task CreateUserAsync(ApplicationUser user, AuditLog? auditLog = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result<T>> ExecuteCustomerSetupResendAsync<T>(string userId, Func<Task<Result<T>>> action, CancellationToken cancellationToken = default) =>
            action();

        public Task<bool> TryRotateCustomerSetupTokenAsync(string userId, string expectedEmail, string tokenHash, DateTime expiresAt, DateTime now, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task ClearPasswordSetupTokenAsync(string userId, string tokenHash, DateTime now, AuditLog auditLog, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveSetupAuditAsync(IReadOnlyCollection<AuditLog> auditLogs, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryCompletePasswordSetupAsync(string userId, string tokenHash, string newPasswordHash, AuditLog auditLog, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task ReplaceUserRolesAsync(ApplicationUser user, IReadOnlyCollection<int> roleIds, AuditLog auditLog, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AccountStatus?> GetStatusBeforeLatestLockAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountStatus?>(null);

        public Task<bool> TryUpdateAccountStatusAsync(ApplicationUser user, AccountStatus status, bool revokeActiveRefreshTokens, AuditLog auditLog, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Result<bool>> UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));
    }

    // ── Existing mapster test ─────────────────────────────────────────────────

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

    private sealed class EmailServiceFake : IEmailService
    {
        public bool IsConfigured { get; set; } = true;
        public bool ThrowOnSend { get; set; }
        public List<(string Recipient, string Subject, string Body)> SentMessages { get; } = [];

        public Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
                throw new InvalidOperationException("Simulated SMTP outage.");

            SentMessages.Add((recipient, subject, textBody));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset CurrentUtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => CurrentUtcNow;
    }

    private sealed class UserAdministrationRepositoryFake : IUserAdministrationRepository
    {
        public int GetUserByIdCalls { get; private set; }
        public bool EmailAlreadyExists { get; set; }
        public bool PhoneAlreadyExists { get; set; }
        public ApplicationUser? ExistingUser { get; init; }
        public ApplicationUser? CreatedUser { get; private set; }
        public AuditLog? LastAuditLog { get; private set; }
        private readonly ApplicationRole _customerRole = new() { Id = 1, RoleName = "Customer" };

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmailAlreadyExists);

        public Task<bool> PhoneNumberExistsAsync(
            string phoneNumber,
            string? excludingUserId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PhoneAlreadyExists);

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

        public Task<ApplicationUser?> GetUserByPasswordSetupTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
        {
            var candidate = CreatedUser ?? ExistingUser;
            return Task.FromResult(candidate?.PasswordSetupTokenHash == tokenHash ? candidate : null);
        }

        public Task<ApplicationUser?> FindPendingCustomerAsync(
            string? canonicalEmail,
            string? canonicalPhone,
            CancellationToken cancellationToken = default) => Task.FromResult<ApplicationUser?>(null);

        public Task<(IReadOnlyList<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
            string? role,
            AccountStatus? accountStatus,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<ApplicationUser>, int)>(([], 0));

        public Task<Result<T>> ExecuteProvisioningAsync<T>(
            Func<Task<Result<T>>> action,
            CancellationToken cancellationToken = default) =>
            action();

        public Task CreateUserAsync(
            ApplicationUser user,
            AuditLog? auditLog = null,
            CancellationToken cancellationToken = default)
        {
            CreatedUser = user;
            LastAuditLog = auditLog;
            return Task.CompletedTask;
        }

        public Task<Result<T>> ExecuteCustomerSetupResendAsync<T>(
            string userId,
            Func<Task<Result<T>>> action,
            CancellationToken cancellationToken = default) => action();

        public Task<bool> TryRotateCustomerSetupTokenAsync(
            string userId,
            string expectedEmail,
            string tokenHash,
            DateTime expiresAt,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            var candidate = CreatedUser ?? ExistingUser;
            if (candidate is null || candidate.UserId != userId || candidate.AccountStatus != AccountStatus.PendingVerification)
                return Task.FromResult(false);

            candidate.PasswordSetupTokenHash = tokenHash;
            candidate.PasswordSetupExpiresAt = expiresAt;
            candidate.UpdatedAt = now;
            return Task.FromResult(true);
        }

        public Task ClearPasswordSetupTokenAsync(
            string userId,
            string tokenHash,
            DateTime now,
            AuditLog auditLog,
            CancellationToken cancellationToken = default)
        {
            var candidate = CreatedUser ?? ExistingUser;
            if (candidate is not null && candidate.UserId == userId && candidate.PasswordSetupTokenHash == tokenHash)
            {
                candidate.PasswordSetupTokenHash = null;
                candidate.PasswordSetupExpiresAt = null;
                candidate.UpdatedAt = now;
            }
            return Task.CompletedTask;
        }

        public Task SaveSetupAuditAsync(
            IReadOnlyCollection<AuditLog> auditLogs, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryCompletePasswordSetupAsync(
            string userId,
            string tokenHash,
            string newPasswordHash,
            AuditLog auditLog,
            CancellationToken cancellationToken = default)
        {
            var now = auditLog.OccurredAt;
            var candidate = CreatedUser ?? ExistingUser;
            if (candidate is null || candidate.UserId != userId || candidate.PasswordSetupTokenHash != tokenHash ||
                !candidate.PasswordSetupExpiresAt.HasValue || candidate.PasswordSetupExpiresAt.Value <= now ||
                candidate.AccountStatus != AccountStatus.PendingVerification)
                return Task.FromResult(false);

            candidate.PasswordHash = newPasswordHash;
            candidate.PasswordSetupTokenHash = null;
            candidate.PasswordSetupExpiresAt = null;
            candidate.AccountStatus = AccountStatus.Active;
            candidate.UpdatedAt = now;
            LastAuditLog = auditLog;
            return Task.FromResult(true);
        }

        public Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task ReplaceUserRolesAsync(
            ApplicationUser user,
            IReadOnlyCollection<int> roleIds,
            AuditLog auditLog,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AccountStatus?> GetStatusBeforeLatestLockAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountStatus?>(null);

        public Task<bool> TryUpdateAccountStatusAsync(
            ApplicationUser user,
            AccountStatus status,
            bool revokeActiveRefreshTokens,
            AuditLog auditLog,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Result<bool>> UpdateUserAsync(ApplicationUser user, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));
    }
}
