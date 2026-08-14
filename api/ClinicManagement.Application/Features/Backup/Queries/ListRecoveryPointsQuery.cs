using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Backup.Queries;

/// <summary>One retained recovery point, as the « Sauvegarde » card lists it.</summary>
/// <param name="RowCount">
/// How many rows it carries. The figure worth showing beside a size: a point whose row count collapsed overnight is
/// the one an owner must not restore blindly, and « 3 tables » on a cabinet with forty is a detectable disaster that
/// a byte count cannot express.
/// </param>
/// <param name="CarriesFiles">
/// Whether the point includes the blobs. Always <c>false</c> for a scheduled one — stated rather than left for the
/// reader to infer from a zero, because « pas de fichiers dans cette archive » and « les fichiers n'ont pas pu être
/// lus » are opposite facts.
/// </param>
public sealed record RecoveryPointDto(
    Guid Id,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string Outcome,
    bool IsRestorable,
    bool CarriesFiles,
    long? SizeBytes,
    int? TableCount,
    int? RowCount,
    string? Error);

/// <summary>
/// What the cabinet can restore from, and what its own off-server copy looks like
/// (<c>clinic-recovery-points</c>).
/// </summary>
/// <param name="LastArchiveDownloadedAtUtc">
/// When an archive last <b>reached</b> somebody. Null on a cabinet that has never taken one — the screen says
/// « jamais », never a date it invented.
/// </param>
/// <param name="ArchiveStaleAfterDays">
/// The threshold the alert fires on, served rather than restated in the browser, so the card and the bell cannot
/// disagree about when a copy has gone stale.
/// </param>
public sealed record RecoveryPointsDto(
    IReadOnlyList<RecoveryPointDto> Points,
    DateTime? LastArchiveDownloadedAtUtc,
    int ArchiveStaleAfterDays,
    int RetentionCount);

/// <summary>
/// Lists the cabinet's recovery points, newest first.
///
/// <para><b>A query, so it stays off <c>RealtimeBroadcastBehavior</c></b> — which derives its key from the namespace,
/// and a « Backup » broadcast on every card render would tell every open browser in the practice that something
/// changed when nothing did.</para>
///
/// <para><b>Capped rather than paged</b>: retention keeps <see cref="ClinicRecoveryPoint.RetentionCount"/> succeeded
/// points, so the list is bounded by construction and a pager over seven rows would be furniture. The cap is
/// deliberately larger than the retention count, because a run of *failures* is not pruned and is exactly what
/// somebody opening this list needs to see all of.</para>
/// </summary>
public class ListRecoveryPointsQuery : IRequest<Result<RecoveryPointsDto>>
{
}

public class ListRecoveryPointsQueryHandler : IRequestHandler<ListRecoveryPointsQuery, Result<RecoveryPointsDto>>
{
    /// <summary>Enough to show every retained point plus a bad fortnight of failures behind them.</summary>
    private const int Limit = 30;

    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRecoveryPointRepository _points;

    public ListRecoveryPointsQueryHandler(
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IClinicContext clinicContext,
        IClinicRecoveryPointRepository points)
    {
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _clinicContext = clinicContext;
        _points = points;
    }

    public async Task<Result<RecoveryPointsDto>> Handle(
        ListRecoveryPointsQuery request, CancellationToken cancellationToken)
    {
        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Result<RecoveryPointsDto>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
        if (caller == null)
        {
            return Result<RecoveryPointsDto>.Failure("Utilisateur introuvable.");
        }

        // Defense in depth behind the controller's AdminOnly policy, the shape every sibling here uses. Listing the
        // points is listing the moments a cabinet's whole record can be put back from.
        if (!caller.IsAdmin())
        {
            return Result<RecoveryPointsDto>.Failure(
                "Seuls les administrateurs peuvent consulter les points de restauration.");
        }

        var clinic = await _clinicRepository.GetByIdAsync(caller.ClinicId, cancellationToken);
        if (clinic == null)
        {
            return Result<RecoveryPointsDto>.Failure("Cabinet introuvable.");
        }

        var points = await _points.ListAsync(caller.ClinicId, Limit, cancellationToken);

        return Result<RecoveryPointsDto>.Success(new RecoveryPointsDto(
            points.Select(Map).ToList(),
            clinic.LastArchiveDownloadedAtUtc,
            ClinicRecoveryPoint.ArchiveStaleAfterDays,
            ClinicRecoveryPoint.RetentionCount));
    }

    private static RecoveryPointDto Map(ClinicRecoveryPoint point) => new(
        point.Id,
        point.StartedAt,
        point.CompletedAt,
        point.Outcome.ToString(),
        point.IsRestorable,
        point.Contents == ClinicArchiveContents.RowsAndFiles,
        point.SizeBytes,
        point.TableCount,
        point.RowCount,
        point.Error);
}
