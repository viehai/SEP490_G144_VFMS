using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Contracts.Trainers;
using VFMS.Contracts.Users;

namespace VFMS.Client.Services;

public sealed class TrainerApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string Root = "api/trainers";

    public Task<PagedResponse<TrainerProfileResponse>> SearchAsync(TrainerSearch filter) =>
        Send<PagedResponse<TrainerProfileResponse>>(HttpMethod.Get, Root + Query(
            ("Search", filter.Search), ("TrainerRole", filter.TrainerRole), ("Gender", filter.Gender),
            ("MinimumRating", filter.MinimumRating?.ToString(CultureInfo.InvariantCulture)),
            ("Page", filter.Page.ToString(CultureInfo.InvariantCulture)), ("Level", filter.Level),
            ("AcceptingNewMembers", filter.AcceptingNewMembers?.ToString())));

    public Task<TrainerPublicDetail> DetailAsync(int id, int reviewPage = 1) =>
        Send<TrainerPublicDetail>(HttpMethod.Get, $"{Root}/{id}?reviewPage={reviewPage}", optionalAuth: true);

    public Task<TrainerRecruitmentStatus> RecruitmentStatusAsync() =>
        Send<TrainerRecruitmentStatus>(HttpMethod.Get, $"{Root}/recruitment-status");

    public Task<TrainerRecruitmentStatus> SetRecruitmentStatusAsync(bool accepting) =>
        Send<TrainerRecruitmentStatus>(HttpMethod.Put, $"{Root}/recruitment-status",
            new SetTrainerRecruitmentStatusRequest(accepting), auth: true);

    public async Task<TrainerApplicationReceipt> SubmitAsync(SubmitTrainerApplicationRequest request)
    {
        try
        {
            return await Send<TrainerApplicationReceipt>(HttpMethod.Post, $"{Root}/applications", request,
                auth: request.ApplicationType == "ProfileUpdate", optionalAuth: true);
        }
        catch (AuthApiException ex) when (ex.ErrorCode is "RECRUITMENT_COOLDOWN" or "TRAINER_PROFILE_UPDATE_COOLDOWN")
        {
            const string prefix = "You can submit again after ";
            const string vietnamesePrefix = "Bạn có thể nộp lại hồ sơ sau ";
            var message = "Vui lòng chờ đủ 10 phút kể từ lần nộp trước rồi thử lại.";
            var matchedPrefix = ex.Message.StartsWith(vietnamesePrefix, StringComparison.Ordinal) ? vietnamesePrefix : prefix;
            if (ex.Message.StartsWith(matchedPrefix, StringComparison.Ordinal) &&
                DateTimeOffset.TryParse(ex.Message[matchedPrefix.Length..].TrimEnd('.'),
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
            {
                var vietnamTime = retryAt.ToOffset(TimeSpan.FromHours(7))
                    .ToString("HH:mm:ss 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
                message = $"Bạn có thể nộp lại hồ sơ sau {vietnamTime} (giờ Việt Nam).";
            }

            throw new AuthApiException(message, ex.StatusCode, ex.Errors, ex.ErrorCode);
        }
    }

    public Task<PagedResponse<TrainerApplicationResponse>> ApplicationsAsync(TrainerApplicationFilter filter, bool mine = false) =>
        Send<PagedResponse<TrainerApplicationResponse>>(HttpMethod.Get, $"{Root}/{(mine ? "me/" : "")}applications" + Query(
            ("Status", filter.Status), ("TrainerRole", filter.TrainerRole), ("ApplicationType", filter.ApplicationType),
            ("TrackingCode", filter.TrackingCode), ("Name", filter.Name),
            ("From", filter.From?.ToString("O", CultureInfo.InvariantCulture)),
            ("To", filter.To?.ToString("O", CultureInfo.InvariantCulture)),
            ("Page", filter.Page.ToString(CultureInfo.InvariantCulture))), auth: true);

    /// <summary>UC 3.1: locked account details for a signed-in Customer or Member; fails for guests and other roles.</summary>
    public Task<TrainerApplicantPrefill> ApplicantPrefillAsync() =>
        Send<TrainerApplicantPrefill>(HttpMethod.Get, $"{Root}/applicant-prefill", auth: true);

    /// <summary>UC 3.1: public status lookup by tracking code.</summary>
    public Task<TrainerApplicationTracking> TrackAsync(string trackingCode) =>
        Send<TrainerApplicationTracking>(HttpMethod.Get, $"{Root}/applications/track/{Uri.EscapeDataString(trackingCode.Trim())}");

    public Task<TrainerApplicationResponse> ApplicationAsync(int id) =>
        Send<TrainerApplicationResponse>(HttpMethod.Get, $"{Root}/applications/{id}", auth: true);

    public Task<TrainerApplicationResponse> ReviewAsync(int id, ReviewTrainerApplicationRequest request) =>
        Send<TrainerApplicationResponse>(HttpMethod.Post, $"{Root}/applications/{id}/review", request, auth: true);

    public Task<bool> ResendAsync(int id) =>
        Send<bool>(HttpMethod.Post, $"{Root}/applications/{id}/setup-email", auth: true);

    public Task<bool> SetupAsync(TrainerSetupRequest request) =>
        Send<bool>(HttpMethod.Post, $"{Root}/setup-password", request);

    public Task<TrainerPublicDetail> MineAsync() =>
        Send<TrainerPublicDetail>(HttpMethod.Get, $"{Root}/me", auth: true);

    public Task<UserProfileResponse> AccountAsync() =>
        Send<UserProfileResponse>(HttpMethod.Get, "api/users/me", auth: true);

    public Task<bool> UpdateAsync(UpdateTrainerProfileRequest request) =>
        Send<bool>(HttpMethod.Put, $"{Root}/me", request, auth: true);

    public Task<TrainerReadinessStatus> SetReadinessAsync(bool accepting) =>
        Send<TrainerReadinessStatus>(HttpMethod.Put, $"{Root}/me/readiness",
            new SetTrainerReadinessRequest(accepting), auth: true);

    public Task<PTPackageResponse[]> PackagesAsync() =>
        Send<PTPackageResponse[]>(HttpMethod.Get, "api/pt/packages");

    public Task<PTContractResponse[]> ContractsAsync() =>
        Send<PTContractResponse[]>(HttpMethod.Get, "api/pt/contracts", auth: true);

    public string? PublicAsset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (path.StartsWith("/api/trainers/", StringComparison.Ordinal))
            return new Uri(http.BaseAddress!, path.TrimStart('/')).AbsoluteUri;
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri : null;
    }

    public async Task DownloadAsync(int applicationId, TrainerDocumentResponse file, IJSRuntime js)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Root}/applications/{applicationId}/documents/{Uri.EscapeDataString(file.Id)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await RequiredTokenAsync());
        try
        {
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new AuthApiException("Không thể tải chứng chỉ. Vui lòng kiểm tra quyền truy cập.", response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reference = new DotNetStreamReference(stream, leaveOpen: true);
            await js.InvokeVoidAsync("vfmsDownloadFile", file.FileName, reference);
        }
        catch (HttpRequestException)
        {
            throw new AuthApiException("Không kết nối được máy chủ để tải chứng chỉ.");
        }
        catch (OperationCanceledException)
        {
            throw new AuthApiException("Máy chủ phản hồi quá lâu khi tải chứng chỉ.");
        }
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body = null,
        bool auth = false, bool optionalAuth = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = System.Net.Http.Json.JsonContent.Create(body, body.GetType());
        var token = auth ? await RequiredTokenAsync() : optionalAuth ? await session.GetAccessTokenAsync() : null;
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await AuthApiClient.SendAsync<T>(http, request, CancellationToken.None);
    }

    private async Task<string> RequiredTokenAsync() => await session.GetAccessTokenAsync()
        ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);

    private static string Query(params (string Key, string? Value)[] fields) =>
        "?" + string.Join("&", fields.Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => $"{field.Key}={Uri.EscapeDataString(field.Value!)}"));

    public void Dispose() => http.Dispose();
}
