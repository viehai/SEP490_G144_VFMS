using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Pages.Manager.Locker;
using VFMS.Contracts.Lockers;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class LockerClientTests
{
    [Fact]
    public async Task ManagerLockerDetailsShowsOverdueInformationWithoutStaffPaymentActions()
    {
        var rental = new LockerRentalView(7, "LK-07", new(1, "A01", "A", 0, "Overdue"), "Monthly", "Overdue",
            250_000, 100_000, DateTime.UtcNow.AddDays(-32), DateTime.UtcNow.AddDays(-2), null, 2, 20_000, null,
            true, null, null, null, false, 1, null, 0, null);
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(Client(HttpStatusCode.OK, new LockerStaffRental(rental, "member", "Nguyễn An", "0901234567")))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ManagerLockerContractDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ManagerLockerContractDetails.Id)] = 7
            }))).ToHtmlString()));
        Assert.Contains("Nguyễn An", html);
        Assert.Contains("LK-07", html);
        Assert.Contains("20.000 ₫", html);
        Assert.Contains("/manager/locker-operations?", html);
        Assert.DoesNotContain("Xác nhận đã thu", html);
        Assert.DoesNotContain("Giao chìa", html);
        Assert.All(_requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.EndsWith("/staff/rentals/7", Assert.Single(_requests).RequestUri!.AbsolutePath);
    }
}
