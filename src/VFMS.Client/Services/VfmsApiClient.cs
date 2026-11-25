using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VFMS.Client.Auth;
using VFMS.Contracts.Users;

namespace VFMS.Client.Services;

/// <summary>
/// Calls authenticated VFMS.API endpoints with the signed-in user's access token.
/// Failures surface as <see cref="ApiException"/> carrying the server message, field errors and the failing call,
/// and are logged once here, so pages only need to show <c>ex.Message</c>.
/// </summary>
public sealed class VfmsApiClient(HttpClient http, AuthSession session, ILogger<VfmsApiClient>? logger = null)
{
    public Task<WalkInCustomerResponse> CreateWalkInCustomerAsync(
        WalkInCustomerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<WalkInCustomerResponse>("api/users/walk-in-customer", request, cancellationToken);

    public Task<WalkInCustomerResponse> FindPendingCustomerAsync(
        FindPendingCustomerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<WalkInCustomerResponse>("api/users/find-pending-customer", request, cancellationToken);

    public Task<ResendSetupEmailResponse> ResendCustomerSetupEmailAsync(
        string userId, CancellationToken cancellationToken = default) =>
        PostAsync<ResendSetupEmailResponse>(
            $"api/users/{Uri.EscapeDataString(userId)}/resend-setup-email", null, cancellationToken);

    public Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    public Task<T> PostAsync<T>(string path, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, body ?? new { }, cancellationToken);

    public Task<T> PutAsync<T>(string path, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, cancellationToken);

    public Task<T> PatchAsync<T>(string path, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Patch, path, body, cancellationToken);

    public Task<T> DeleteAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Delete, path, null, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            if (await session.GetAccessTokenAsync() is { } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw Logged(await ToExceptionAsync(method, path, response, cancellationToken));

            T? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            }
            // A changed DTO or content type on the server breaks here, not in the page: name the endpoint and the type.
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw Logged(new ApiException("Phản hồi từ máy chủ không hợp lệ. Vui lòng thử lại sau.", response.StatusCode,
                    "INVALID_RESPONSE", method: method.Method, path: path, inner: ex, responseType: typeof(T).Name));
            }
            return result ?? throw Logged(new ApiException("Máy chủ chưa trả về dữ liệu. Vui lòng thử lại.", response.StatusCode,
                "EMPTY_RESPONSE", method: method.Method, path: path, responseType: typeof(T).Name));
        }
        catch (HttpRequestException ex)
        {
            throw Logged(new ApiException("Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.", null,
                "NETWORK_ERROR", method: method.Method, path: path, inner: ex));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Logged(new ApiException("Máy chủ phản hồi quá lâu. Vui lòng thử lại.", null,
                "TIMEOUT", method: method.Method, path: path, inner: ex));
        }
    }

    // One log line per failed call, so the browser console names the endpoint, status and code.
    // 4xx answers are expected rules (already checked in, late cancel): Warning. No answer, 5xx or a bad body: Error.
    private ApiException Logged(ApiException ex)
    {
        var level = ex.StatusCode is { } status && (int)status < 500 && ex.InnerException is null ? LogLevel.Warning : LogLevel.Error;
        logger?.Log(level, ex.InnerException, "{Failure}", ex.Describe());
        return ex;
    }

    private static async Task<ApiException> ToExceptionAsync(HttpMethod method, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again to continue. (MSG-11)",
            HttpStatusCode.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
            HttpStatusCode.TooManyRequests => "Bạn đã thao tác quá nhiều lần. Vui lòng đợi một lát rồi thử lại.",
            >= HttpStatusCode.InternalServerError => "Hệ thống tạm thời chưa sẵn sàng. Vui lòng thử lại sau.",
            _ => "Không thể hoàn tất yêu cầu. Vui lòng thử lại."
        };
        string? code = null;
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                // ToActionResult puts the code in the ProblemDetails "code" extension; "errorCode" is the older shape.
                code = root.TryGetProperty("code", out var c) || root.TryGetProperty("errorCode", out c) ? c.GetString() : null;
                if (root.TryGetProperty("errorMessage", out var m) && m.GetString() is { Length: > 0 } text)
                    message = text;
                else if (root.TryGetProperty("detail", out m) && m.GetString() is { Length: > 0 } detail)
                    message = detail;
                if (root.TryGetProperty("errors", out var validation) && validation.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in validation.EnumerateObject())
                        if (field.Value.ValueKind == JsonValueKind.Array)
                            errors[field.Name] = field.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray();
                    message = errors.Values.SelectMany(v => v).FirstOrDefault(v => v.Length > 0) ?? "Vui lòng kiểm tra lại thông tin đã nhập.";
                }
            }
        }
        catch (JsonException) { /* non-JSON error page (proxy, IIS): the status code still names the failure */ }
        return new ApiException(message, response.StatusCode, code, errors, method.Method, path);
    }
}

/// <summary>
/// A failed <see cref="VfmsApiClient"/> call. <see cref="Exception.Message"/> is the user-facing text;
/// <see cref="Method"/>, <see cref="Path"/>, <see cref="StatusCode"/> and <see cref="ErrorCode"/> say which call failed and how.
/// Client-side codes: NETWORK_ERROR, TIMEOUT, EMPTY_RESPONSE, INVALID_RESPONSE. Any other code came from the API.
/// </summary>
public sealed class ApiException(string message, HttpStatusCode? statusCode = null, string? errorCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null, string? method = null, string? path = null,
    Exception? inner = null, string? responseType = null) : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public string? Method { get; } = method;
    public string? Path { get; } = path;
    /// <summary>The DTO the page expected; set for INVALID_RESPONSE and EMPTY_RESPONSE.</summary>
    public string? ResponseType { get; } = responseType;

    /// <summary>For example "VFMS API POST api/yoga/bookings/5/cancel -> 409 Conflict LATE_CANCELLATION_CONFIRM: ...".</summary>
    public string Describe()
    {
        var status = StatusCode is { } s ? $"{(int)s} {s}" : "no response";
        var expected = ResponseType is null ? "" : $" (expected {ResponseType})";
        var cause = InnerException is null ? "" : $" [{InnerException.GetType().Name}: {InnerException.Message}]";
        return $"VFMS API {Method ?? "?"} {Path ?? "?"} -> {status} {ErrorCode ?? "-"}{expected}: {Message}{cause}";
    }

    public override string ToString() => $"{nameof(ApiException)}: {Describe()}{Environment.NewLine}{StackTrace}";
}
