using System.IO.Compression;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>
/// The vendor puts a cabinet back from the archive its owner kept — <b>re-creating the practice at the archive's
/// own clinic id</b> and minting a fresh administrator for it
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>Why this door exists at all.</b> The cabinet's own « Restaurer » needs somebody signed in, and the
/// case this feature is really written for is the one where nobody can sign in: the cabinet is gone, and its staff
/// accounts with it. Then the only party who can act is the one holding the database. This is that path, and it is
/// the only one that works when the accounts are gone too.</para>
///
/// <para><b>⚠️ The cabinet is not provisioned — it is restored.</b> Running
/// <see cref="LocalClinicProvisioning"/> would create a <i>new</i> <c>Clinic</c> row and the archive's own would
/// then be « présent mais différent », i.e. skipped: the practice would come back with its patients and its money
/// but with a blank name, no billing settings, no working hours and no logo. So the <c>Clinic</c> row is restored
/// like every other row, from the archive, at its own id — and only the pieces an archive deliberately does
/// <i>not</i> carry are created here: the administrator (password hashes do not travel in a file on a laptop) and
/// the entitlement (the vendor's money, never the cabinet's — <c>clinic-subscription</c> FR-2).</para>
///
/// <para>⚠️ <b>A cabinet that is still live is refused</b> (<c>clinic_exists</c>), and that is not the same rule as
/// the cabinet path's clinic-id check. There the archive might belong to somebody else; here it belongs to exactly
/// the right cabinet and the cabinet is <i>already there</i> — at which point the practice's own admin can restore
/// it themselves, with their own eyes on the result, and the vendor minting a second administrator into a working
/// practice is the wrong move whatever the archive says.</para>
///
/// <para>⚠️ <b>The archive's own <c>Clinic</c> row is validated before anything is staged, and the whole operation
/// is one transaction.</b> Both used to be missing, and together they made this door single-use in the worst way:
/// the live-cabinet guard keys on the manifest's <i>claim</i> while the row that lands comes from
/// <c>data/Clinic.json</c>'s own <c>Id</c>, so a hand-edited manifest inserted the practice at one id, re-stamped
/// every child to another, and only then met the « no <c>Clinic</c> row » refusal — after the commit. The result
/// was a cabinet's patients, invoices and files back under an id nothing points at, with no administrator, no
/// entitlement (FR-13), no journal row, and every retry answered <c>409 clinic_exists</c>: unrecoverable by either
/// door short of deleting the row in SQL. The same hole swallowed any fault between the apply and the final save —
/// a lost race on the e-mail index, an entitlement conflict, a container restart.</para>
/// </summary>
public class RestoreClinicFromArchiveCommand : IRequest<Result<PlatformClinicRestoredDto>>
{
    /// <summary>The uploaded <c>.zip</c>.</summary>
    public Stream? Archive { get; set; }

    /// <summary>Who the re-created cabinet's administrator will be. Their one-time password is returned once.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>That administrator's name, for the account and for the practice to recognise.</summary>
    public string? AdminFullName { get; set; }
}

