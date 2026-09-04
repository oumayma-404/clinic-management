using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>
/// The acts of this patient's recent fiches de soins that could be turned into a multi-séance treatment —
/// « c'est la suite d'une séance précédente ».
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Candidates, never a diagnosis.</b> A fiche records what was carried out and says nothing about what
/// remains, so no read can tell an unfinished bridge from a finished obturation. Every recent act is offered and
/// the dentist chooses. The alternative — guessing — is wrong on ordinary completed work, which is most of it.
/// </para>
/// <para>
/// ⚠️ <b>An act already carried by a devis is excluded</b>, matched on the plan item's own
/// <c>LinkedDentalRecordId</c>. Offering it would invite a second devis over the same work, and the two would
/// then disagree about how far along it is — with the money on whichever one was billed.
/// </para>
/// <para>
/// Bounded to <see cref="LookbackDays"/> because the question is « la séance de la semaine dernière », not the
/// patient's whole history: a list of every act a long-standing patient ever had is one nobody reads.
/// </para>
/// </remarks>
public class GetContinuableActsQuery : IRequest<Result<List<ContinuableActDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetContinuableActsQueryHandler
    : IRequestHandler<GetContinuableActsQuery, Result<List<ContinuableActDto>>>
{
    /// <summary>How far back a séance can be and still be offered as continuable. Roughly a clinical quarter.</summary>
    private const int LookbackDays = 120;

    private readonly IDentalRecordRepository _recordRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetContinuableActsQueryHandler> _logger;

    public GetContinuableActsQueryHandler(
        IDentalRecordRepository recordRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetContinuableActsQueryHandler> logger)
    {
        _recordRepository = recordRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<ContinuableActDto>>> Handle(
        GetContinuableActsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<ContinuableActDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<List<ContinuableActDto>>.Failure("Patient introuvable.");
            }

            // Tunisian midnight, not UTC's — a séance recorded at 00:30 local belongs to the day that just began.
            var since = ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday().AddDays(-LookbackDays));

            var records = (await _recordRepository.GetByPatientIdAsync(request.PatientId, cancellationToken))
                .Where(r => r.ClinicId == clinicId && r.InterventionDate >= since)
                .ToList();
            if (records.Count == 0)
            {
                return Result<List<ContinuableActDto>>.Success(new List<ContinuableActDto>());
            }

            // Fiches already evidencing a devis act — excluded, see the class note.
            var plans = await _planRepository.GetFilteredAsync(
                clinicId, patientId: request.PatientId, cancellationToken: cancellationToken);
            // ⚠️ Cancelled plans excluded, or a wrong continuation is permanent. Picking the wrong séance creates
            // an accepted devis that can only be cancelled, and with no status filter here its acts still
            // matched — so the fiche vanished from « Suite d'une séance précédente » for ever, the continuation
            // could never be re-run, and the fiche itself became undeletable (deleting it un-marks a step on a
            // cancelled plan, which the aggregate refuses).
            var recordsOnAPlan = plans.Items
                .Where(p => p.Status != TreatmentPlanStatus.Cancelled)
                .SelectMany(p => p.Items)
                .SelectMany(i => i.Steps
                    .Select(s => s.LinkedDentalRecordId)
                    .Append(i.LinkedDentalRecordId))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            // Which fiches are already on a note, and what is still owed on it. The light projection rather than
            // GetFilteredAsync, for the reason the repository's own docstring gives.
            var invoiceLinks = InvoiceLinkChoice.ByKey(
                (await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken))
                    .Select(l => (l.DentalRecordId, l.InvoiceId, l.Number, l.Status)));

            var outstandingByInvoice = new Dictionary<Guid, decimal>();
            foreach (var invoiceId in invoiceLinks.Values.Select(v => v.InvoiceId).Distinct())
            {
                var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);
                if (invoice != null && invoice.ClinicId == clinicId)
                {
                    outstandingByInvoice[invoiceId] = invoice.Outstanding;
                }
            }

            var acts = new List<ContinuableActDto>();
            foreach (var record in records.Where(r => !recordsOnAPlan.Contains(r.Id)))
            {
                var billed = invoiceLinks.TryGetValue(record.Id, out var link);
                foreach (var act in record.Acts)
                {
                    acts.Add(new ContinuableActDto
                    {
                        DentalRecordId = record.Id,
                        ActId = act.Id,
                        InterventionDate = record.InterventionDate,
                        ProcedureName = act.ProcedureName,
                        ProcedureTypeId = act.ProcedureTypeId,
                        ToothNumbers = act.ToothNumbers.ToList(),
                        Cost = act.Cost,
                        InvoiceId = billed ? link.InvoiceId : null,
                        InvoiceNumber = billed ? link.Number : null,
                        InvoiceOutstanding = billed
                            ? outstandingByInvoice.GetValueOrDefault(link.InvoiceId)
                            : 0m,
                    });
                }
            }

            // Most recent first: the séance somebody is continuing is almost always the last one.
            return Result<List<ContinuableActDto>>.Success(
                acts.OrderByDescending(a => a.InterventionDate).ThenBy(a => a.ProcedureName).ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading continuable acts for patient {PatientId}", request.PatientId);
            return Result<List<ContinuableActDto>>.Failure("Erreur lors de la lecture des séances précédentes.");
        }
    }
}
