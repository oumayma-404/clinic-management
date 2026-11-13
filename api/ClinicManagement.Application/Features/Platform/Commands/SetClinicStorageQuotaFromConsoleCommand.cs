using ClinicManagement.Application.Common.Exceptions;
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

    public const string ReasonRequiredError =
        "Indiquez le motif du changement : il reste inscrit au journal, et c'est la seule réponse à « pourquoi ce "
        + "quota ? » que vos collègues pourront lire ensuite.";

    public const string NotEnforcedError =
        "Ce déploiement n'impose aucun plafond de stockage : le cabinet stocke ses fichiers sur son propre poste.";

    private readonly IClinicRepository _clinics;
    private readonly IPatientFileRepository _files;
    private readonly IClinicStoragePolicy _storage;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SetClinicStorageQuotaFromConsoleCommandHandler> _logger;

    public SetClinicStorageQuotaFromConsoleCommandHandler(
        IClinicRepository clinics,
        IPatientFileRepository files,
        IClinicStoragePolicy storage,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<SetClinicStorageQuotaFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _files = files;
        _storage = storage;
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
                    reason: request.Reason!.Trim());

                await _clinics.UpdateAsync(clinic, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Console account {AccountId} set clinic {ClinicId} storage quota to {QuotaBytes}",
                    accountId, clinic.Id, request.QuotaBytes);
            }

            var used = await _files.GetHostedBytesAsync(clinic.Id, cancellationToken);
            return Result<PlatformStorageQuotaChangedDto>.Success(new PlatformStorageQuotaChangedDto(
                ClinicId: clinic.Id,
                StorageUsedBytes: used,
                StorageQuotaBytes: clinic.StorageQuotaBytes ?? _storage.QuotaBytes,
                StorageQuotaIsCustom: clinic.StorageQuotaBytes is not null));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<PlatformStorageQuotaChangedDto>.Failure(
                $"Le quota doit être compris entre {Clinic.MinStorageQuotaBytes / (1024L * 1024 * 1024)} Go et "
                + $"{Clinic.MaxStorageQuotaBytes / (1024L * 1024 * 1024 * 1024)} To.");
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error setting the storage quota of clinic {ClinicId} from the console", request.ClinicId);
            return Result<PlatformStorageQuotaChangedDto>.Failure("Erreur lors de la modification du quota de stockage.");
        }
    }
}
