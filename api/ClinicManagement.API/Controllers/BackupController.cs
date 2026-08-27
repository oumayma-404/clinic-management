using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Application.Features.Backup.Queries;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.API.Filters;
using ClinicManagement.API.Models;
using ClinicManagement.API.Startup;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Admin-only one-click "Backup now" (US-8 / FR-G). Thin MediatR pass-through: a success returns the
/// destination path + size (AC-8.2), a failure returns the clear operator-facing reason as a 400 —
/// never a silent success (AC-8.2 / AC-8.3).
///
/// <para>⚠️ <b>The two writes 404 where <c>DeploymentProfile.BacksUpItsOwnData</c> is false</b> — the two hosted
/// kinds, whose data is protected by the <c>deploy/</c> <c>backup</c> sidecar (off-server, on a schedule) and whose
/// single database holds every cabinet, so an in-app <c>pg_dump</c> would be both weaker and a cross-tenant read.
/// Absent rather than present-and-refusing, the shape <c>push-devices</c> and <c>SubscriptionController</c> already
/// use.</para>
///
/// <para>⚠️ <b><c>GET history</c> deliberately answers anyway</b>, reporting <c>managedByHost</c>: it is the read
/// that explains why there is no button, so refusing it would leave the screen unable to say anything. Before this,
/// a hosted clinic met « L'outil pg_dump est introuvable » on that panel and a « sauvegarde périmée » bell alert
/// that nothing available to them could ever clear.</para>
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class BackupController : ApiControllerBase
{
    /// <summary>
    /// Where the step-up confirmation travels (FR-4.3). A header rather than the query string because this
    /// application's URLs are logged and FR-4.4 is about exactly that; and rather than the body because the
    /// download is a <c>GET</c>.
    /// </summary>
    public const string StepUpHeader = "X-Step-Up-Confirmation";

    /// <summary>The action a download's confirmation must have been minted for.</summary>
    public const string ArchiveStepUpAction = "download-clinic-archive";

    /// <summary>
    /// And the restore's — a <b>different</b> action, so one confirmation cannot authorise the other. They are
    /// opposite operations on the same records, and a token good for both would let « je vais télécharger une
    /// copie » become « j'ai écrasé le cabinet » on one click.
    /// </summary>
    public const string RestoreStepUpAction = "restore-clinic-archive";

    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;
    private readonly IConfiguration _configuration;
    private readonly IStepUpConfirmations _stepUp;
    private readonly IClinicContext _clinicContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupController> _logger;

    public BackupController(
        IMediator mediator,
        DeploymentProfile deployment,
        IConfiguration configuration,
        IStepUpConfirmations stepUp,
        IClinicContext clinicContext,
        IServiceScopeFactory scopeFactory,
        ILogger<BackupController> logger)
    {
        _mediator = mediator;
        _deployment = deployment;
        _configuration = configuration;
        _stepUp = stepUp;
        _clinicContext = clinicContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Spends the caller's confirmation for <paramref name="action"/>, or refuses with 403.
    ///
    /// <para>⚠️ <b>Enforced here rather than in the handler</b>, and single-use per action: an unlocked machine
    /// with an admin session open must not be enough to take a practice's whole record out, so the confirmation
    /// proves somebody is present <i>now</i>. The refusal is deliberately not the login lockout's — three wrong
    /// attempts refuse this action on the step-up's own counter and leave the session untouched, which is what
    /// stops a mistyped password at the export card locking a practice's only administrator out mid-day.</para>
    /// </summary>
    private ActionResult? RequireStepUp(string? confirmation, string action)
    {
        var callerId = _clinicContext.GetUserId();

        if (string.IsNullOrWhiteSpace(callerId)
            || !_stepUp.Consume(callerId, action, confirmation ?? string.Empty))
        {
            return Failure(
                "Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        return null;
    }

    /// <summary>
    /// Run a backup now — dumps the database and copies the file-storage folder to a timestamped
    /// subfolder of the destination (AC-8.1).
    /// </summary>
    [HttpPost]
    [AllowsWithoutSubscription("FR-3 — the AC-4.2 argument: a cabinet must always be able to take its data out. "
                               + "The scheduled backup keeps running for the same reason (FR-8).")]
    public async Task<ActionResult<BackupResultDto>> BackupNow([FromBody] BackupRequest request)
    {
        if (!_deployment.BacksUpItsOwnData)
        {
            return NotFound();
        }

        var command = new BackupNowCommand { DestinationFolder = request.DestinationFolder };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// « Historique des sauvegardes » (L4d) — the clinic's recorded attempts, newest first, plus the last
    /// successful moment, the resolved default destination and the schedule.
    ///
    /// <para>The read that turns backup from a habit into a guarantee: before it, the result of a backup lived in
    /// a React <c>useState</c> and « quand la dernière sauvegarde a-t-elle réussi ? » had no answer anywhere in
    /// the product.</para>
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<BackupHistoryDto>> History([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        // ⚠️ This one answers instead of 404ing, and the asymmetry with the two writes is the point: it is the read
        // that *explains* why there is no button, so refusing it would leave the screen with nothing to say. Same
        // exemption logic as `meta/client-requirements` against its own version floor, and
        // `push-devices/availability` against the platforms it reports as unavailable.
        if (!_deployment.BacksUpItsOwnData)
        {
            return Ok(BackupHistoryDto.ManagedByTheHost());
        }

        var result = await _mediator.Send(new GetBackupHistoryQuery { Page = page, PageSize = pageSize });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The unattended-backup schedule (L4a): on/off, the clinic-local hour, how many copies to keep, and the
    /// staleness threshold. The caller the four new columns ship with — a setting with no writer is the
    /// <c>SetStockExpiryLeadDays</c> failure the spec names.
    /// </summary>
    [HttpPut("schedule")]
    public async Task<ActionResult<BackupScheduleDto>> SetSchedule([FromBody] SetBackupScheduleCommand command)
    {
        if (!_deployment.BacksUpItsOwnData)
        {
            return NotFound();
        }

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Downloads the cabinet's own **archive** — every clinical and financial record it holds, plus the blobs
    /// behind them, as one file the practice keeps on its own PC
    /// (<c>clinic-data-archive-and-restore</c>).
    ///
    /// <para><b>⚠️ Not gated on <c>BacksUpItsOwnData</c>, unlike the three actions above, and the difference is the
    /// whole reason this exists.</b> Those run <c>pg_dump</c>, which takes <c>--dbname</c> and has no tenant
    /// predicate — so on a shared database « Dr X clicks Sauvegarder » would dump every other practice's patients,
    /// which is why the capability turns them off there. This one goes through the tenant filter like every CSV
    /// export, carries one cabinet's rows and nothing else, and is therefore available on **every** deployment: on
    /// the hosted kinds it is the answer to « comment garder une copie de mon côté ? » that the card previously had
    /// no button for, and on a clinic's own PC it is a portable copy its own <c>pg_dump</c> backup is not.</para>
    ///
    /// <para>⚠️ It is a **GET that the subscription gate never inspects**, so an expired cabinet keeps it — the
    /// AC-4.2 guarantee that a practice can always take its data out. The attribute below states that rather than
    /// leaving a reader to re-derive it.</para>
    /// </summary>
    /// <param name="confirmation">
    /// The step-up token minted by <c>POST /api/auth/step-up</c> for <see cref="ArchiveStepUpAction"/>
    /// (FR-4.3). ⚠️ <b>A header, never the query string</b>: this application's URLs are logged, and FR-4.4 is
    /// about exactly that.
    /// </param>
    [HttpGet("archive")]
    [EnableRateLimiting(RateLimiting.ArchivePolicy)]
    [AllowsWithoutSubscription(
        "A cabinet must always be able to take its own data out — recovering records that already exist is not "
        + "recording new work (AC-8, the AC-4.2 argument).")]
    public async Task<IActionResult> DownloadArchive(
        [FromHeader(Name = StepUpHeader)] string? confirmation, CancellationToken cancellationToken)
    {
        var refusal = RequireStepUp(confirmation, ArchiveStepUpAction);
        if (refusal != null)
        {
            return refusal;
        }

        var result = await _mediator.Send(new BuildClinicArchiveQuery(), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        var archive = result.Value!;

        // FR-4.2 — « delivered », not « requested ». The response body has not been written yet, so the only
        // moment that can tell them apart is after it completes: `RequestAborted` is what a download abandoned at
        // 90 % looks like from here. The write needs its OWN scope — the request's is being torn down as this
        // runs — and it is best-effort, unlike the request row, because the archive has already left and there is
        // nobody left to refuse.
        var length = archive.Content.CanSeek ? archive.Content.Length : 0;
        var aborted = HttpContext.RequestAborted;
        Response.OnCompleted(() => RecordDeliveryAsync(archive, length, !aborted.IsCancellationRequested));

        // The stream overload, so the framework copies the temp file to the response in chunks and disposes it
        // afterwards. `File(byte[], …)` would hold the whole archive — twenty years of radiographs — in memory
        // for the life of the response, on a process shared with every other cabinet.
        return File(archive.Content, ClinicArchiveFormat.ContentType, archive.FileName);
    }

    /// <summary>
    /// What the cabinet can restore from without a file: the retained recovery points, newest first, plus when an
    /// archive last actually reached somebody (<c>clinic-recovery-points</c>).
    ///
    /// <para><b>Not gated on <c>BacksUpItsOwnData</c></b>, for <see cref="DownloadArchive"/>'s reason: recovery points
    /// are tenant-filtered per-clinic archives, not a <c>pg_dump</c>, so they exist on every deployment kind.</para>
    ///
    /// <para>No step-up. Listing the moments a cabinet could be restored from discloses no record — the sizes and row
    /// counts of its own points — and requiring a password to *read* the list would make the confirmation on the
    /// action itself read as noise.</para>
    /// </summary>
    [HttpGet("recovery-points")]
    [AllowsWithoutSubscription(
        "Recovering records that already exist is not recording new work (AC-8, the AC-4.2 argument) — and a cabinet "
        + "that has just lost data is exactly the one that must be able to see what it can restore from.")]
    public async Task<ActionResult<RecoveryPointsDto>> RecoveryPoints(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListRecoveryPointsQuery(), cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Restores the cabinet from one of its own retained recovery points.
    ///
    /// <para>⚠️ <b>The same step-up action as the upload restore</b> (<see cref="RestoreStepUpAction"/>), deliberately:
    /// it is the same operation on the same records, so a separate action name would suggest a different one. And it
    /// is required — a stored point is one click closer than an upload, so without the confirmation this would be a
    /// quieter route to the same records than the one FR-4.3 guards.</para>
    ///
    /// <para>⚠️ <b>Additive like every other restore</b>: missing rows come back with their original ids, rows still
    /// present are untouched, and rows that *differ* are skipped and counted apart. A scheduled point carries no
    /// files, and the report says so rather than reporting « 0 fichier ».</para>
    /// </summary>
    [HttpPost("recovery-points/{recoveryPointId:guid}/restore")]
    [EnableRateLimiting(RateLimiting.ArchivePolicy)]
    [AllowsWithoutSubscription(
        "Putting back records the cabinet already had is not recording new work (AC-8) — and an expired cabinet that "
        + "has also lost data is exactly the one that must be able to recover it.")]
    public async Task<ActionResult<ClinicArchiveRestoreReport>> RestoreFromRecoveryPoint(
        Guid recoveryPointId,
        [FromHeader(Name = StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        var refusal = RequireStepUp(confirmation, RestoreStepUpAction);
        if (refusal != null)
        {
            return refusal;
        }

        var result = await _mediator.Send(
            new RestoreFromRecoveryPointCommand { RecoveryPointId = recoveryPointId }, cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    private async Task RecordDeliveryAsync(ClinicArchiveFile archive, long bytes, bool delivered)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            // A fresh scope starts with no tenant scope at all, and the clinic query filters REFUSE an unset one
            // — they return nothing rather than everything (multi-tenant-cloud US-2). The ledger itself is
            // unfiltered, so today this write would work either way; declaring it is what keeps that true if the
            // row ever grows a filtered read, and it states which cabinet this belongs to at the one point in
            // the request where nobody is left to ask.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(archive.ClinicId);

            await ArchiveAccessLedger.RecordDeliveryAsync(
                // The clinic repository is passed so a DELIVERED archive also stamps the cabinet's own
                // « dernière archive téléchargée » — the fact the staleness alert reads, and the one thing the two
                // ledger rows cannot answer without matching their French prose.
                scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().Current,
                archive.ClinicId,
                archive.LedgerEntryId,
                delivered,
                bytes,
                DateTime.UtcNow,
                scope.ServiceProvider.GetRequiredService<IClinicRepository>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not record the delivery of archive {LedgerEntryId} for clinic {ClinicId}; the request "
                + "itself is recorded.",
                archive.LedgerEntryId,
                archive.ClinicId);
        }
    }

    /// <summary>
    /// Restores an archive into this cabinet: missing records are re-inserted with their original ids, records
    /// still present are left untouched, and the result is reported per entity.
    ///
    /// <para>⚠️ <b>Additive, and nothing is ever overwritten.</b> A record that exists but *differs* from the
    /// archive is skipped and counted apart, so work done since the archive was taken cannot be rolled back
    /// (AC-4) — and a restore run twice changes nothing the second time (AC-2).</para>
    ///
    /// <para>⚠️ <b>Every refusal carries a code beside its French sentence</b> — <c>archive_invalid</c>,
    /// <c>archive_clinic_mismatch</c>, <c>archive_schema_unsupported</c> — because the client branches on the
    /// code and the user reads the prose. Recovering an outcome by matching prose is the defect this repository
    /// deleted in <c>adoption-gaps-remediation</c>.</para>
    /// </summary>
    [HttpPost("archive/restore")]
    [DisableRequestSizeLimit]
    [ArchiveUploadLimit]
    [EnableRateLimiting(RateLimiting.ArchivePolicy)]
    [AllowsWithoutSubscription(
        "Putting back records the cabinet already had is not recording new work (AC-8) — and an expired cabinet "
        + "that has also lost data is exactly the one that must be able to recover it.")]
    public async Task<ActionResult<ClinicArchiveRestoreReport>> RestoreArchive(
        [FromForm] IFormFile archive,
        [FromHeader(Name = StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        // FR-4.3 — checked before the form is read, so a request with no confirmation resolves no handler and
        // touches no row, the shape `UsersController.ResetTotp` already uses.
        var stepUp = RequireStepUp(confirmation, RestoreStepUpAction);
        if (stepUp != null)
        {
            return stepUp;
        }

        var refusal = ValidateUpload(archive);
        if (refusal != null)
        {
            return refusal;
        }

        await using var stream = archive.OpenReadStream();

        var result = await _mediator.Send(
            new RestoreClinicArchiveCommand { Archive = stream }, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The two things about an upload only the transport can know — that a file arrived, and how big it is.
    /// Everything about the archive's *contents* is the handler's business.
    ///
    /// <para>⚠️ It reads the same ceiling <c>[ArchiveUploadLimit]</c> applies to the form reader, so the sentence
    /// a caller gets names the limit that actually refused them. Without that attribute this check was
    /// unreachable above 128 MB — the reader's own default threw during binding, before the action ran.</para>
    /// </summary>
    private ActionResult? ValidateUpload(IFormFile? archive)
    {
        if (archive == null || archive.Length == 0)
        {
            return Failure("Aucun fichier n'a été envoyé.");
        }

        var maxMb = ArchiveUploadLimit.MaxSizeMb(_configuration);

        return archive.Length > ArchiveUploadLimit.MaxBytes(_configuration)
            ? Failure($"L'archive dépasse la taille maximale acceptée ({maxMb} Mo).")
            : null;
    }
}
