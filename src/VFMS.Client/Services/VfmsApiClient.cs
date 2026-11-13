using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Auth;

namespace VFMS.Client.Services;

/// <summary>
/// Calls authenticated VFMS.API endpoints with the signed-in user's access token.
/// Failures surface as <see cref="ApiException"/> carrying the server message and field errors.
/// </summary>
public sealed class VfmsApiClient(HttpClient http, AuthSession session)
{
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
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                       ?? throw new ApiException("Máy chủ chưa trả về dữ liệu. Vui lòng thử lại.");

            throw await ToExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("Máy chủ phản hồi quá lâu. Vui lòng thử lại.");
        }
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again to continue. (MSG-11)",
            HttpStatusCode.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
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
                code = root.TryGetProperty("errorCode", out var c) ? c.GetString() : null;
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
        catch (JsonException) { /* non-JSON error page */ }
        return new ApiException(message, response.StatusCode, code, errors);
    }
}

public sealed class ApiException(string message, HttpStatusCode? statusCode = null, string? errorCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
