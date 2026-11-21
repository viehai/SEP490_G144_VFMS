using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;

namespace VFMS.Client.Services;

public sealed class PersonalTrainingApiClient(HttpClient http, AuthSession session) : IDisposable
{
    public Task<PTWeeklySlot[]> WeeklyTemplateAsync(string trainer) =>
        Send<PTWeeklySlot[]>(HttpMethod.Get, $"trainers/{Uri.EscapeDataString(trainer)}/weekly-template", anonymous: true);
    public Task<PTSlotInput[]> AvailabilityAsync() => Send<PTSlotInput[]>(HttpMethod.Get, "availability");
    public Task<PTOperatingHours> OperatingHoursAsync() =>
        Send<PTOperatingHours>(HttpMethod.Get, "availability/operating-hours");
    public Task<bool> PublishAsync(PTPublishRequest request) => Send<bool>(HttpMethod.Put, "availability", request);

    public Task<PTPackageResponse[]> ManagerPackagesAsync() => Send<PTPackageResponse[]>(HttpMethod.Get, "manager/packages");
    public Task<bool> SetPackageSaleStatusAsync(int id, bool isActive) =>
        Send<bool>(HttpMethod.Patch, $"manager/packages/{id}/status", new PTPackageStatusRequest(isActive));
    public Task<PTPackageDeleteResponse> DeletePackageAsync(int id) =>
        Send<PTPackageDeleteResponse>(HttpMethod.Delete, $"manager/packages/{id}");
    public Task<PTPackageResponse> SavePackageAsync(int? id, PTPackageRequest request) =>
        id is { } package ? Send<PTPackageResponse>(HttpMethod.Put, $"manager/packages/{package}", request)
            : Send<PTPackageResponse>(HttpMethod.Post, "manager/packages", request);

    public Task<PTBookingPreview> PreviewAsync(PTBookingRequest request) =>
        Send<PTBookingPreview>(HttpMethod.Post, "schedule/preview", request);
    public Task<PTWeeklySlot[]> BookingSlotsAsync(PTBookingSlotsRequest request) =>
        Send<PTWeeklySlot[]>(HttpMethod.Post, "schedule/booking-slots", request);
    public Task<PTCheckoutResponse> HoldAsync(PTBookingRequest request) =>
        Send<PTCheckoutResponse>(HttpMethod.Post, "purchases", request);
    public Task<PTCheckoutResponse> StartPaymentAsync(string order, PTPaymentRequest request) =>
        Send<PTCheckoutResponse>(HttpMethod.Post, $"purchases/{Uri.EscapeDataString(order)}/payment", request);
    public Task<PTCheckoutResponse> ConfirmCashAsync(string order, PTCashConfirmationRequest request, CancellationToken ct) =>
        Send<PTCheckoutResponse>(HttpMethod.Post, $"purchases/{Uri.EscapeDataString(order)}/cash-confirmation", request, ct: ct);
    public Task<PTCheckoutResponse> CheckoutAsync(string order, CancellationToken ct = default) =>
        Send<PTCheckoutResponse>(HttpMethod.Get, $"purchases/{Uri.EscapeDataString(order)}", ct: ct);

