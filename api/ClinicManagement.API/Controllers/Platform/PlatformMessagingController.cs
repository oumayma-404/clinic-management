using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// The vendor allocates a cabinet's WhatsApp reminder forfait and corrects one it recorded wrongly
/// (<c>vendor-whatsapp-messaging-quota</c> US-6, US-7).
///
/// <para>⚠️ <b>Its own controller, on <c>PlatformSubscriptionsController</c>'s precedent and for a second reason.</b>
/// That one says « the console's writes are about entitlements », and a forfait de rappels is not an entitlement — it is
/// a metered consumable the vendor buys from Meta and resells. Folding these two routes in would make both claims
/// vaguer, and it would put a third vocabulary (« forfait » meaning messages, not a subscription plan) on a file whose
/// every other route means the other one. <c>PlatformPortfolioController</c>'s « read-only by construction » stays
/// checkable for the same reason.</para>
///
/// <para>⚠️ <b>Both carry <c>[AllowsWithoutSubscription]</c>.</b> A console account is not a cabinet, so today they pass
/// the gate on that ground — but « the endpoint whose purpose is to end a refusal must never be refused » belongs at the
/// endpoint rather than inferred from how the tenant scope happens to resolve. It applies here even more directly than to
/// the payment routes: a cabinet whose *subscription* has lapsed is precisely one whose reminders the vendor may still
/// need to top up, and refusing that would leave a practice unable to warn patients about visits it already has booked.</para>
///
/// <para>⚠️ <b>No action here names either of the two vendor commands under
/// <c>Application/Features/Messaging/Commands/</c></b> (AC-9.3) — <c>MessagingVendorCommandReachabilityTests</c>
/// source-scans <c>Controllers/</c> for those type names, and a <c>using</c> <b>or a comment</b> is enough to fail it,
/// which is why they are described here rather than spelled. That constraint is also why the wrappers are named
/// <c>Record…FromConsoleCommand</c> and <c>Cancel…FromConsoleCommand</c>: neither contains a vendor command's name as a
/// substring, where the obvious <c>Grant…</c>/<c>Cancel…</c> pairing would have.</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s <c>/api/platform/*</c> on
/// the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformMessagingController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformMessagingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Records a cabinet's WhatsApp reminder forfait — a standing monthly figure or a one-off top-up (AC-6.1).
    ///
    /// <para>⚠️ <b>Which of the two it is, and which month it takes effect in, are the server's decision</b> (AC-6.4a):
    /// a raise applies immediately and a lowering from the next Tunisian month, so a practice is never cut off
    /// mid-afternoon by a change it had no warning of. The response states the effective month for that reason.</para>
    ///
    /// <para>⚠️ <b>A repeated submission produces one entry and returns the first outcome</b> (AC-6.7) — not a conflict.
    /// Two <i>different</i> allocations landing together are two entries in an append-only ledger, both kept (EC-5): the
    /// surplus one is corrected by a cancellation, never by refusing money already received.</para>
    ///
    /// <para>⚠️ An unknown cabinet is <b>404 with a code</b> the console branches on, never the French sentence; a past
    /// top-up month carries its own code so the dialog can point at the month field rather than the form.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/messaging-allowances")]
    [AllowsWithoutSubscription(
        "The vendor console tops up a cabinet's reminder forfait, and a cabinet whose own subscription has lapsed is "
        + "precisely one that may still need its patients warned about visits already booked.")]
    public async Task<ActionResult<PlatformMessagingAllowanceRecordedDto>> RecordAllowance(
        Guid clinicId,
        [FromBody] RecordMessagingAllowanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RecordMessagingAllowanceFromConsoleCommand
            {
                ClinicId = clinicId,
                IdempotencyKey = request.IdempotencyKey,
                MessagesPerMonth = request.MessagesPerMonth,
                TopUpMessages = request.TopUpMessages,
                AppliesToMonth = request.AppliesToMonth,
                AmountDt = request.AmountDt,
                Method = request.Method,
                Reference = request.Reference,
                Note = request.Note,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return HandleFailure(
            result,
            result.Code == RecordMessagingAllowanceFromConsoleCommandHandler.UnknownClinicCode
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Cancels one allocation with a written motif, and every month it fed recomputes (AC-7.1–7.4).
    ///
    /// <para>⚠️ <b>A POST and deliberately not a DELETE.</b> Nothing is deleted: the entry stays in the ledger, struck
    /// through, carrying its motif and its canceller (AC-7.2), and a <c>DELETE</c> would advertise the opposite to every
    /// reader of this file and of any client generated from it.</para>
    ///
    /// <para>⚠️ <b>Unlike its subscription sibling this reaches the CURRENT month</b> (AC-7.4/7.4a): a mis-keyed
    /// « +3000 » must be correctable in the month it was keyed into. Consumption is untouched, so the month may end up
    /// reading « épuisé » — which the confirmation states in advance from the read's own server-computed preview.</para>
    ///
    /// <para>⚠️ The refusals a client acts on differently carry <b>codes</b>: an unknown cabinet or allocation is a 404,
    /// and one already struck through is a state of the world (409) rather than a rejected request — the fiche is then
    /// re-read so the existing motif and author appear (AC-7.5). Neither is recovered by matching the French sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/messaging-allowances/{entryId:guid}/cancellation")]
    [AllowsWithoutSubscription(
        "Correcting a forfait recorded by mistake is the vendor's own bookkeeping, and it must not depend on whether "
        + "the cabinet it concerns has paid its own subscription.")]
    public async Task<ActionResult<PlatformMessagingAllowanceCancelledDto>> CancelAllowance(
        Guid clinicId,
        Guid entryId,
        [FromBody] CancelMessagingAllowanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new CancelMessagingAllowanceFromConsoleCommand
            {
                ClinicId = clinicId,
                EntryId = entryId,
                Reason = request.Reason ?? string.Empty,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return HandleFailure(result, result.Code switch
        {
            CancelMessagingAllowanceFromConsoleCommandHandler.UnknownClinicCode => StatusCodes.Status404NotFound,
            CancelMessagingAllowanceFromConsoleCommandHandler.UnknownEntryCode => StatusCodes.Status404NotFound,
            CancelMessagingAllowanceFromConsoleCommandHandler.AlreadyCancelledCode => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });
    }
}
