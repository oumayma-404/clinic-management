using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions.Queries;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Abonnement » — where the cabinet stands and how to pay (`clinic-subscription` Part C, US-2).
///
/// <para><b>Read-only, and there is no write endpoint anywhere in this feature</b> (FR-6): granting, cancelling and
/// suspending are vendor console verbs. A cabinet that could extend its own entitlement over HTTP would not have
/// one.</para>
///
/// <para><b>⚠️ The class policy is <c>AnyClinicRole</c>, which is a deliberate exception to this product's rule that
/// a secretary sees no clinic-wide money screen</b> (AC-2.2). The amounts here are what the practice owes its
/// software vendor, not clinic revenue — none of it reaches la caisse or any patient's balance (FR-2) — and EC-10
/// depends on reception being able to open the screen the refusal points at. What stays admin-only is the payment
/// <see cref="GetHistory">history</see>.</para>
/// </summary>
[ApiController]
[Route("api/subscription")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class SubscriptionController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;

    public SubscriptionController(IMediator mediator, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _deployment = deployment;
    }

    /// <summary>
    /// The cabinet's state, end date, countdown, forfait, the published tariff, how to pay and who to contact.
    ///
    /// <para>Works on an expired cabinet (AC-4.8) — structurally, because <c>SubscriptionGateMiddleware</c> never
    /// inspects a GET. The attribute below is documentation of that intent, not the thing that makes it true.</para>
    /// </summary>
    [HttpGet]
    [AllowsWithoutSubscription("AC-4.8 — the one screen that says how to pay must be readable by a cabinet that has not.")]
    public async Task<ActionResult<SubscriptionDto>> GetSubscription(CancellationToken cancellationToken = default)
    {
        if (!_deployment.RequiresSubscription)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetSubscriptionQuery(), cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// One page of what the cabinet has paid, newest first: date, period covered, amount, method and reference, with
    /// a cancelled entry kept visible and struck through (AC-2.3, AC-5.5).
    /// </summary>
    /// <param name="page">1-based. Omitting it gets the first page — this read is never unbounded.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    [HttpGet("history")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [AllowsWithoutSubscription("AC-4.8 — the owner deciding whether to renew reads what they last paid.")]
    public async Task<ActionResult<SubscriptionHistoryPageDto>> GetHistory(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        if (!_deployment.RequiresSubscription)
        {
            return NotFound();
        }

        var result = await _mediator.Send(
            new GetSubscriptionHistoryQuery { Page = page, PageSize = pageSize }, cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
