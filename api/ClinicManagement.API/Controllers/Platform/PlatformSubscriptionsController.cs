using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// The vendor records a payment and the cabinet is unlocked (<c>platform-console</c> US-4), and corrects one it
/// recorded wrongly (US-5).
///
/// <para>⚠️ <b>The console's only writes, and they are deliberately in their own controller.</b>
/// <c>PlatformPortfolioController</c> is read-only by construction and says so — « it has no write path » is the
/// strongest proof a surface that can read every practice can offer, and that claim stops being checkable the
/// moment one action on it writes. Keeping the writes here leaves that guarantee legible on both files.</para>
///
/// <para>⚠️ <b>It carries <c>[AllowsWithoutSubscription]</c>, and it is not decoration.</b> The gate refuses every
/// non-GET under <c>/api</c> for a cabinet whose entitlement has lapsed. A console account is not a cabinet, so
/// today it passes on that ground — but « the endpoint whose purpose is to END a refusal must never be refused »
/// is a property worth stating at the endpoint rather than inferring from how the scope happens to resolve, and it
/// is exactly the class of cabinet this action exists for. <c>SubscriptionWriteGateTests</c> pins both halves.</para>
///
/// <para>⚠️ Reachable only on the console's own Kestrel listener: <c>ConsolePortGate</c> 404s <c>/api/platform/*</c>
/// on the public port and 404s every console path when <c>Console:Port</c> is 0.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformSubscriptionsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformSubscriptionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Records a payment already received and extends the cabinet's entitlement (AC-4.1–4.3).
    ///
    /// <para>⚠️ <b>A repeated submission produces one entry and returns the first outcome</b> (AC-4.6) — not a
    /// conflict. Two <i>different</i> grants landing together are two entries in an append-only ledger, both kept
    /// (EC-6): the surplus one is corrected by a cancellation, never by refusing money the vendor has been paid.</para>
    ///
    /// <para>⚠️ An unknown cabinet is <b>404 with a code</b> the console branches on, never the French sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/subscription-periods")]
    [AllowsWithoutSubscription(
        "The vendor console records a payment for a cabinet that has usually already lapsed — refusing this is "
        + "refusing the one action that ends the refusal.")]
    public async Task<ActionResult<PlatformSubscriptionRecordedDto>> RecordPeriod(
        Guid clinicId,
        [FromBody] RecordSubscriptionPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RecordSubscriptionPeriodCommand
            {
                ClinicId = clinicId,
                IdempotencyKey = request.IdempotencyKey,
                Complimentary = request.Complimentary,
                DurationMonths = request.DurationMonths,
                DurationDays = request.DurationDays,
                EndsOn = request.EndsOn,
                Plan = request.Plan,
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
            result.Code == RecordSubscriptionPeriodCommandHandler.UnknownClinicCode
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Cancels one ledger entry with a written reason, and the cabinet's end date recomputes (AC-5.1–5.3).
    ///
    /// <para>⚠️ <b>A POST and deliberately not a DELETE.</b> Nothing is deleted: the entry stays in the ledger,
    /// struck through, carrying its motif and its canceller (AC-5.2), and a <c>DELETE</c> would advertise the
    /// opposite to every future reader of this file and of any client generated from it.</para>
    ///
    /// <para>⚠️ The two refusals a client acts on differently carry <b>codes</b> — an unknown cabinet or entry is a
    /// 404, and an entry already struck through is a state of the world (409) rather than a rejected request. Neither
    /// is recovered by matching the French sentence.</para>
    /// </summary>
    [HttpPost("clinics/{clinicId:guid}/subscription-periods/{entryId:guid}/cancellation")]
    [AllowsWithoutSubscription(
        "Correcting a payment recorded by mistake is the vendor's own bookkeeping, and a cabinet whose entitlement "
        + "has lapsed is the likeliest one to need it — including when the lapse is what the mis-keyed entry caused.")]
    public async Task<ActionResult<PlatformSubscriptionCancelledDto>> CancelPeriod(
        Guid clinicId,
        Guid entryId,
        [FromBody] CancelSubscriptionPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new CancelSubscriptionPeriodFromConsoleCommand
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
            CancelSubscriptionPeriodFromConsoleCommandHandler.UnknownClinicCode => StatusCodes.Status404NotFound,
            CancelSubscriptionPeriodFromConsoleCommandHandler.UnknownEntryCode => StatusCodes.Status404NotFound,
            CancelSubscriptionPeriodFromConsoleCommandHandler.AlreadyCancelledCode => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });
    }
}