    public Task<PTContractResponse[]> ContractsAsync(int? id = null) =>
        Send<PTContractResponse[]>(HttpMethod.Get, id is { } contract ? $"contracts/{contract}" : "contracts");
    public Task<PagedResponse<PTAssignedMember>> AssignedPageAsync(string search, bool historical, int page) =>
        Send<PagedResponse<PTAssignedMember>>(HttpMethod.Get,
            $"assigned-members/page?search={Uri.EscapeDataString(search)}&historical={(historical ? "true" : "false")}&page={page}");
    public Task<PagedResponse<PTContractResponse>> ContractPageAsync(int page, string? trainerId) =>
        Send<PagedResponse<PTContractResponse>>(HttpMethod.Get,
            $"contracts/page?page={page}" + (trainerId is null ? "" : $"&trainerId={Uri.EscapeDataString(trainerId)}"));
    public Task<PTTeachingSlot[]> TeachingScheduleAsync(DateOnly monday, CancellationToken ct = default) =>
        Send<PTTeachingSlot[]>(HttpMethod.Get, $"teaching-schedule?from={monday:yyyy-MM-dd}", ct: ct);
    public Task<PTRescheduleSlot[]> RescheduleOptionsAsync(int id, DateOnly from) =>
        Send<PTRescheduleSlot[]>(HttpMethod.Get, $"sessions/{id}/reschedule-options?from={from:yyyy-MM-dd}");
    public Task<PTSessionResponse> RequestRescheduleAsync(int id, PTRescheduleRequest request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/reschedule-request", request);
    public Task<PTSessionResponse> DecideRescheduleAsync(int id, PTRescheduleDecision request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/reschedule-request/decision", request);
    public Task<PTSessionResponse> ProposeAsync(int id, PTProposalRequest request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/proposal", request);
    public Task<PTSessionResponse> ProposeMakeUpAsync(int id, PTMakeUpRequest request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/make-up", request);
    public Task<PTSessionResponse> DecideMakeUpRequestAsync(int id, PTProposalDecision request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/make-up/decision", request);
    public Task<PTSessionResponse> RequestCancellationAsync(int id) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/cancellation-request");
    public Task<PTSessionResponse> DecideProposalAsync(int id, PTProposalDecision request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/proposal/decision", request);
    public Task<PTSessionResponse> AttendanceAsync(int id, bool attended) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/attendance", attended);
    public Task<PTSessionResponse> ReportTrainerAbsenceAsync(int id, PTTrainerAbsenceReportRequest request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"sessions/{id}/trainer-absence", request);
    public Task<PagedResponse<PTAttendanceDispute>> AttendanceDisputesAsync(bool processed, int page) =>
        Send<PagedResponse<PTAttendanceDispute>>(HttpMethod.Get,
            $"manager/attendance-disputes?processed={(processed ? "true" : "false")}&page={page}");
    public Task<PTSessionResponse> DecideDisputeAsync(int id, PTAttendanceDisputeDecision request) =>
        Send<PTSessionResponse>(HttpMethod.Post, $"manager/attendance-disputes/{id}/decision", request);
    public Task<PTSessionResponse> NoteAsync(int id, PTNoteRequest request) =>
        Send<PTSessionResponse>(HttpMethod.Put, $"sessions/{id}/note", request);
    public Task<int> ReviewAsync(int id, PTReviewRequest request) =>
        Send<int>(HttpMethod.Post, $"contracts/{id}/review", request);

    public Task<PagedResponse<PTFreezeResponse>> ManagerFreezePageAsync(bool processed, int page) =>
        Send<PagedResponse<PTFreezeResponse>>(HttpMethod.Get,
            $"manager/freezes/page?processed={(processed ? "true" : "false")}&page={page}");
    public Task<PagedResponse<PTFreezeResponse>> MemberFreezePageAsync(int contract, int page) =>
        Send<PagedResponse<PTFreezeResponse>>(HttpMethod.Get, $"contracts/{contract}/freezes?page={page}");
    public Task<PTFreezeResponse> RequestFreezeAsync(int id, PTFreezeRequest request) =>
        Send<PTFreezeResponse>(HttpMethod.Post, $"contracts/{id}/freezes", request);
    public Task<PTFreezeProposal> PreviewFreezeRequestAsync(int id, PTFreezeRequest request) =>
        Send<PTFreezeProposal>(HttpMethod.Post, $"contracts/{id}/freezes/preview", request);
    public Task<PTFreezeResponse> WithdrawFreezeAsync(int id) =>
        Send<PTFreezeResponse>(HttpMethod.Post, $"freezes/{id}/withdraw");
    public Task<PTReplacementTrainer[]> ReplacementTrainersAsync(int contract) =>
        Send<PTReplacementTrainer[]>(HttpMethod.Get, $"contracts/{contract}/replacement-trainers");
    public Task<PTTrainerChangePreview> PreviewTrainerChangeAsync(int contract, PTTrainerChangeRequest request) =>
        Send<PTTrainerChangePreview>(HttpMethod.Post, $"contracts/{contract}/trainer-change/preview", request);
    public Task<PTContractResponse> ChangeTrainerAsync(int contract, PTTrainerChangeRequest request) =>
        Send<PTContractResponse>(HttpMethod.Post, $"contracts/{contract}/trainer-change", request);
    public Task<PTFreezePreview> PreviewFreezeAsync(int id) =>
        Send<PTFreezePreview>(HttpMethod.Get, $"manager/freezes/{id}/preview");
    public Task<PTFreezeResponse> DecideFreezeAsync(int id, PTFreezeDecision request) =>
        Send<PTFreezeResponse>(HttpMethod.Post, $"manager/freezes/{id}/decision", request);
    public Task<PTEarlyResumePreview> PreviewResumeAsync(int id, PTEarlyResumeRequest request) =>
        Send<PTEarlyResumePreview>(HttpMethod.Post, $"freezes/{id}/early-resume/preview", request);
    public Task<PTContractResponse> ConfirmResumeAsync(int id, PTEarlyResumeConfirmation request) =>
        Send<PTContractResponse>(HttpMethod.Post, $"freezes/{id}/early-resume/confirm", request);

    private async Task<T> Send<T>(HttpMethod method, string route, object? body = null, bool anonymous = false,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, $"api/pt/{route}");
        if (!anonymous)
        {
            var token = await session.GetAccessTokenAsync()
                ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        return await AuthApiClient.SendAsync<T>(http, request, ct);
    }

    public void Dispose() => http.Dispose();
}
