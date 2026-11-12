using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;

namespace VFMS.Client.Services;

/// <summary>
/// HTTP client for the Module 6 - Competitions endpoints.
/// The access token comes from <see cref="AuthSession"/> so the client follows the same refresh flow.
/// </summary>
public sealed class CompetitionApiClient(HttpClient http, AuthSession session)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // UC-56 View Competitions
    public async Task<PagedResponse<CompetitionResponse>> GetCompetitionsAsync(
        CompetitionListRequest request, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Status)) query.Add($"status={Uri.EscapeDataString(request.Status)}");
        if (!string.IsNullOrWhiteSpace(request.Search)) query.Add($"search={Uri.EscapeDataString(request.Search)}");
        query.Add($"page={request.Page}");
        query.Add($"pageSize={request.PageSize}");

        return await GetAsync<PagedResponse<CompetitionResponse>>(
            $"api/competitions?{string.Join('&', query)}", cancellationToken);
    }

    public Task<CompetitionResponse> GetCompetitionAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<CompetitionResponse>($"api/competitions/{id}", cancellationToken);

    public async Task<IReadOnlyCollection<CompetitionLeaderboardEntry>> GetLeaderboardAsync(
        int id, CancellationToken cancellationToken = default) =>
        await GetAsync<List<CompetitionLeaderboardEntry>>($"api/competitions/{id}/leaderboard", cancellationToken);

    // UC-57 Registration, UC-58 History
    public Task<CompetitionParticipantResponse> RegisterAsync(int competitionId, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionParticipantResponse>($"api/competitions/{competitionId}/register", null, cancellationToken);

    public Task<PagedResponse<CompetitionParticipantResponse>> GetMyHistoryAsync(
        CompetitionParticipantListRequest request, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (request.CompetitionId is { } competitionId) query.Add($"competitionId={competitionId}");
        if (!string.IsNullOrWhiteSpace(request.Status)) query.Add($"status={Uri.EscapeDataString(request.Status)}");
        query.Add($"page={request.Page}");
        query.Add($"pageSize={request.PageSize}");
        return GetAsync<PagedResponse<CompetitionParticipantResponse>>(
            $"api/competitions/my/history?{string.Join('&', query)}", cancellationToken);
    }

    // UC-59 Manage Competitions
    public Task<CompetitionResponse> CreateAsync(CompetitionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionResponse>("api/competitions", request, cancellationToken);

    public Task<CompetitionResponse> UpdateAsync(int id, CompetitionRequest request, CancellationToken cancellationToken = default) =>
        PutAsync<CompetitionResponse>($"api/competitions/{id}", request, cancellationToken);

    public Task<CompetitionResponse> PublishAsync(int id, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionResponse>($"api/competitions/{id}/publish", null, cancellationToken);

    public Task<CompetitionResponse> CancelAsync(int id, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionResponse>($"api/competitions/{id}/cancel", null, cancellationToken);

    // UC-60 Manage Competition Participants
    public Task<PagedResponse<CompetitionParticipantResponse>> GetParticipantsAsync(
        CompetitionParticipantListRequest request, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (request.CompetitionId is { } competitionId) query.Add($"competitionId={competitionId}");
        if (!string.IsNullOrWhiteSpace(request.Status)) query.Add($"status={Uri.EscapeDataString(request.Status)}");
        if (!string.IsNullOrWhiteSpace(request.Search)) query.Add($"search={Uri.EscapeDataString(request.Search)}");
        query.Add($"page={request.Page}");
        query.Add($"pageSize={request.PageSize}");
        return GetAsync<PagedResponse<CompetitionParticipantResponse>>(
            $"api/competitions/participants?{string.Join('&', query)}", cancellationToken);
    }

    public Task<CompetitionParticipantResponse> DisqualifyAsync(int joinId, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionParticipantResponse>($"api/competitions/participants/{joinId}/disqualify", null, cancellationToken);

    public Task<CompetitionParticipantResponse> ReinstateAsync(int joinId, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionParticipantResponse>($"api/competitions/participants/{joinId}/reinstate", null, cancellationToken);

    // UC-61 and UC-62 Results
    public Task<CompetitionParticipantResponse> RecordResultAsync(
        int joinId, RecordCompetitionResultRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionParticipantResponse>($"api/competitions/participants/{joinId}/result", request, cancellationToken);

    public Task<CompetitionParticipantResponse> ConfirmResultAsync(
        ConfirmCompetitionResultRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CompetitionParticipantResponse>("api/competitions/results/confirm", request, cancellationToken);

    public Task<int> ConfirmAllResultsAsync(int competitionId, CancellationToken cancellationToken = default) =>
        PostAsync<int>($"api/competitions/{competitionId}/results/confirm-all", null, cancellationToken);

    // Transport
    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T> PutAsync<T>(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, path);
        if (await session.GetAccessTokenAsync() is { } token)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            message.Content = JsonContent.Create(body, options: JsonOptions);

        var response = await http.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        var detail = await ReadProblemAsync(response, cancellationToken);
        var statusCode = response.StatusCode;
        response.Dispose();
        throw new CompetitionApiException(detail, statusCode);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new CompetitionApiException("May chu khong tra ve du lieu.", response.StatusCode);
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return "Vui lòng đăng nhập để sử dụng chức năng cuộc thi.";
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return "Tài khoản của bạn không có quyền thực hiện thao tác này.";

        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString() ?? "Khong the hoan tat yeu cau.";
            if (root.TryGetProperty("errorMessage", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "Khong the hoan tat yeu cau.";
        }
        catch (JsonException) { /* The server may return a non-JSON error page. */ }
        return "Khong the hoan tat yeu cau. Vui long thu lai.";
    }
}

public sealed class CompetitionApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
