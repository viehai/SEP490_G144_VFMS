using VFMS.Contracts.Yoga;

namespace VFMS.Client.Services;

/// <summary>Yoga booking calls whose flow needs more than one request.</summary>
public static class YogaBookingCalls
{
    public const string LateCancellationCode = "LATE_CANCELLATION_CONFIRM";

    /// <summary>
    /// UC-39 Cancel Yoga Booking. Returns false when the session starts within 2 hours and the member has not yet
    /// accepted the no-show penalty (MSG-34); call again with <paramref name="confirmLate"/> after they accept.
    /// </summary>
    public static async Task<bool> CancelBookingAsync(this VfmsApiClient api, int bookingId, bool confirmLate, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.PostAsync<YogaBookingResponse>($"api/yoga/bookings/{bookingId}/cancel", new CancelYogaBookingRequest(confirmLate), cancellationToken);
            return true;
        }
        catch (ApiException ex) when (!confirmLate && ex.ErrorCode == LateCancellationCode)
        {
            return false;
        }
    }
}
