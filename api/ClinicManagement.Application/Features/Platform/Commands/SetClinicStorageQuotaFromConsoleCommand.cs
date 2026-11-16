using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor raises — or lowers, or returns to the default — one cabinet's storage ceiling
/// (<c>features/camera-photo-import</c>, phase 3). A practice that photographs every patient outgrows the default, and the
/// « contactez APEXA pour augmenter l'espace » every refusal ends with needs something the vendor can actually do.
///
/// <para>⚠️ <b>A mandatory motif, journaled</b> on the console's access ledger in the same save — the cabinet keeps only
/// the new figure, so « pourquoi ce cabinet a-t-il 25 Go ? » is answered there or nowhere. An unchanged figure writes
/// nothing, so a double-click cannot put two rows in the journal.</para>
///
/// <para>⚠️ Lowering below what the cabinet already stores is allowed: nothing is deleted, the next upload is refused
/// with the ordinary « plein » sentence. The console shows the figures before the press.</para>
///
/// <para>⚠️ <b>Every refusal is answered before anything is read</b>, each with its code: a quota out of range and a motif
/// longer than the journal holds used to reach the catch-all as exceptions. And a changed ceiling re-evaluates the
/// cabinet's 80 / 95 % bell after the commit — raising it is the remedy the bell names, so it must not keep ringing.</para>
/// </summary>
public class SetClinicStorageQuotaFromConsoleCommand : IRequest<Result<PlatformStorageQuotaChangedDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The new ceiling in bytes, or null for the deployment's default.</summary>
    public long? QuotaBytes { get; set; }

    public string? Reason { get; set; }
}

public class SetClinicStorageQuotaFromConsoleCommandHandler
    : IRequestHandler<SetClinicStorageQuotaFromConsoleCommand, Result<PlatformStorageQuotaChangedDto>>
{
    public const string UnknownClinicCode = "clinic_not_found";
    public const string NotEnforcedCode = "storage_not_enforced";
    public const string QuotaOutOfRangeCode = "storage_quota_out_of_range";
    public const string ReasonTooLongCode = "reason_too_long";

    public const string ReasonRequiredError =
        "Indiquez le motif du changement : il reste inscrit au journal, et c'est la seule réponse à « pourquoi ce "
        + "quota ? » que vos collègues pourront lire ensuite.";

    public const string NotEnforcedError =
        "Ce déploiement n'impose aucun plafond de stockage : le cabinet stocke ses fichiers sur son propre poste.";

    public static readonly string ReasonTooLongError =
        $"Le motif ne peut pas dépasser {PlatformAccessEntry.MaxReasonLength} caractères.";

    private readonly IClinicRepository _clinics;
    private readonly IPatientFileRepository _files;
    private readonly IClinicStoragePolicy _storage;
    private readonly ClinicStorageAllowance _allowance;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SetClinicStorageQuotaFromConsoleCommandHandler> _logger;

    public SetClinicStorageQuotaFromConsoleCommandHandler(
        IClinicRepository clinics,
        IPatientFileRepository files,
        IClinicStoragePolicy storage,
        ClinicStorageAllowance allowance,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<SetClinicStorageQuotaFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _files = files;
        _storage = storage;
        _allowance = allowance;
        _accessEntries = accessEntries;
        _session = session;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformStorageQuotaChangedDto>> Handle(
        SetClinicStorageQuotaFromConsoleCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared scope would read zero rows and call every cabinet unknown.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            if (!_storage.Enforced)
            {
                return Result<PlatformStorageQuotaChangedDto>.Failure(NotEnforcedError, NotEnforcedCode);
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PlatformStorageQuotaChangedDto>.Failure(ReasonRequiredError);
            }

            var reason = request.Reason.Trim();
            if (reason.Length > PlatformAccessEntry.MaxReasonLength)
            {
                return Result<PlatformStorageQuotaChangedDto>.Failure(ReasonTooLongError, ReasonTooLongCode);
            }

            if (!Clinic.IsValidStorageQuota(request.QuotaBytes))
            {
                return Result<PlatformStorageQuotaChangedDto>.Failure(
                    Clinic.StorageQuotaRangeError, QuotaOutOfRangeCode);
            }

            var clinic = await _clinics.GetByIdAsync(request.ClinicId, cancellationToken);
            if (clinic is null)
            {
                return Result<PlatformStorageQuotaChangedDto>.Failure("Cabinet introuvable.", UnknownClinicCode);
            }

            var accountId = PlatformAccessLedger.RequireAccountId(_session);
            if (clinic.SetStorageQuota(request.QuotaBytes))
            {
                await PlatformAccessLedger.RecordAsync(
                    _accessEntries,
                    _session,
                    clinic.Id,
                    clinic.Name,
                    PlatformAccessAction.StorageQuotaChanged,
                    DateTime.UtcNow,
                    cancellationToken,
                    reason: reason);

                await _clinics.UpdateAsync(clinic, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Console account {AccountId} set clinic {ClinicId} storage quota to {QuotaBytes}",
                    accountId, clinic.Id, request.QuotaBytes);

                // Post-commit, best-effort: a raised ceiling withdraws or lowers the bell, a lowered one may raise it.
                await _allowance.AfterChangeAsync(clinic.Id, cancellationToken);
            }

            var used = await _files.GetHostedBytesAsync(clinic.Id, cancellationToken);
            return Result<PlatformStorageQuotaChangedDto>.Success(new PlatformStorageQuotaChangedDto(
                ClinicId: clinic.Id,
                StorageUsedBytes: used,
                StorageQuotaBytes: clinic.StorageQuotaBytes ?? _storage.QuotaBytes,
                StorageQuotaIsCustom: clinic.StorageQuotaBytes is not null));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error setting the storage quota of clinic {ClinicId} from the console", request.ClinicId);
            return Result<PlatformStorageQuotaChangedDto>.Failure("Erreur lors de la modification du quota de stockage.");
        }
    }
}
