using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// « Exporter le dossier » — one patient's complete record, as one archive they can be handed.
///
/// <para><b>The right this serves.</b> Under <i>loi organique 2004-63</i> a patient may obtain a copy of the data
/// held about them, and the request arrives at a cabinet for the most ordinary reason of all: they are changing
/// dentist. Nothing in the product could produce it — every export was list-scoped and the whole-clinic archive
/// is the practice's backup — so the answer was to collect it by hand from about ten screens, or not at all.</para>
///
/// <para>⚠️ <b>A Query that writes</b>, on <c>BuildClinicArchiveQuery</c>'s precedent and for its exact reason: a
/// Command would broadcast into the clinic's group on every export because <c>RealtimeBroadcastBehavior</c>
/// derives its key from the namespace, and « something changed » is false.</para>
///
/// <para>⚠️ <b>Recorded, and not best-effort.</b> This carries one person's entire medical history out of the
/// building in a single file — the most concentrated export the product can produce — so an unrecorded one
/// succeeding is exactly the guarantee that must not be silently false. <c>PatientRecordAccessLedger</c>'s rule
/// and its reasoning about why refusing here cannot strand anybody.</para>
///
/// <para>⚠️ <b>A file whose bytes cannot be fetched is LISTED, never dropped.</b> Two cases: an original held at
/// the cabinet (the coffre), and a storage read that fails. Both leave the file named in the manifest with its
/// date, because an archive quietly missing a radiograph is worse than one that says which radiograph is
/// missing — the reader has no other way to learn it existed. A storage failure therefore does not fail the
/// export.</para>
/// </summary>
public class ExportPatientDossierQuery : IRequest<Result<PatientDossier>>
{
    public Guid PatientId { get; set; }
}

public class ExportPatientDossierQueryHandler
    : IRequestHandler<ExportPatientDossierQuery, Result<PatientDossier>>
{
    private readonly IPatientRepository _patients;
    private readonly IClinicRepository _clinics;
    private readonly IAppointmentRepository _appointments;
    private readonly IDentalRecordRepository _dentalRecords;
    private readonly IToothStateRepository _toothStates;
    private readonly IMedicalDocumentRepository _documents;
    private readonly IPatientFileRepository _files;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _auditActor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExportPatientDossierQueryHandler> _logger;

    public ExportPatientDossierQueryHandler(
        IPatientRepository patients,
        IClinicRepository clinics,
        IAppointmentRepository appointments,
        IDentalRecordRepository dentalRecords,
        IToothStateRepository toothStates,
        IMedicalDocumentRepository documents,
        IPatientFileRepository files,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider auditActor,
        IUnitOfWork unitOfWork,
        ILogger<ExportPatientDossierQueryHandler> logger)
    {
        _patients = patients;
        _clinics = clinics;
        _appointments = appointments;
        _dentalRecords = dentalRecords;
        _toothStates = toothStates;
        _documents = documents;
        _files = files;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _auditEntries = auditEntries;
        _auditActor = auditActor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PatientDossier>> Handle(
        ExportPatientDossierQuery request, CancellationToken cancellationToken)
    {
        var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicId.IsFailure)
        {
            return Result<PatientDossier>.Failure(clinicId.Error!);
        }

        var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
        if (patient == null || patient.ClinicId != clinicId.Value)
        {
            return Result<PatientDossier>.Failure("Patient introuvable.");
        }

        var clinic = await _clinics.GetByIdAsync(clinicId.Value, cancellationToken);

        // Sequential, not Task.WhenAll: these repositories share the request's DbContext — the constraint the
        // dashboard's section readers already document.
        var appointments = (await _appointments.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();
        var records = (await _dentalRecords.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();
        var teeth = (await _toothStates.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();
        var docs = (await _documents.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();
        var files = (await _files.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();

        var (contents, unreadable) = await FetchWhatCanBeFetchedAsync(files, cancellationToken);

        var dossier = PatientDossierPackager.Build(
            patient,
            clinic?.Name ?? string.Empty,
            appointments,
            records,
            teeth,
            docs,
            files,
            contents,
            unreadable,
            ClinicClock.ClinicToday());

        // Recorded BEFORE the archive is handed back, and not best-effort — see the class note.
        try
        {
            await PatientRecordAccessLedger.RecordAsync(
                _auditEntries,
                _unitOfWork,
                _auditActor.Current,
                clinicId.Value,
                PatientRecordAccessLedger.FileEntityType,
                patient.Id,
                patient.Id,
                $"Dossier complet du patient exporté ({dossier.SectionCount} section(s), "
                + $"{dossier.FilesIncluded} fichier(s) joints, {dossier.FilesListedOnly} listé(s) seulement)",
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Refused a patient dossier export for clinic {ClinicId}: the ledger row failed.", clinicId.Value);
            return Result<PatientDossier>.Failure(
                PatientRecordAccessLedger.UnrecordableMessage, PatientRecordAccessLedger.UnrecordableCode);
        }

        return Result<PatientDossier>.Success(dossier);
    }

    /// <summary>
    /// Reads the bytes of every file whose original is on the server, and skips — without failing — the ones
    /// that are not. Both skips are visible in the manifest, which is what keeps the archive honest.
    /// </summary>
    private async Task<(IReadOnlyList<(Guid FileId, string EntryName, byte[] Bytes)> Fetched,
        IReadOnlySet<Guid> Unreadable)> FetchWhatCanBeFetchedAsync(
        IReadOnlyList<PatientFile> files, CancellationToken cancellationToken)
    {
        var fetched = new List<(Guid, string, byte[])>();
        var unreadable = new HashSet<Guid>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file.Residency != FileResidency.Hosted || string.IsNullOrEmpty(file.StorageKey))
            {
                continue;
            }

            try
            {
                await using var stream = await _fileStorage.DownloadAsync(file.StorageKey, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);

                fetched.Add((file.Id, UniqueEntryName(file, used), buffer.ToArray()));
            }
            catch (Exception ex)
            {
                // ⚠️ Logged and skipped, never fatal. One unreadable object must not deny a patient the rest of
                // their dossier — and the manifest already says which files are not enclosed, so the omission is
                // visible rather than silent. LogMask because the name is composed from the patient's own.
                // ⚠️ Recorded as UNREADABLE, not merely skipped. The manifest tells these apart from a file
                // whose original is at the cabinet, because « conservé au cabinet » is an assertion about where
                // the file is — and saying it about a file that is on the server sends a patient to their
                // cabinet for something the cabinet does not have.
                unreadable.Add(file.Id);
                _logger.LogWarning(
                    ex,
                    "A patient file could not be read for a dossier export and was listed only: {FileId}",
                    file.Id);
            }
        }

        return (fetched, unreadable);
    }

    /// <summary>
    /// Two files may legitimately share a name (« radio.jpg » twice), and a ZIP with two identical entry names
    /// is a broken ZIP in some readers and a silently lost file in others.
    /// </summary>
    private static string UniqueEntryName(PatientFile file, HashSet<string> used)
    {
        var name = FileNameSanitizer.Sanitize(file.FileName);
        if (used.Add(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}{extension}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
