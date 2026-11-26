using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;

namespace VFMS.Client.Auth;

public sealed record StoredAuthSession(LoginResponse User, DateTimeOffset ExpiresAt, bool RememberMe);

public sealed class AuthSession(IJSRuntime js, AuthApiClient api, MembershipCart? cart = null) : AuthenticationStateProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StoredAuthSession? _session;
    private bool _loaded;
    private DotNetObjectReference<AuthSession>? _browser;
    private static AuthenticationState Anonymous => new(new ClaimsPrincipal(new ClaimsIdentity()));

    public async Task WatchBrowserSessionAsync()
    {
        _browser = DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("vfmsAuthStorage.watch", _browser);
    }

    [JSInvokable]
    public async Task BrowserSessionChanged() => await GetAuthenticationStateAsync();

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            await GetAccessTokenAsync();
            return ToAuthenticationState(_session);
        }
        catch (AuthApiException) { return Anonymous; }
    }

    private static AuthenticationState ToAuthenticationState(StoredAuthSession? session)
    {
        if (session is null) return Anonymous;
        var user = session.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId), new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "VFMS")));
    }

    public async Task SignInAsync(LoginRequest request)
    {
        await WithSessionAsync(async () =>
        {
            var user = await api.LoginAsync(request);
            if (_session?.User.UserId != user.UserId) cart?.Clear();
            await SaveAsync(new(user, DateTimeOffset.UtcNow.AddSeconds(user.ExpiresIn), request.RememberMe));
            return true;
        });
    }

    public async Task UpdateFullNameAsync(string fullName)
    {
        var trimmed = fullName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;
        await WithSessionAsync(async () =>
        {
            if (_session is not null && !string.Equals(_session.User.FullName, trimmed, StringComparison.Ordinal))
            {
                await SaveAsync(_session with { User = _session.User with { FullName = trimmed } });
            }
            return true;
        });
    }

    public Task<string?> GetAccessTokenAsync() => WithSessionAsync(GetAccessTokenCoreAsync);

    public async Task SyncMemberRoleAsync(CancellationToken cancellationToken)
    {
        if (_loaded && (_session is null || !CanPromote(_session))) return;
        try
        {
            var token = await GetAccessTokenAsync();
            if (token is null || _session is not { } session || !CanPromote(session)) return;

            var profile = await api.GetProfileAsync(token, cancellationToken);
            if (profile.Roles.Contains("Member"))
                await RefreshMemberRoleAsync(session.User.UserId, cancellationToken);
        }
        catch (AuthApiException) { /* A temporary profile failure can be retried on the next check. */ }
    }

    public Task RefreshRolesAsync(string expectedUserId) => RefreshRolesAsync(expectedUserId, false, default);

    private Task RefreshRolesAsync(string? expectedUserId, bool promotionOnly, CancellationToken cancellationToken) =>
        WithSessionAsync(async () =>
        {
            if (_session is not { } session || (promotionOnly && !CanPromote(session)) ||
                (expectedUserId is not null && expectedUserId != session.User.UserId)) return false;

            var user = await api.RefreshAsync(session.User.RefreshToken);
            await SaveAsync(new(user, DateTimeOffset.UtcNow.AddSeconds(user.ExpiresIn), session.RememberMe));
            return true;
        }, cancellationToken);

    public async Task RefreshMemberRoleAsync(string? expectedUserId, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshRolesAsync(expectedUserId, true, cancellationToken);
        }
        catch (AuthApiException) { /* Payment or activation succeeded. The profile check will retry. */ }
    }

    private static bool CanPromote(StoredAuthSession session) =>
        session.User.Roles.Contains("Customer") && !session.User.Roles.Contains("Member");

    public Task<bool> SignOutAsync() => WithSessionAsync(async () =>
    {
        var serverConfirmed = true;
        try
        {
            var accessToken = await GetAccessTokenCoreAsync();
            if (accessToken is not null && _session is not null)
                await api.LogoutAsync(accessToken, _session.User.RefreshToken);
        }
        catch (AuthApiException)
        {
            // Local sign-out must still work when the API is unavailable.
            serverConfirmed = false;
        }
        finally
        {
            cart?.Clear();
            await js.InvokeVoidAsync("vfmsAuthStorage.clear");
            _session = null;
        }
        return serverConfirmed;
    });

    // Read and rotate under the same origin-wide lock: a second tab must use the newly issued refresh token.
    private async Task<T> WithSessionAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var locked = false;
        StoredAuthSession? before = _session;
        try
        {
            if (_browser is not null)
            {
                await js.InvokeVoidAsync("vfmsAuthStorage.lock");
                locked = true;
            }
            if (!_loaded || locked)
                _session = await ReadSessionAsync();
            if (!_loaded) before = _session;
            else if (before?.User.UserId != _session?.User.UserId)
            {
                // Discard the old account's page data and pending actions when another tab switches accounts.
                cart?.Clear();
                await js.InvokeVoidAsync("location.reload");
                throw new AuthApiException("Tài khoản đăng nhập đã thay đổi.");
            }
            _loaded = true;
            ct.ThrowIfCancellationRequested();
            return await action();
        }
        finally
        {
            try
            {
                if (locked) await js.InvokeVoidAsync("vfmsAuthStorage.unlock");
            }
            finally
            {
                _gate.Release();
                if (before?.User.UserId != _session?.User.UserId || before?.User.FullName != _session?.User.FullName ||
                    (before is not null && _session is not null && !before.User.Roles.ToHashSet().SetEquals(_session.User.Roles)))
                    NotifyAuthenticationStateChanged(Task.FromResult(ToAuthenticationState(_session)));
            }
        }
    }

    private async Task<StoredAuthSession?> ReadSessionAsync()
    {
        var json = await js.InvokeAsync<string?>("vfmsAuthStorage.read");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredAuthSession>(json);
            if (stored?.User is { Roles: not null } user && !string.IsNullOrWhiteSpace(user.UserId) &&
                !string.IsNullOrWhiteSpace(user.AccessToken) && !string.IsNullOrWhiteSpace(user.RefreshToken)) return stored;
        }
        catch (JsonException) { }
        await js.InvokeVoidAsync("vfmsAuthStorage.clear");
        return null;
    }

    private async Task<string?> GetAccessTokenCoreAsync()
    {
        if (_session is null) return null;
        if (_session.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            try
            {
                var user = await api.RefreshAsync(_session.User.RefreshToken);
                await SaveAsync(new(user, DateTimeOffset.UtcNow.AddSeconds(user.ExpiresIn), _session.RememberMe));
            }
            catch (AuthApiException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                cart?.Clear();
                await js.InvokeVoidAsync("vfmsAuthStorage.clear");
                _session = null;
                return null;
            }
        }
        return _session.User.AccessToken;
    }

    private async Task SaveAsync(StoredAuthSession session)
    {
        if (string.IsNullOrWhiteSpace(session.User.AccessToken) || string.IsNullOrWhiteSpace(session.User.RefreshToken) ||
            string.IsNullOrWhiteSpace(session.User.UserId) || string.IsNullOrWhiteSpace(session.User.FullName) ||
            string.IsNullOrWhiteSpace(session.User.Email) || session.User.Roles is null || session.User.ExpiresIn <= 0)
            throw new AuthApiException("Phản hồi đăng nhập không hợp lệ. Vui lòng thử lại sau.");
        await js.InvokeVoidAsync("vfmsAuthStorage.write", JsonSerializer.Serialize(session), session.RememberMe);
        _session = session;
    }

    public void Dispose() => _browser?.Dispose();
}
