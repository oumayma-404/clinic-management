using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetDentalRecordsQuery : IRequest<Result<IEnumerable<DentalRecordDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetDentalRecordsQueryHandler : IRequestHandler<GetDentalRecordsQuery, Result<IEnumerable<DentalRecordDto>>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetDentalRecordsQueryHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IMedicalDocumentRepository documentRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _documentRepository = documentRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<DentalRecordDto>>> Handle(GetDentalRecordsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Verify the owning patient belongs to the caller's clinic before returning any records.
            // DentalRecord is a child entity with no ClinicId of its own and is not covered by the global
            // query filter, so this explicit check is the sole tenant guard for this read (AC-1).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<DentalRecordDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<DentalRecordDto>>.Failure("Patient introuvable.");
            }

            var records = await _dentalRecordRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);

            var dtos = records.Select(dr => dr.ToDto()).ToList();

            /*
             * What each séance collected onto a treatment — ONE batched read over the page's fiches, never one
             * per row (§ 9.7).
             *
             * ⚠️ Without it the history printed « 0,000 DT » for every séance of a multi-séance act, because such
             * an act is priced 0 on its fiche: the money is real, it is on the treatment's échéancier, and the
             * one list of the visits that produced it could not see any of it. Read back rather than stored, so
             * voiding a payment corrects the history with it.
             */
            var recordIds = dtos.Select(d => d.Id).ToList();
            var collected = await _treatmentPlanRepository.GetCollectedByDentalRecordAsync(
                clinicResult.Value, recordIds, cancellationToken);
            /*
             * ⚠️ **The LINK is read separately from the MONEY, and merging them the other way round is what left a
             * whole class of séance unlabelled.** The collection read can only see a fiche that took money, so a
             * séance where the patient paid nothing that day — entirely ordinary on a six-visit implant — carried
             * no plan id and printed « 0,000 DT · 0,000 DT », indistinguishable from a free ordinary visit while
             * the treatment showed 1 500 DT outstanding. Belonging to a treatment is a clinical fact.
             */
            var links = await _treatmentPlanRepository.GetPlanLinksByDentalRecordAsync(
                clinicResult.Value, recordIds, cancellationToken);
            var byRecord = collected.ToDictionary(c => c.DentalRecordId);
            var linkByRecord = links.ToDictionary(l => l.DentalRecordId);
            foreach (var dto in dtos)
            {
                if (linkByRecord.TryGetValue(dto.Id, out var link))
                {
                    dto.TreatmentPlanId = link.TreatmentPlanId;
                    dto.TreatmentPlanNumber = link.PlanNumber;
                    // The act's own id, so a reopened fiche can re-establish « Acte planifié » — without it the
                    // séance reads as un-carried and its locked 0 is announced as a discount. See the DTO.
                    dto.TreatmentPlanItemId = link.TreatmentPlanItemId;
                    dto.TreatmentActDesignation = link.ActDesignationFr;
                    dto.TreatmentStepLabel = link.StepLabel;
                    dto.TreatmentStepNumber = link.StepNumber;
                    dto.TreatmentStepTotal = link.StepTotal > 0 ? link.StepTotal : null;
                    // 0 rather than null: « this séance belongs to a treatment and took nothing » is a figure,
                    // and null would leave the row unable to tell it from « not a treatment séance at all ».
                    dto.CollectedOnTreatment = 0m;
                }
                if (!byRecord.TryGetValue(dto.Id, out var row))
                {
                    continue;
                }
                dto.CollectedOnTreatment = row.Amount;
                // The money read wins on identity too: a payment names the plan it was posted to, which is the
                // plan whose échéancier the figure came off.
                /*
                 * ⚠️ **The act id goes with the plan it belongs to, or it goes.** This row carries no act — a
                 * payment is posted to an échéancier, not to a line — so when the money names a *different*
                 * plan from the clinical link, an act id left standing beside it would be a pair that cannot
                 * both be true: `patient-record-modal` resolves the plan from the ACT, so the fiche would
                 * quietly re-link itself to the other treatment. Rare (a fiche whose séance is on one devis and
                 * whose money went to another) and silent, which is exactly the shape this file's other two
                 * comments were written about.
                 */
                if (dto.TreatmentPlanId != row.TreatmentPlanId)
                {
                    dto.TreatmentPlanItemId = null;
                }

                dto.TreatmentPlanId = row.TreatmentPlanId;
                dto.TreatmentPlanNumber = row.PlanNumber;
            }

            /*
             * What each séance prescribed — ONE batched read for the whole history, like the two above. The
             * patient page loads every fiche in a single pass (four other things on that screen need the full
             * list), so a per-row lookup here would be an N+1 over a list that routinely runs to dozens.
             *
             * ⚠️ The appointment ids go with the record ids because ordonnances written before
             * MedicalDocument.DentalRecordId existed carry only the visit, and nothing was backfilled — see
             * the repository method for why matching on the appointment alone would be wrong.
             */
            var visitIds = dtos.Where(d => d.AppointmentId.HasValue).Select(d => d.AppointmentId!.Value).Distinct().ToList();
            var ordonnances = await _documentRepository.GetFicheOrdonnancesForDentalRecordsAsync(
                clinicResult.Value, recordIds, visitIds, cancellationToken);

            /*
             * ⚠️ Both of a séance's ordonnance types arrive from that one read and are matched INDEPENDENTLY.
             * A visit that prescribes an antibiotic and a panoramique owns two documents — a médicament and an
             * examen may not share a sheet (DocumentTypes.Examens) — so this is two assignments per fiche, not
             * a choice between them.
             */
            foreach (var documentType in new[] { DocumentTypes.Prescription, DocumentTypes.Examens })
            {
                var ofType = ordonnances
                    .Where(p => string.Equals(p.DocumentType, documentType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (ofType.Count == 0)
                {
                    continue;
                }

                var byRecordId = ofType
                    .Where(p => p.DentalRecordId.HasValue)
                    .GroupBy(p => p.DentalRecordId!.Value)
                    .ToDictionary(g => g.Key, g => g.First());
                var legacyByVisit = ofType
                    .Where(p => !p.DentalRecordId.HasValue && p.AppointmentId.HasValue)
                    .GroupBy(p => p.AppointmentId!.Value)
                    .ToDictionary(g => g.Key, g => g.First());

                // Only the médicament ordonnance can be legacy: the examens type is newer than
                // MedicalDocument.DentalRecordId, so a demande with a null fiche id does not exist, and
                // running the fallback for it would let one fiche report another's document.
                var allowLegacy = documentType == DocumentTypes.Prescription;

                foreach (var dto in dtos)
                {
                    // The fiche's own document wins over a legacy one sharing its visit: a séance that has
                    // issued its own ordonnance must not report the older document instead.
                    var document = byRecordId.GetValueOrDefault(dto.Id);
                    if (document is null && allowLegacy && dto.AppointmentId.HasValue)
                    {
                        document = legacyByVisit.GetValueOrDefault(dto.AppointmentId.Value);
                    }

                    if (document is null)
                    {
                        continue;
                    }

                    if (documentType == DocumentTypes.Examens)
                    {
                        dto.ExamensDocumentId = document.Id;
                        dto.ExamensSummary = PrescriptionLines.ExamenShortLabels(document.ContentJson).ToList();
                    }
                    else
                    {
                        dto.PrescriptionDocumentId = document.Id;
                        dto.PrescriptionSummary = PrescriptionLines.ShortLabels(document.ContentJson).ToList();
                    }
                }
            }

            return Result<IEnumerable<DentalRecordDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<DentalRecordDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

