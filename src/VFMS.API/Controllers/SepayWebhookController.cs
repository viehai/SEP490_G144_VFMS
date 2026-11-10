using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/v1/commerce/payment/webhook/sepay")]
[AllowAnonymous]
public sealed class SepayWebhookController(IMembershipPurchaseService service) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16_384)]
    [ProducesResponseType(typeof(SepayWebhookResponse), 200)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // SePay signs the raw body, including the original JSON whitespace and property order.
        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, ct);
        return this.ToActionResult(await service.ConfirmSepayAsync(Request.Headers.Authorization.ToString(),
            Request.Headers["X-SePay-Timestamp"].ToString(), Request.Headers["X-SePay-Signature"].ToString(),
            body.ToArray(), ct));
    }
}
