using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// What deleting this fiche de soins will undo — read before the user commits, never reported afterwards.
///
/// <para>
/// ⚠️ <b>It goes through <see cref="DentalRecordDeletionReversal.InspectAsync"/>, the same call the deletion
/// itself makes</b>, and that is the whole design. A confirmation assembled from its own second reading of the
/// database is free to disagree with what the delete then does — « 80,000 DT » on screen and 160,000 DT
/// reversed, or a refusal the dialog never mentioned — and this repository's most-recorded defect is exactly
/// that: a correct rule wired to one of its call sites.
/// </para>
/// <para>
/// A refusal is surfaced here too, so the dialog can state it with a « Retour » instead of offering a
/// « Supprimer » whose only outcome is a red toast.
/// </para>
/// </summary>
public class GetDentalRecordDeletionPreviewQuery : IRequest<Result<DentalRecordDeletionPreviewDto>>
{
    public Guid PatientId { get; set; }
    public Guid Id { get; set; }
}

/// <summary>The figures the confirmation prints, and nothing the caller has to re-derive.</summary>
public class DentalRecordDeletionPreviewDto
{
    /// <summary>True when this deletion moves money — what turns the dialog into a warning.</summary>
    public bool TouchesMoney { get; set; }

    /// <summary>Everything that will leave la caisse, devis and note together.</summary>
    public decimal TotalReversed { get; set; }

    /// <summary>Per devis: its number (null for an un-numbered treatment) and what comes off it.</summary>
    public List<DentalRecordDeletionPlanDto> Plans { get; set; } = new();

    /// <summary>The note d'honoraires this fiche raised, when it has one.</summary>
    public DentalRecordDeletionNoteDto? Note { get; set; }

    /// <summary>
    /// The days whose caisse figures move, oldest first, as ISO dates. A void lands on the day the money was
    /// <b>received</b>, not today — so a deletion silently rewrites an extrait somebody may already have read.
    /// The dialog names them for that reason.
    /// </summary>
    public List<DateTime> AffectedCaisseDays { get; set; } = new();

    /// <summary>Ordonnances that will be kept and simply cut loose from the fiche.</summary>
    public int DocumentsKept { get; set; }

    /// <summary>Non-null when the deletion will be refused, already phrased for the user.</summary>
    public string? Refusal { get; set; }
}

public class DentalRecordDeletionPlanDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class DentalRecordDeletionNoteDto
{
    public Guid Id { get; set; }

    /// <summary>Null on a draft — which is why the dialog must not interpolate it blind.</summary>
    public string? Number { get; set; }

    public decimal Amount { get; set; }

    /// <summary>A draft is deleted outright; a numbered note is annulée and keeps its number.</summary>
    public bool IsDraft { get; set; }
}

public class GetDentalRecordDeletionPreviewQueryHandler
    : IRequestHandler<GetDentalRecordDeletionPreviewQuery, Result<DentalRecordDeletionPreviewDto>>
{
    private readonly IDentalRecordRepository _recordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IMedicalDocumentRepository _medicalDocumentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetDentalRecordDeletionPreviewQueryHandler> _logger;

    public GetDentalRecordDeletionPreviewQueryHandler(
        IDentalRecordRepository recordRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        IMedicalDocumentRepository medicalDocumentRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetDentalRecordDeletionPreviewQueryHandler> logger)
    {
        _recordRepository = recordRepository;
        _patientRepository = patientRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _medicalDocumentRepository = medicalDocumentRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<DentalRecordDeletionPreviewDto>> Handle(
        GetDentalRecordDeletionPreviewQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalRecordDeletionPreviewDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var record = await _recordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (record == null || record.PatientId != request.PatientId)
            {
                return Result<DentalRecordDeletionPreviewDto>.Failure("Fiche de soins introuvable.");
            }

            // A fiche carries no ClinicId of its own on the read path — it is a child of Patient — so the tenant
            // check goes through the patient, and a cross-clinic fiche reads as "not found" rather than 403.
            var patient = await _patientRepository.GetByIdAsync(record.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDeletionPreviewDto>.Failure("Fiche de soins introuvable.");
            }

            var reversal = await DentalRecordDeletionReversal.InspectAsync(
                _planRepository, _invoiceRepository, _creditNoteRepository,
                clinicResult.Value, record, cancellationToken);

            if (reversal.IsRefused)
            {
                return Result<DentalRecordDeletionPreviewDto>.Success(new DentalRecordDeletionPreviewDto
                {
                    Refusal = reversal.Refusal,
                });
            }

            var documents = await _medicalDocumentRepository.GetFicheOrdonnancesForDentalRecordsAsync(
                clinicResult.Value,
                new[] { record.Id },
                record.AppointmentId is { } appointmentId ? new[] { appointmentId } : Array.Empty<Guid>(),
                cancellationToken);

            var note = reversal.Notes.FirstOrDefault();

            return Result<DentalRecordDeletionPreviewDto>.Success(new DentalRecordDeletionPreviewDto
            {
                TouchesMoney = reversal.TouchesMoney,
                TotalReversed = reversal.TotalReversed,
                AffectedCaisseDays = reversal.AffectedCaisseDays.ToList(),
                DocumentsKept = documents.Count(d => d.DentalRecordId is null || d.DentalRecordId == record.Id),
                Plans = reversal.PlanCollections.Select(c => new DentalRecordDeletionPlanDto
                {
                    Id = c.Plan.Id,
                    Number = c.Plan.Number,
                    Title = c.Plan.Title,
                    Amount = c.Payments.Sum(p => p.Amount),
                }).ToList(),
                Note = note is null ? null : new DentalRecordDeletionNoteDto
                {
                    Id = note.Invoice.Id,
                    Number = note.Invoice.Number,
                    Amount = note.Payments.Sum(p => p.Amount),
                    IsDraft = note.Invoice.Number is null,
                },
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error previewing deletion of dental record {RecordId}", request.Id);
            return Result<DentalRecordDeletionPreviewDto>.Failure(
                "Impossible de vérifier ce que la suppression annulerait.");
        }
    }
}
