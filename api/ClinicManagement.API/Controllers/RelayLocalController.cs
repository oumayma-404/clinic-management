using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Paramètres → PC de secours » on the PC de secours itself (<c>clinic-pc-copy</c> AC-8.1, AC-8.2): what this PC is
/// now, and — once retired — « Effacer la copie ». Admins only, like the card; 404 anywhere else.
/// </summary>
[ApiController]
[Route("api/relay/local")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class RelayLocalController : ApiControllerBase
{
    private readonly DeploymentProfile _deployment;
    private readonly IRelayLocalStatus _status;
    private readonly IStepUpConfirmations _stepUp;
    private readonly IClinicContext _clinicContext;
    private readonly IDataProtectionProvider _protection;
    private readonly RelayLocalEraser _eraser;

    public RelayLocalController(
        DeploymentProfile deployment,
        IRelayLocalStatus status,
        IStepUpConfirmations stepUp,
        IClinicContext clinicContext,
        IDataProtectionProvider protection,
        RelayLocalEraser eraser)
    {
        _deployment = deployment;
        _status = status;
        _stepUp = stepUp;
        _clinicContext = clinicContext;
        _protection = protection;
        _eraser = eraser;
    }

    /// <summary>
    /// <c>clinic-pc-copy</c> Part 3: whether this PC holds the cabinet's saves — asked by the cabinet's Windows and
    /// Android apps (through the PC's pinned certificate) to know whether to switch here, and when to go back to the
    /// cloud. Anonymous: an app asks before anyone is signed in here, and the answer is one fact the cabinet's own
    /// screens already show everyone (the strip).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("holding")]
    public ActionResult<RelayHoldingDto> Holding()
    {
        if (!_deployment.MirrorsCloudClinic)
        {
            return NotFound();
        }

        return Ok(new RelayHoldingDto(_status.IsHolding || _status.IsHandingBack, _status.IsHandingBack));
    }

    [HttpGet]
    public ActionResult<RelayLocalStatusDto> Get()
    {
        if (!_deployment.MirrorsCloudClinic)
        {
            return NotFound();
        }

        return Ok(new RelayLocalStatusDto(
            _status.IsRetired,
            _status.RetiredAtUtc,
            RelayLabels.Local(_status.IsRetired, _status.RetiredAtUtc)));
    }

    /// <summary>
    /// « Effacer la copie » (AC-8.2): every record and file of the cabinet leaves this PC. Only once the cloud has
    /// retired it — before that, this copy is the cabinet's spare — and only with an authenticator code.
    /// </summary>
    [HttpPost("erase")]
    [AllowedOnStandbyRelay("Erasing a retired PC's copy writes nothing the cloud owns: it removes this PC's copy (AC-8.2).")]
    [AllowsWithoutSubscription("Removing a retired PC's copy of the cabinet's records is offboarding, never new work.")]
    public async Task<IActionResult> Erase(
        [FromHeader(Name = BackupController.StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!_deployment.MirrorsCloudClinic)
        {
            return NotFound();
        }

        if (!_status.IsRetired)
        {
            return HandleFailure(Result.Failure(RelayRefusals.NotRetired, RelayRefusals.NotRetiredCode),
                StatusCodes.Status409Conflict);
        }

        if (!_eraser.MayErase)
        {
            return HandleFailure(Result.Failure(RelayRefusals.CutWorkKept, RelayRefusals.CutWorkKeptCode),
                StatusCodes.Status409Conflict);
        }

        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId) || !_stepUp.Consume(callerId, RelayStepUpActions.Erase, confirmation ?? string.Empty))
        {
            return Failure("Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        // The PC's own pairing names the cabinet whose copy this is — never a value a request could choose.
        var credentials = new RelayCredentialStore(_protection).TryLoad();
        if (credentials is null)
        {
            return Failure("Ce PC ne sait plus de quel cabinet il garde la copie.", StatusCodes.Status409Conflict);
        }

        var files = await _eraser.EraseAsync(credentials.ClinicId, DateTime.UtcNow, cancellationToken);
        return Ok(new { erased = true, filesDeleted = files });
    }
}
