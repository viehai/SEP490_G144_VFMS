using System.Text.Json;
using Microsoft.JSInterop;

namespace VFMS.Client.Components.Memberships;

/// <summary>
/// The yoga class a visitor picked on the timetable before buying a plan. Kept in sessionStorage so the
/// checkout page can link back to it once the order is paid. Booking stays manual (the member picks a mat).
/// </summary>
public sealed record PendingYogaClass(int ClassId, string Name, string Level, DateOnly Date)
{
    /// <summary>The plans shown for this class: the checkout links back only when one of them was bought.</summary>
    public IReadOnlyList<int> PlanIds { get; init; } = [];

    private const string Key = "vfms.yoga.pendingClass";

    public string ScheduleHref => YogaClassCard.ScheduleLink(ClassId, Date);

    /// <summary>The plans page link for buying a plan that books this class.</summary>
    public string PlansHref =>
        $"/membership-plans?type=yoga&level={Uri.EscapeDataString(Level)}&classId={ClassId}&date={Date:yyyy-MM-dd}&className={Uri.EscapeDataString(Name)}";

    public Task SaveAsync(IJSRuntime js) =>
        js.InvokeVoidAsync("sessionStorage.setItem", Key, JsonSerializer.Serialize(this)).AsTask();

    public static async Task<PendingYogaClass?> LoadAsync(IJSRuntime js)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", Key);
            return json is null ? null : JsonSerializer.Deserialize<PendingYogaClass>(json);
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            return null;   // storage blocked or an old value: just no link
        }
    }
}
