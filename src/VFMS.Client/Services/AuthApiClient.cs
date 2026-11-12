using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Contracts.Auth;

namespace VFMS.Client.Services;

public sealed class AuthApiClient(HttpClient http) : IDisposable
{
    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<LoginRequest, LoginResponse>("api/auth/login", request, cancellationToken);

    public Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<RegisterRequest, RegisterResponse>("api/auth/register", request, cancellationToken);

    public Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        PostAsync<RefreshTokenRequest, LoginResponse>("api/auth/refresh", new(refreshToken), cancellationToken);

    public async Task LogoutAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        await PostAsync<LogoutRequest, JsonElement>("api/auth/logout", new(refreshToken), cancellationToken, accessToken);
    }

    public void Dispose() => http.Dispose();

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken,
        string? accessToken = null)
    {
        using var messageRequest = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
        if (accessToken is not null)
            messageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync<TResponse>(http, messageRequest, cancellationToken);
    }

    internal static async Task<TResponse> SendAsync<TResponse>(HttpClient http, HttpRequestMessage messageRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(messageRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
                    ?? throw new AuthApiException("Máy chủ chưa trả về dữ liệu. Vui lòng thử lại.");
            }

            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized when messageRequest.Headers.Authorization is not null => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
                HttpStatusCode.Forbidden when messageRequest.Headers.Authorization is not null => "Tài khoản của bạn không có quyền thực hiện thao tác này.",
                HttpStatusCode.TooManyRequests => "Bạn đã thử quá nhiều lần. Vui lòng đợi rồi thử lại.",
                _ => "Không thể hoàn tất yêu cầu. Vui lòng thử lại."
            };
            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string? code = null;
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                code = root.TryGetProperty("errorCode", out var errorCode) ? errorCode.GetString()
                    : root.TryGetProperty("code", out var problemCode) ? problemCode.GetString() : null;
                message = code switch
                {
                    "AUTH_INVALID_CREDENTIALS" => "Email, số điện thoại hoặc mật khẩu chưa đúng.",
                    "AUTH_EMAIL_EXISTS" => "Email này đã được đăng ký. Hãy đăng nhập hoặc sử dụng email khác.",
                    "AUTH_ACCOUNT_LOCKED" => "Tài khoản đang bị khóa. Vui lòng liên hệ lễ tân để được hỗ trợ.",
                    "AUTH_ACCOUNT_INACTIVE" or "AUTH_ACCOUNT_NOT_ACTIVE" => "Tài khoản chưa hoạt động. Vui lòng liên hệ lễ tân.",
                    "AUTH_PENDING_VERIFICATION" => "Tài khoản đang chờ xác minh. Vui lòng liên hệ lễ tân.",
                    "AUTH_INVALID_REFRESH_TOKEN" => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại. (MSG-11)",
                    "PAYMENT_UNAVAILABLE" => "VietQR hiện chưa khả dụng. Bạn có thể chọn thanh toán tại quầy.",
                    _ when (int)response.StatusCode >= 500 => "Hệ thống tạm thời chưa sẵn sàng. Vui lòng thử lại sau.",
                    _ => root.TryGetProperty("errorMessage", out var detail) ? detail.GetString() ?? message
                        : root.TryGetProperty("detail", out detail) ? detail.GetString() ?? message : message
                };
                if (root.TryGetProperty("errors", out var validation) && validation.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in validation.EnumerateObject())
                        if (field.Value.ValueKind == JsonValueKind.Array)
                            errors[field.Name] = field.Value.EnumerateArray().Select(value => value.GetString() ?? message).ToArray();
                    message = "Vui lòng kiểm tra lại thông tin đã nhập.";
                }
            }
            catch (JsonException) { /* A gateway may return a non-JSON error page. */ }
            throw new AuthApiException(message, response.StatusCode, errors, code);
        }
        catch (HttpRequestException)
        {
            throw new AuthApiException("Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AuthApiException("Máy chủ phản hồi quá lâu. Vui lòng thử lại.");
        }
        catch (JsonException)
        {
            throw new AuthApiException("Phản hồi từ máy chủ không hợp lệ. Vui lòng thử lại sau.");
        }
    }
}

public sealed class AuthApiException(string message, HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null, string? errorCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public string? ErrorCode { get; } = errorCode;
}
