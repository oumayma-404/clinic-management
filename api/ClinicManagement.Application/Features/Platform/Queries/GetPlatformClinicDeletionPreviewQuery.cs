using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Queries;

/// <summary>
/// What deleting this cabinet would remove (<c>clinic-account-removal</c>) — read by the console's confirmation
/// panel before the vendor can type the name.
///
/// <para><b>⚠️ The figures come from the deletion's own plan</b> (<c>IClinicPurge.CountAsync</c>), never from a
/// separate set of counting queries. Two implementations of « what does this cabinet hold » would agree for a
/// while and then not, and the direction of that drift is the dangerous one: a preview naming fewer tables than
/// the deletion empties is a destructive click made on an understatement.</para>
///
/// <para>⚠️ <b>A Query that writes nothing, unlike <c>GetPlatformClinicDetailQuery</c> beside it.</b> That one
/// records a <c>ViewedClinic</c> row because opening a cabinet's file is the thing being audited; this is a
/// dialog's own read, fired as a panel opens, and a ledger row per open would drown the row that matters — the
/// deletion's. The deletion records itself.</para>
///
/// <para>⚠️ It is deliberately <b>not</b> refused for a cabinet holding money or patients. That was asked and
/// answered: the barrier is the typed name, the motif and the journal, not a server-side rule about what a
/// cabinet may hold — a refusal keyed on « has encaissements » would make the test cabinets this exists for
/// undeletable the moment somebody records a payment in one.</para>
/// </summary>
public class GetPlatformClinicDeletionPreviewQuery : IRequest<Result<PlatformClinicDeletionPreviewDto>>
{
    public Guid ClinicId { get; set; }
}

public class GetPlatformClinicDeletionPreviewQueryHandler
    : IRequestHandler<GetPlatformClinicDeletionPreviewQuery, Result<PlatformClinicDeletionPreviewDto>>
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicPurge _purge;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GetPlatformClinicDeletionPreviewQueryHandler> _logger;

    public GetPlatformClinicDeletionPreviewQueryHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicPurge purge,
        ITenantScope tenantScope,
        ILogger<GetPlatformClinicDeletionPreviewQueryHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _purge = purge;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformClinicDeletionPreviewDto>> Handle(
        GetPlatformClinicDeletionPreviewQuery request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-clinic scope reads zero rows with no error, which
        // here would preview every cabinet in the deployment as empty — the most dangerous possible understatement.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        try
        {
            var clinic = await _clinics.GetByIdAsync(request.ClinicId, cancellationToken);

            if (clinic is null)
            {
                return Result<PlatformClinicDeletionPreviewDto>.Failure(
                    ClinicDeletionRefusals.UnknownClinic, ClinicDeletionRefusals.UnknownClinicCode);
            }

            // The addresses first: the two tables no clinic id can reach are found by them, so a census taken
            // without them would understate the deletion by exactly the rows that hold the practice's own name.
            var freed = await ClinicDeletionAddresses.FreedByDeletingAsync(_users, clinic.Id, cancellationToken);
            var census = await _purge.CountAsync(clinic.Id, freed, cancellationToken);

            return Result<PlatformClinicDeletionPreviewDto>.Success(new PlatformClinicDeletionPreviewDto(
                ClinicId: clinic.Id,
                ClinicName: clinic.Name,
                FreedEmails: freed,
                Tallies: PlatformClinicFootprint.Tallies(census),
                RowsTotal: census.Rows,
                FileCount: PlatformClinicFootprint.FileCount(census),
                FileBytes: census.FileBytes,
                // The panel asks for what the check will accept, and the two are decided in one place.
                ConfirmationKind: ClinicDeletionRefusals.KindFor(freed).ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing the deletion of clinic {ClinicId}", request.ClinicId);
            return Result<PlatformClinicDeletionPreviewDto>.Failure(
                "Erreur lors de la lecture de ce que contient ce cabinet.");
        }
    }
}
