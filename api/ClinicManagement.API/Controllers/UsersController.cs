using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Users.Queries;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class UsersController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    private readonly DeploymentProfile _deployment;

    private readonly IStepUpConfirmations _stepUp;
    private readonly IClinicContext _clinicContext;

    public UsersController(
        IMediator mediator,
        IConfiguration configuration,
        DeploymentProfile deployment,
        IStepUpConfirmations stepUp,
        IClinicContext clinicContext)
    {
        _mediator = mediator;
        _configuration = configuration;
        _deployment = deployment;
        _stepUp = stepUp;
        _clinicContext = clinicContext;
    }

    /// <summary>The action name a step-up confirmation must have been minted for.</summary>
    public const string ResetTotpStepUpAction = "reset-user-totp";

    /// <summary>
    /// Get all users for the current admin's clinic, with account status (AC-5.1).
    /// </summary>
    /// <param name="page">1-based page number. Omit both paging parameters to get every match.</param>
    /// <param name="pageSize">Rows per page, clamped to <c>PageRequest.MaxPageSize</c>.</param>
    /// <param name="search">
    /// Free-text filter. Applied in SQL <b>before</b> the page is cut, so it searches the whole clinic — a
    /// search that only saw the current page would answer a different question from the one that was typed.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<ClinicUsersPageDto>> GetUsers(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null)
    {
        var query = new ListUsersQuery { Page = page, PageSize = pageSize, SearchTerm = search };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a colleague's account and return a one-time password to relay (<c>multi-tenant-cloud</c> US-3).
    ///
    /// <para>404s where the deployment does not own its accounts: an Auth0 install creates users in Auth0, and a
    /// password-backed row minted here would be an account nobody could log into. Gated on
    /// <c>UsesLocalAccounts</c> and <b>not</b> on <c>AllowsSelfRegistration</c> — those are opposite questions.
    /// This action is what a profile with self-registration closed uses <i>instead</i>, so it must stay reachable
    /// in both account-owning profiles.</para>
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedClinicUserDto>> CreateUser([FromBody] CreateClinicUserRequest request)
    {
        if (!_deployment.UsesLocalAccounts)
        {
            return NotFound();
        }

        var command = new CreateClinicUserCommand
        {
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role,
            DoctorInfo = request.DoctorInfo
        };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Reset a user's password to a temporary one and force a change at next login (AC-5.2).
    /// The temporary password is returned once for the admin to relay.
    /// </summary>
    /// <summary>
    /// An administrator removes a colleague's second factor so they can enrol a new one
    /// (<c>hosted-security-hardening</c> FR-1.4) — way back #2 of three.
    ///
    /// <para>⚠️ <b>A step-up confirmation is required, and it is enforced HERE rather than in the handler.</b>
    /// This is the action that strips another person's protection, so an unlocked machine with an admin session
    /// open must not be enough — the confirmation proves somebody is present now. It is checked before the
    /// mediator is reached, so a request without one resolves no handler and touches no row.</para>
    ///
    /// <para>⚠️ <b>Deliberately NOT <c>[AllowsWithoutSubscription]</c></b>, unlike the password reset above.
    /// That one is about regaining <i>read</i> access an expired cabinet keeps by right; this one is about a
    /// colleague who can still sign in with a recovery code, and there is no read at stake.</para>
    /// </summary>
    [HttpPost("{id}/totp/reset")]
    public async Task<IActionResult> ResetTotp(string id, [FromBody] StepUpConfirmationRequest request)
    {
        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId)
            || !_stepUp.Consume(callerId, ResetTotpStepUpAction, request.ConfirmationToken ?? string.Empty))
        {
            return Failure(
                "Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        var result = await _mediator.Send(new ResetUserTotpCommand { UserId = id });

        return result.IsSuccess ? Ok(new { }) : HandleFailure(result);
    }

    [HttpPost("{id}/reset-password")]
    [AllowsWithoutSubscription(
        "FR-3, AC-4.1/4.2 — regaining READ access must not depend on payment. A staff member who has forgotten "
        + "their password is locked out of the reads, exports and PDFs an expired cabinet keeps by right, and on a "
        + "hosted deployment there is no other recovery: change-password needs the current one and reset-admin-password "
        + "needs container access. AC-4.7's own reasoning for exempting change-password extends to the reset that "
        + "creates one.")]
    public async Task<ActionResult<ResetPasswordResultDto>> ResetPassword(string id)
    {
        var command = new ResetUserPasswordCommand { TargetUserId = id };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Deactivate or reactivate a user (AC-5.3). Historical records are retained.
    /// </summary>
    [HttpPut("{id}/status")]
    [AllowsWithoutSubscription("FR-3 — offboarding must not wait on an invoice; a colleague who left keeps access otherwise.")]
    public async Task<ActionResult<ClinicUserDto>> SetStatus(string id, [FromBody] SetUserStatusRequest request)
    {
        var command = new SetUserActiveCommand
        {
            TargetUserId = id,
            IsActive = request.IsActive,
            Version = request.Version,
        };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Change a user's clinic role between admin / doctor / secretary (AC-P2.23). Admin-only through the
    /// class-level policy; the handler re-checks the DB role, validates the value against the closed set, and
    /// refuses a self-demotion that would leave the clinic with no active admin.
    /// </summary>
    [HttpPut("{id}/role")]
    public async Task<ActionResult<ClinicUserDto>> SetRole(string id, [FromBody] SetUserRoleRequest request)
    {
        var command = new ChangeUserRoleCommand
        {
            TargetUserId = id,
            Role = request.Role,
            Version = request.Version,
        };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