public class RestoreClinicFromArchiveCommandHandler
    : IRequestHandler<RestoreClinicFromArchiveCommand, Result<PlatformClinicRestoredDto>>
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IPlatformAccessEntryRepository _accessEntries;
    private readonly IPlatformSessionContext _session;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly ILocalAuthService _localAuth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<RestoreClinicFromArchiveCommandHandler> _logger;

    public RestoreClinicFromArchiveCommandHandler(
        IClinicRepository clinics,
        IUserRepository users,
        IClinicSubscriptionRepository subscriptions,
        IPlatformAccessEntryRepository accessEntries,
        IPlatformSessionContext session,
        ISubscriptionPolicy subscriptionPolicy,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        ILocalAuthService localAuth,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        IAuditEntryRepository auditEntries,
        ITenantScope tenantScope,
        ILogger<RestoreClinicFromArchiveCommandHandler> logger)
    {
        _clinics = clinics;
        _users = users;
        _subscriptions = subscriptions;
        _accessEntries = accessEntries;
        _session = session;
        _subscriptionPolicy = subscriptionPolicy;
        _store = store;
        _fileStorage = fileStorage;
        _localAuth = localAuth;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _auditEntries = auditEntries;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    public async Task<Result<PlatformClinicRestoredDto>> Handle(
        RestoreClinicFromArchiveCommand request, CancellationToken cancellationToken)
    {
        // EC-12, as on every console path: an undeclared cross-clinic scope reads zero rows with no error — which
        // here would report a live cabinet as absent and re-create it on top of itself.
        PlatformTenantScope.EnsureDeclared(_tenantScope);

        if (request.Archive == null)
        {
            return Result<PlatformClinicRestoredDto>.Failure(
                "Aucun fichier n'a été envoyé.", ClinicArchiveFormat.InvalidCode);
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail) || string.IsNullOrWhiteSpace(request.AdminFullName))
        {
            return Result<PlatformClinicRestoredDto>.Failure(
                "L'adresse e-mail et le nom de l'administrateur du cabinet sont obligatoires.");
        }

        using var buffer = await ClinicArchiveRestorer.BufferAsync(request.Archive, cancellationToken);

        ZipArchive zip;

        try
        {
            zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return Result<PlatformClinicRestoredDto>.Failure(
                "Ce fichier n'est pas une archive lisible.", ClinicArchiveFormat.InvalidCode);
        }

        using (zip)
        {
            var read = ClinicArchivePackager.ReadManifest(zip);
            if (read.IsRefused)
            {
                return Result<PlatformClinicRestoredDto>.Failure(read.Error!, read.Code!);
            }

            var manifest = read.Manifest!;

            var live = await _clinics.GetByIdAsync(manifest.ClinicId, cancellationToken);
            if (live is not null)
            {
                return Result<PlatformClinicRestoredDto>.Failure(
                    $"Le cabinet « {live.Name} » existe toujours. Sa restauration se fait depuis « Paramètres » "
                    + "par son propre administrateur.",
                    ClinicArchiveFormat.ClinicExistsCode);
            }

            var existingAccount = await _users.GetByEmailAsync(request.AdminEmail!, cancellationToken);
            if (existingAccount != null)
            {
                // Caught here rather than at the partial unique index on the lowercased email, which would surface
                // as a 500 after the cabinet's rows had already been written.
                return Result<PlatformClinicRestoredDto>.Failure("Un compte existe déjà avec cet email.");
            }

            // ⚠️ Before anything is staged: the manifest's clinic id is the archive's *claim* and the row that
            // lands is `data/Clinic.json`'s own, so the guard below fired after the commit — see the type remarks.
            if (!ClinicArchivePackager.CarriesCabinetRecord(zip, manifest.ClinicId))
            {
                return Result<PlatformClinicRestoredDto>.Failure(
                    "Cette archive ne contient pas la fiche du cabinet qu'elle annonce ; il n'a pas pu être recréé.",
                    ClinicArchiveFormat.InvalidCode);
            }

            // ⚠️ One transaction over the rows, the administrator, the entitlement and the journal row — Parts 4/5'
            // shape, and « an unattributable action must not aboutir » applies hardest on this one.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var applied = await ClinicArchiveRestorer.ApplyAsync(
                    zip, manifest, manifest.ClinicId, _store, _fileStorage, _unitOfWork, _auditActor, _auditEntries,
                    _logger, cancellationToken);

                if (applied.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Result<PlatformClinicRestoredDto>.Failure(applied.Error!, applied.Code);
                }

                var report = applied.Value!;

                var restoredClinic = await _clinics.GetByIdAsync(manifest.ClinicId, cancellationToken);
                if (restoredClinic is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    return Result<PlatformClinicRestoredDto>.Failure(
                        "Cette archive ne contient pas la fiche du cabinet ; il n'a pas pu être recréé.",
                        ClinicArchiveFormat.InvalidCode);
                }

                var oneTimePassword = _localAuth.GenerateTemporaryPassword();

                var admin = User.CreateLocalUser(
                    restoredClinic.Id,
                    User.RoleAdmin,
                    request.AdminEmail!,
                    _localAuth.HashPassword(oneTimePassword),
                    request.AdminFullName!,
                    mustChangePassword: true);

                await _users.AddAsync(admin, cancellationToken);

                // FR-13's « no cabinet without an entitlement », through the companion's own helper rather than a
                // second answer to what a cabinet starts with. A restored cabinet gets the same opening cover a
                // new one does: what it had before is the vendor's own ledger, which an archive never carries.
                await LocalClinicProvisioning.StageEntitlementAsync(
                    restoredClinic.Id, _subscriptions, _subscriptionPolicy, cancellationToken);

                await PlatformAccessLedger.RecordAsync(
                    _accessEntries,
                    _session,
                    restoredClinic.Id,
                    restoredClinic.Name,
                    PlatformAccessAction.RestoredClinic,
                    DateTime.UtcNow,
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation(
                    "Console restored clinic {ClinicId} from an archive: {Restored} rows, {Blobs} blobs.",
                    restoredClinic.Id, report.TotalRestored, report.BlobsRestored);

                return Result<PlatformClinicRestoredDto>.Success(new PlatformClinicRestoredDto
                {
                    ClinicId = restoredClinic.Id,
                    ClinicName = restoredClinic.Name,
                    // `User.Email` is nullable on the type; this account was just created from a validated address.
                    AdminEmail = admin.Email ?? request.AdminEmail!,
                    OneTimePassword = oneTimePassword,
                    ArchivedAtUtc = report.ArchivedAtUtc,
                    Tables = PlatformClinicRestoredDto.TablesOf(report),
                    BlobsRestored = report.BlobsRestored,
                    Warnings = report.Warnings,
                });
            }
            catch (Exception ex) when (ex is not ConflictException)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Console restore failed for clinic {ClinicId}.", manifest.ClinicId);

                return Result<PlatformClinicRestoredDto>.Failure(
                    "La restauration a échoué. Aucune donnée n'a été modifiée.", ClinicArchiveFormat.InvalidCode);
            }
        }
    }
}
