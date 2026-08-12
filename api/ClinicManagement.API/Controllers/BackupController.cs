using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Application.Features.Backup.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Filters;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;
    private readonly IConfiguration _configuration;

    public BackupController(IMediator mediator, DeploymentProfile deployment, IConfiguration configuration)
    {
        _mediator = mediator;
        _deployment = deployment;
        _configuration = configuration;
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
    [HttpGet("archive")]
    [AllowsWithoutSubscription(
        "A cabinet must always be able to take its own data out — recovering records that already exist is not "
        + "recording new work (AC-8, the AC-4.2 argument).")]
    public async Task<IActionResult> DownloadArchive(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new BuildClinicArchiveQuery(), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        var archive = result.Value!;

        // The stream overload, so the framework copies the temp file to the response in chunks and disposes it
        // afterwards. `File(byte[], …)` would hold the whole archive — twenty years of radiographs — in memory
        // for the life of the response, on a process shared with every other cabinet.
        return File(archive.Content, ClinicArchiveFormat.ContentType, archive.FileName);
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
    [AllowsWithoutSubscription(
        "Putting back records the cabinet already had is not recording new work (AC-8) — and an expired cabinet "
        + "that has also lost data is exactly the one that must be able to recover it.")]
    public async Task<ActionResult<ClinicArchiveRestoreReport>> RestoreArchive(
        [FromForm] IFormFile archive, CancellationToken cancellationToken)
    {
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
