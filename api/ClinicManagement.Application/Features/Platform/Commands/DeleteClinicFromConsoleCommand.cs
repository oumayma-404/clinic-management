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
/// The vendor deletes a cabinet — every row it holds, every account, every file (<c>clinic-account-removal</c>).
/// The console's fourth write, and the only irreversible one.
///
/// <para><b>Why it exists.</b> An e-mail is unique per install, filtered on being password-backed, so a verified
/// account keeps its address for ever: a cabinet created to try the product out holds one of the few addresses its
/// owner has, and nothing in the product could release it. The other half is the portfolio — a hosted deployment
/// accumulating abandoned cabinets has no way to say which of its rows are real practices.</para>
///
/// <para><b>⚠️ What guards it, and what deliberately does not.</b> The barriers are the <b>typed cabinet name</b>
/// (checked server-side, so a mis-clicked row in the list cannot be deleted whatever the client sent), a
/// <b>mandatory motif</b>, and a journal row written in the same transaction. There is deliberately <b>no refusal
/// for a cabinet holding money or patients</b>: that was asked and decided — such a rule would make the test
/// cabinets this exists for undeletable the moment somebody records a payment in one, and it would push the real
/// deletions back to SSH, where nothing is recorded at all.</para>
///
/// <para><b>⚠️ The order is the design.</b> Every refusal is raised before the transaction opens; the addresses are
/// read <i>before</i> the rows go (afterwards there is nothing to read); the rows and the journal row commit
/// together; and the blob sweep runs <b>after</b> the commit, because an object store cannot be rolled back — a
/// deletion that failed on its last statement having already removed the radiographs would leave a cabinet whose
/// records point at nothing.</para>
///
/// <para>⚠️ <b>The journal row is what remains.</b> <c>PlatformAccessEntry</c> declares no foreign key to
/// <c>Clinics</c> and carries the cabinet's name denormalised precisely so this action can be read years later; the
/// motif is the only thing that will ever distinguish « cabinet de test » from « le cabinet a demandé la
/// suppression de ses données ».</para>
/// </summary>
public class DeleteClinicFromConsoleCommand : IRequest<Result<PlatformClinicDeletedDto>>
{
    public Guid ClinicId { get; set; }

    /// <summary>The cabinet's name as the vendor typed it. Refused unless it names this cabinet.</summary>
    public string? ConfirmationName { get; set; }

    /// <summary>Mandatory. Lands on the journal row, which is all that survives the cabinet.</summary>
    public string? Reason { get; set; }
}

public class DeleteClinicFromConsoleCommandHandler
    : IRequestHandler<DeleteClinicFromConsoleCommand, Result<PlatformClinicDeletedDto>>
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicPurge _purge;
    private readonly IFileStorage _storage;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<DeleteClinicFromConsoleCommandHandler> _logger;

    public DeleteClinicFromConsoleCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicPurge purge,
        IFileStorage storage,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        ILogger<DeleteClinicFromConsoleCommandHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _purge = purge;
        _storage = storage;
        _accessEntries = accessEntries;
        _session = session;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformClinicDeletedDto>> Handle(
        DeleteClinicFromConsoleCommand request, CancellationToken cancellationToken)
    {
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<PlatformClinicDeletedDto>.Failure(
                ClinicDeletionRefusals.ReasonRequired, ClinicDeletionRefusals.ReasonRequiredCode);
        }

        var clinic = await _clinics.GetByIdAsync(request.ClinicId, cancellationToken);

        if (clinic is null)
        {
            return Result<PlatformClinicDeletedDto>.Failure(
                ClinicDeletionRefusals.UnknownClinic, ClinicDeletionRefusals.UnknownClinicCode);
        }

        // Against the cabinet the id actually resolved to, never against a name the client also sent: the failure
        // this catches is a wrong row, and a client comparing two of its own strings would agree with itself.
        if (!ClinicDeletionRefusals.NamesTheClinic(request.ConfirmationName, clinic.Name))
        {
            return Result<PlatformClinicDeletedDto>.Failure(
                ClinicDeletionRefusals.NameMismatch, ClinicDeletionRefusals.NameMismatchCode);
        }

        // Resolved before anything is written, `SetClinicSuspensionFromConsoleCommand`'s rule: « nous ne savons pas
        // qui » has to stop the write rather than be discovered while recording it.
        var accountId = PlatformAccessLedger.RequireAccountId(_session);

        // Read while the accounts still exist. Afterwards the answer is structurally unavailable, and this list is
        // the one thing the vendor came for.
        var freed = await ClinicDeletionAddresses.FreedByDeletingAsync(_users, clinic.Id, cancellationToken);

        var clinicId = clinic.Id;
        var clinicName = clinic.Name;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        ClinicPurgeCensus census;

        try
        {
            census = await _purge.PurgeAsync(clinicId, freed, cancellationToken);

            await PlatformAccessLedger.RecordAsync(
                _accessEntries,
                _session,
                clinicId,
                clinicName,
                PlatformAccessAction.DeletedClinic,
                DateTime.UtcNow,
                cancellationToken,
                reason: request.Reason);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            // A conflict here is not a business outcome to report as one — nothing was deleted, and the middleware's
            // 409 is what tells the console to re-read rather than to re-word its own error.
            if (ex is ConflictException)
            {
                throw;
            }

            _logger.LogError(ex, "Error deleting clinic {ClinicId} from the console", clinicId);

            return Result<PlatformClinicDeletedDto>.Failure(
                "La suppression a échoué et rien n'a été supprimé : le cabinet est intact. "
                + "Réessayez, et si cela se reproduit lisez les journaux du serveur avant d'insister.");
        }

        // After the commit, and never inside it: an object store has no rollback, so a sweep that ran first would
        // take a cabinet's radiographs away from a deletion that then refused. Best effort by contract — the rows
        // are gone, so an unremovable blob is wasted bytes and not a wrong record.
        var filesDeleted = 0;

        try
        {
            filesDeleted = await _storage.DeleteByClinicAsync(clinicId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Clinic {ClinicId} was deleted but its files could not be cleared", clinicId);
        }

        _logger.LogWarning(
            "Console account {AccountId} deleted clinic {ClinicId} ({ClinicName}): {Rows} rows, {Files} objects, "
            + "{Addresses} addresses freed",
            accountId, clinicId, clinicName, census.Rows, filesDeleted, freed.Count);

        return Result<PlatformClinicDeletedDto>.Success(new PlatformClinicDeletedDto(
            ClinicId: clinicId,
            ClinicName: clinicName,
            FreedEmails: freed,
            Tallies: PlatformClinicFootprint.Tallies(census),
            RowsDeleted: census.Rows,
            FilesDeleted: filesDeleted,
            // The two address-keyed tables, reported as one figure: what the vendor is being told is « nothing is
            // still holding these addresses », and which of the two tables held a row is not their question.
            AddressRowsCleared: (int)(census.RowsOf(nameof(ClinicSignup))
                                      + census.RowsOf(nameof(PasswordResetRequest)))));
    }
}
