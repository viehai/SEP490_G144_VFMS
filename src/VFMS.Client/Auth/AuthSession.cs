using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;

namespace VFMS.Client.Auth;

public sealed record StoredAuthSession(LoginResponse User, DateTimeOffset ExpiresAt, bool RememberMe);

public sealed class AuthSession(IJSRuntime js, AuthApiClient api) : AuthenticationStateProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StoredAuthSession? _session;
    private bool _loaded;
    private static AuthenticationState Anonymous => new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            if (await GetAccessTokenAsync() is null || _session is null) return Anonymous;
            var user = _session.User;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId), new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Email, user.Email)
            };
            claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "VFMS")));
        }
        catch (AuthApiException) { return Anonymous; }
    }

    public async Task SignInAsync(LoginRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var user = await api.LoginAsync(request);
            await SaveAsync(new(user, DateTimeOffset.UtcNow.AddSeconds(user.ExpiresIn), request.RememberMe));
        }
        finally { _gate.Release(); }
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loaded)
            {
                try
                {
                    var json = await js.InvokeAsync<string?>("vfmsAuthStorage.read");
                    if (!string.IsNullOrEmpty(json)) _session = JsonSerializer.Deserialize<StoredAuthSession>(json);
                    if (_session is not null && (_session.User is null || string.IsNullOrWhiteSpace(_session.User.AccessToken) ||
                        string.IsNullOrWhiteSpace(_session.User.RefreshToken) || _session.User.Roles is null))
                    {
                        _session = null;
                        await js.InvokeVoidAsync("vfmsAuthStorage.clear");
                    }
                }
                catch (JsonException) { await js.InvokeVoidAsync("vfmsAuthStorage.clear"); }
                _loaded = true;
            }
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
                    await js.InvokeVoidAsync("vfmsAuthStorage.clear");
                    _session = null;
                    return null;
                }
            }
            return _session.User.AccessToken;
        }
        finally { _gate.Release(); }
    }

    private async Task SaveAsync(StoredAuthSession session)
    {
        if (string.IsNullOrWhiteSpace(session.User.AccessToken) || string.IsNullOrWhiteSpace(session.User.RefreshToken) ||
            string.IsNullOrWhiteSpace(session.User.UserId) || string.IsNullOrWhiteSpace(session.User.FullName) ||
            string.IsNullOrWhiteSpace(session.User.Email) || session.User.Roles is null || session.User.ExpiresIn <= 0)
            throw new AuthApiException("Phản hồi đăng nhập không hợp lệ. Vui lòng thử lại sau.");
        await js.InvokeVoidAsync("vfmsAuthStorage.write", JsonSerializer.Serialize(session), session.RememberMe);
        _session = session;
        _loaded = true;
    }
}
