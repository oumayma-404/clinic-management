using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.PushDevices.Commands;
using ClinicManagement.Application.Features.PushDevices.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// OS-push device registry (<c>mobile-native-shells</c> Part 6).
///
/// <para><b><c>AnyClinicRole</c>, and that is the point:</b> a secretary's phone must be able to register. The
/// notifications this subscribes to are the agenda events reception acts on first — a booking, a cancellation, a
/// reschedule — so gating this on <c>AdminOrDoctor</c> would exclude the role most likely to be holding the
/// phone. Nothing here reads clinic-wide money.</para>
///
/// <para>⚠️ <b>The whole controller 404s where the deployment supports neither platform</b> (AC-51). Absent rather
/// than present-and-always-refusing, because a shell probing a 404 learns « this installation has no push » in one
/// call, whereas a 400 per registration attempt reads as a bad request it should retry.</para>
/// </summary>
[ApiController]
[Route("api/push-devices")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PushDevicesController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IOsPushAvailability _availability;

    public PushDevicesController(IMediator mediator, IOsPushAvailability availability)
    {
        _mediator = mediator;
        _availability = availability;
    }

    /// <summary>
    /// Registers, refreshes or rebinds the caller's device token. One verb for all three — see the command.
    /// </summary>
    [HttpPost]
    [AllowsWithoutSubscription("FR-3, AC-4.7 — fired at every mobile sign-in; refusing it breaks signing in.")]
    public async Task<IActionResult> Register([FromBody] RegisterPushDeviceCommand command)
    {
        if (!_availability.IsAvailableAtAll)
        {
            return NotFound();
        }

        var result = await _mediator.Send(command);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>
    /// Stops delivery to one device (sign-out). Idempotent, so a shell tearing its session down never has to
    /// interpret a refusal.
    /// </summary>
    [HttpDelete("{token}")]
    [AllowsWithoutSubscription("FR-3 — the mirror of registration: signing out must not depend on an invoice.")]
    public async Task<IActionResult> Deregister(string token)
    {
        if (!_availability.IsAvailableAtAll)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new DeletePushDeviceCommand { Token = token });
        return result.IsSuccess ? NoContent() : HandleFailure(result);
    }

    /// <summary>
    /// What this installation can do, per platform (AC-51, AC-52).
    ///
    /// <para>⚠️ <b>Deliberately NOT behind the availability 404 the two writes are behind.</b> It is the endpoint
    /// that <i>answers</i> « is push available? », so refusing it where the answer is « no » would make the one
    /// call that can say so the one call that cannot be made — the same trap
    /// <c>GET /api/meta/client-requirements</c> is exempt from its own version floor for.</para>
    /// </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability()
    {
        var result = await _mediator.Send(new GetPushAvailabilityQuery());
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }
}
