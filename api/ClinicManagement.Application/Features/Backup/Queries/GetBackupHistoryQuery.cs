using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup.Queries;

/// <summary>
/// « Historique des sauvegardes » (L4d) — one page of the clinic's backup attempts, newest first, plus the
/// resolved destination folder and the moment of the last success.
///
/// <para><b>Always paged</b>, like the audit ledger and unlike the list reads: the table grows with every night
/// the practice operates and there is no caller for all of it. Omitting the paging parameters gets the first
/// page, not everything.</para>
///
/// <para>The three extras beside the page are what the settings screen actually leads with. « Dernière sauvegarde
/// réussie » is the headline the spec asks for, and it is a *different question* from « the newest row »: a week
/// of nightly failures leaves the newest row failed and the last success seven days back, and only saying both
/// distinguishes « nobody has backed up » from « it has been trying and failing ». The resolved destination is
/// here because the settings panel promises « laissez vide pour utiliser le dossier par défaut du serveur » and
/// could not say which folder that is.</para>
/// </summary>
public class GetBackupHistoryQuery : IRequest<Result<BackupHistoryDto>>
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

/// <summary>The « Sauvegarde » panel in one read.</summary>
/// <param name="Page">The clinic's recorded attempts, newest first.</param>
/// <param name="LastSuccessAt">
/// When the clinic last had a <b>verified</b> backup, or null if it never has. The headline figure.
/// </param>
/// <param name="LastSuccessSizeBytes">The size of that backup, so « 4 Ko » is visible as the disaster it is.</param>
/// <param name="DefaultDestination">
/// The folder a backup with no explicit destination is written to — resolved by the service, never re-derived
/// client-side, so what the screen prints is where the file actually goes.
/// </param>
/// <param name="StaleAfterHours">The clinic's own staleness threshold, so the screen can say when it will warn.</param>
/// <param name="BackupEnabled">Whether the nightly schedule is on.</param>
/// <param name="BackupHourLocal">The clinic-local hour it runs at.</param>
/// <param name="RetentionCount">How many folders are kept.</param>
/// <param name="ManagedByHost">
/// <b>This deployment does not back itself up — its host does</b>, and the screen says so instead of offering a
/// button that cannot work. True on the two hosted kinds (see <c>DeploymentProfile.BacksUpItsOwnData</c>): the
/// <c>deploy/</c> <c>backup</c> sidecar already dumps the database and the object store off-server on a schedule,
/// and one database holds every cabinet, so an in-app <c>pg_dump</c> would be a cross-tenant read.
///
/// <para>⚠️ It is a field on this DTO rather than the screen inferring it from an empty page, because « aucune
/// sauvegarde » and « les sauvegardes ne sont pas gérées ici » are the same picture and opposite facts — and the
/// first is the one that would send an owner looking for a button to press. The same reasoning as the portfolio's
/// « jamais mesuré » and the caisse's refund split.</para>
///
/// <para>When true, every other field is the neutral empty value and none of them is a claim: there is no schedule
/// to report and no destination on this machine to name.</para>
/// </param>
public record BackupHistoryDto(
    PagedResult<BackupRunDto> Page,
    DateTime? LastSuccessAt,
    long? LastSuccessSizeBytes,
    string DefaultDestination,
    int StaleAfterHours,
    bool BackupEnabled,
    int BackupHourLocal,
    int RetentionCount,
    bool ManagedByHost = false)
{
    /// <summary>
    /// The whole response for a deployment whose backups belong to its host. Built here rather than in the
    /// controller so the shape has one definition — a hand-assembled literal at the call site is how
    /// <c>ManagedByHost</c> would end up beside a `backupEnabled: true` nobody meant.
    /// </summary>
    public static BackupHistoryDto ManagedByTheHost() => new(
        new PagedResult<BackupRunDto>(Array.Empty<BackupRunDto>(), page: 1, pageSize: 0, totalCount: 0),
        LastSuccessAt: null,
        LastSuccessSizeBytes: null,
        DefaultDestination: string.Empty,
        StaleAfterHours: 0,
        BackupEnabled: false,
        BackupHourLocal: 0,
        RetentionCount: 0,
        ManagedByHost: true);
}

public class GetBackupHistoryQueryHandler : IRequestHandler<GetBackupHistoryQuery, Result<BackupHistoryDto>>
{
    private readonly IBackupRunRepository _backupRuns;
    private readonly IClinicRepository _clinicRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IBackupService _backupService;

    public GetBackupHistoryQueryHandler(
        IBackupRunRepository backupRuns,
        IClinicRepository clinicRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IBackupService backupService)
    {
        _backupRuns = backupRuns;
        _clinicRepository = clinicRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _backupService = backupService;
    }

    public async Task<Result<BackupHistoryDto>> Handle(
        GetBackupHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<BackupHistoryDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (caller == null)
            {
                return Result<BackupHistoryDto>.Failure("Utilisateur introuvable.");
            }

            // Defence in depth behind the controller's AdminOnly policy, the same shape BackupNowCommand uses.
            if (!caller.IsAdmin())
            {
                return Result<BackupHistoryDto>.Failure(
                    "Seuls les administrateurs peuvent consulter l'historique des sauvegardes.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<BackupHistoryDto>.Failure("Cabinet introuvable.");
            }

            var page = await _backupRuns.GetHistoryAsync(
                caller.ClinicId, PageRequest.From(request.Page, request.PageSize), cancellationToken);

            var lastSuccess = await _backupRuns.GetLastSuccessfulAsync(caller.ClinicId, cancellationToken);

            return Result<BackupHistoryDto>.Success(new BackupHistoryDto(
                new PagedResult<BackupRunDto>(
                    page.Items.Select(BackupRunDto.From).ToList(),
                    page.Page,
                    page.PageSize,
                    page.TotalCount),
                lastSuccess?.CompletedAt ?? lastSuccess?.StartedAt,
                lastSuccess?.SizeBytes,
                _backupService.ResolveDestinationRoot(null),
                clinic.BackupStaleAfterHours,
                clinic.BackupEnabled,
                clinic.BackupHourLocal,
                clinic.BackupRetentionCount));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // French, and without the raw exception text — a destination path in an error message is an
            // information leak on a screen a secretary can reach by URL.
            return Result<BackupHistoryDto>.Failure("Erreur lors de la récupération de l'historique des sauvegardes.");
        }
    }
}
