using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.Feedback;
using VFMS.Contracts.Consultations;

namespace VFMS.Client.Services;

/// <summary>General policies (UC-84), banned words (UC-85), business reports (UC-86) and audit logs (UC-87).</summary>
public sealed class SystemAdministrationApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string GymSettingsPath = "api/gym-settings";
    private const string ReportsPath = "api/business-reports";
    private const string BannedWordsPath = "api/admin/banned-words";
    private const string AuditLogsPath = "api/admin/audit-logs";
    private const string FeedbackPath = "api/feedback";
    private const string ConsultationsPath = "api/consultations";

    public Task<GymSettingsResponse> GetGymSettingsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<GymSettingsResponse>(HttpMethod.Get, GymSettingsPath, cancellationToken);

    public Task<GymSettingsResponse> UpdateGymSettingsAsync(GymSettingsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<GymSettingsResponse>(HttpMethod.Put, GymSettingsPath, cancellationToken, JsonContent.Create(request));

    public Task<PagedResponse<BusinessReportSummaryResponse>> GetReportsAsync(BusinessReportListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<BusinessReportSummaryResponse>>(HttpMethod.Get, WithQuery(ReportsPath,
            ("from", Date(request.From)), ("to", Date(request.To)), ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), cancellationToken);

    public Task<BusinessReportResponse> GetReportAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<BusinessReportResponse>(HttpMethod.Get, $"{ReportsPath}/{id}", cancellationToken);

    public Task<BusinessReportTrendResponse> GetReportTrendAsync(BusinessReportTrendRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BusinessReportTrendResponse>(HttpMethod.Get, WithQuery($"{ReportsPath}/trend",
            ("periodType", request.PeriodType), ("date", Date(request.Date)), ("count", Number(request.Count))), cancellationToken);

    public Task<BusinessReportResponse> GenerateReportAsync(GenerateBusinessReportRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BusinessReportResponse>(HttpMethod.Post, ReportsPath, cancellationToken, JsonContent.Create(request));

    public Task<PagedResponse<BannedWordResponse>> GetBannedWordsAsync(BannedWordListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<BannedWordResponse>>(HttpMethod.Get, WithQuery(BannedWordsPath,
            ("search", request.Search), ("isActive", request.IsActive is { } active ? (active ? "true" : "false") : null),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), cancellationToken);

    public Task<BannedWordResponse> CreateBannedWordAsync(BannedWordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BannedWordResponse>(HttpMethod.Post, BannedWordsPath, cancellationToken, JsonContent.Create(request));

    public Task<BannedWordResponse> UpdateBannedWordAsync(int id, BannedWordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BannedWordResponse>(HttpMethod.Put, $"{BannedWordsPath}/{id}", cancellationToken, JsonContent.Create(request));

    public Task<BannedWordResponse> SetBannedWordActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) =>
        SendAsync<BannedWordResponse>(HttpMethod.Patch, $"{BannedWordsPath}/{id}/{(isActive ? "activate" : "deactivate")}", cancellationToken);

    public Task<PagedResponse<AuditLogResponse>> GetAuditLogsAsync(AuditLogListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<AuditLogResponse>>(HttpMethod.Get, WithQuery(AuditLogsPath,
            ("from", request.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("to", request.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("actorUserId", request.ActorUserId), ("actionType", request.ActionType), ("entity", request.Entity),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), cancellationToken);

    public Task<AuditLogResponse> GetAuditLogAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<AuditLogResponse>(HttpMethod.Get, $"{AuditLogsPath}/{id}", cancellationToken);

    public Task<PagedResponse<FeedbackResponse>> GetFeedbackAsync(
        FeedbackListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<FeedbackResponse>>(HttpMethod.Get,
            WithQuery($"{FeedbackPath}/manager",
                ("search", request.Search), ("status", request.Status),
                ("rating", request.Rating?.ToString(CultureInfo.InvariantCulture)),
                ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))),
            cancellationToken);

    public Task<FeedbackResponse> SubmitFeedbackAsync(
        FeedbackSubmitRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FeedbackResponse>(HttpMethod.Post, FeedbackPath, cancellationToken,
            JsonContent.Create(request));

    public Task<IReadOnlyList<FeedbackTrainerResponse>> GetFeedbackTrainersAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<FeedbackTrainerResponse>>(HttpMethod.Get,
            $"{FeedbackPath}/trainers", cancellationToken);

    public Task<PagedResponse<FeedbackResponse>> GetMyFeedbackAsync(
        FeedbackListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<FeedbackResponse>>(HttpMethod.Get,
            WithQuery($"{FeedbackPath}/mine",
                ("status", request.Status), ("rating", request.Rating?.ToString(CultureInfo.InvariantCulture)),
                ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))),
            cancellationToken);

    public Task<FeedbackResponse> ApproveFeedbackAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<FeedbackResponse>(HttpMethod.Post, $"{FeedbackPath}/manager/{id}/approve", cancellationToken);

    public Task<FeedbackResponse> RejectFeedbackAsync(
        int id, FeedbackReplyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FeedbackResponse>(HttpMethod.Post, $"{FeedbackPath}/manager/{id}/reject", cancellationToken,
            JsonContent.Create(request));

    public Task<FeedbackResponse> ReplyFeedbackAsync(
        int id, FeedbackReplyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FeedbackResponse>(HttpMethod.Post, $"{FeedbackPath}/manager/{id}/reply", cancellationToken,
            JsonContent.Create(request));

    public Task<FeedbackResponse> SetFeedbackVisibilityAsync(
        int id, bool isVisible, CancellationToken cancellationToken = default) =>
        SendAsync<FeedbackResponse>(HttpMethod.Patch,
            $"{FeedbackPath}/manager/{id}/visibility?visible={isVisible.ToString().ToLowerInvariant()}",
            cancellationToken);

    public Task<PagedResponse<ConsultationConversationResponse>> GetConsultationsAsync(
        ConsultationListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<ConsultationConversationResponse>>(HttpMethod.Get,
            WithQuery(ConsultationsPath,
                ("search", request.Search), ("status", request.Status),
                ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))),
            cancellationToken);

    public Task<ConsultationConversationResponse> GetConsultationAsync(
        int id, CancellationToken cancellationToken = default) =>
        SendAsync<ConsultationConversationResponse>(HttpMethod.Get,
            $"{ConsultationsPath}/{id}", cancellationToken);

    public Task<ConsultationConversationResponse> CreateConsultationAsync(
        ConsultationCreateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ConsultationConversationResponse>(HttpMethod.Post,
            ConsultationsPath, cancellationToken, JsonContent.Create(request));

    public Task<ConsultationConversationResponse> SendConsultationMessageAsync(
        int id, ConsultationSendRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ConsultationConversationResponse>(HttpMethod.Post,
            $"{ConsultationsPath}/{id}/messages", cancellationToken, JsonContent.Create(request));

    public Task<ConsultationConversationResponse> CloseConsultationAsync(
        int id, CancellationToken cancellationToken = default) =>
        SendAsync<ConsultationConversationResponse>(HttpMethod.Post,
            $"{ConsultationsPath}/{id}/close", cancellationToken);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string WithQuery(string path, params (string Name, string? Value)[] parameters)
    {
        var query = parameters.Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!.Trim())}");
        return $"{path}?{string.Join('&', query)}";
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await AuthApiClient.SendAsync<T>(http, request, cancellationToken);
    }

    public void Dispose() => http.Dispose();
}
