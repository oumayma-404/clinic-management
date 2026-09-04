using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Devis → facture bridge (finding #7): create a draft invoice from an accepted treatment plan, seeding its
/// lines from the plan's acts and linking it back via <c>Invoice.TreatmentPlanId</c>. « Solde patient » then
/// counts the invoice instead of the plan (no double-count). Numbering/TVA still happen at issue.
/// </summary>
public class CreateInvoiceFromTreatmentPlanCommand : IRequest<Result<InvoiceDto>>
{
    public Guid TreatmentPlanId { get; set; }
}

public class CreateInvoiceFromTreatmentPlanCommandHandler
    : IRequestHandler<CreateInvoiceFromTreatmentPlanCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateInvoiceFromTreatmentPlanCommandHandler> _logger;

    public CreateInvoiceFromTreatmentPlanCommandHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateInvoiceFromTreatmentPlanCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(CreateInvoiceFromTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // Tenant isolation: a plan from another clinic reads as "not found".
            var plan = await _planRepository.GetByIdAsync(request.TreatmentPlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Plan de traitement introuvable.");
            }

            if (plan.Status == TreatmentPlanStatus.Draft)
            {
                return Result<InvoiceDto>.Failure("Le devis doit être accepté avant d'être facturé.");
            }
            if (plan.Status == TreatmentPlanStatus.Cancelled)
            {
                return Result<InvoiceDto>.Failure("Un devis annulé ne peut pas être facturé.");
            }
            if (!plan.ActiveItems.Any())
            {
                return Result<InvoiceDto>.Failure("Le devis ne comporte aucun acte à facturer.");
            }

            // The notes already bridging this plan, through the light projection (AC-P6.22) rather than
            // `GetFilteredAsync`, which loads every invoice of the patient with its lines and payments to test
            // one `TreatmentPlanId` — the § 9.7 over-fetch. It is also the shared authority the money reads
            // de-duplicate through, so this and those reads cannot disagree about what « déjà facturé » means.
            var planLinks = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
            var live = planLinks
                .Where(l => l.TreatmentPlanId == plan.Id && l.Status != InvoiceStatus.Cancelled)
                .ToList();

            /*
             * ⚠️ A SUPPLEMENTARY note, not a refusal, when the devis has grown since it was billed.
             *
             * Amending a billed devis is deliberately allowed — a fee typed wrong is usually noticed once the
             * work is finished — and the reasoning recorded for that was that the divergence is documentary and
             * stating it is the whole fix. True of a *changed* fee, false of an **added act**: the money reads
             * drop a plan an invoice represents, so 500 DT of delivered work added afterwards reached no
             * balance, no receivable and no caisse, the échéancier refused to collect it, and this guard
             * refused to bill it. The notice even named the wrong remedy — an avoir does not make a plan
             * billable again.
             *
             * So what is refused is billing the SAME money twice; what is allowed is billing the difference.
             * The lines below are the acts, and the supplementary note carries only the amount not yet on one.
             */
            var alreadyBilled = InvoiceCalculator.RoundMoney(live.Sum(l => l.TotalTtc));
            var toBill = InvoiceCalculator.RoundMoney(plan.TotalPlanned - alreadyBilled);
            if (live.Count > 0 && toBill <= 0m)
            {
                return Result<InvoiceDto>.Failure("Ce devis a déjà été facturé.");
            }

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Patient introuvable.");
            }

            var invoice = new Invoice(
                Guid.NewGuid(),
                clinicId,
                plan.PatientId,
                dentalRecordId: null,
                appointmentId: null,
                treatmentPlanId: plan.Id);

            // L9 — same rule as the fiche bridge: the devis already knows which practitioner quoted it, so the note
            // d'honoraires derived from it carries that attribution across instead of re-deriving it from whoever
            // happens to be issuing. This is the hop where an attribution is most easily lost, because the plan and the
            // invoice are two aggregates and nothing else copies anything between them.
            invoice.SetDoctor(plan.DoctorId);

            if (live.Count > 0)
            {
                /*
                 * A supplementary note: one line for the amount the devis has grown by since it was billed.
                 *
                 * Deliberately not « the acts that are not on the first note »: that note's lines are frozen
                 * désignation snapshots, so matching them back to today's acts would be prose-matching — the
                 * failure this repository has already deleted once. The difference between the two totals is a
                 * figure, and it is the figure that is actually owed.
                 */
                invoice.SetLines(new[]
                {
                    ($"Complément au devis {plan.Number ?? string.Empty}".TrimEnd(),
                        1, toBill, (Guid?)null, (Guid?)null, (string?)null),
                });
            }
            else
            {
                // Map each planned act to an invoice line (quantity 1, PlannedCost as unit HT).
                //
                // ⚠️ No DCH code travels from a devis any more — a devis line carries only a ProcedureType, so
                // there is nothing to carry and the invoice's own CNAM split is empty for this path.
                // Parked acts are excluded: they are not treatment any more and their fee left the total.
                invoice.SetLines(plan.ActiveItems.Select(i =>
                    (i.DesignationFr, 1, i.PlannedCost, (Guid?)null, (Guid?)null, (string?)null)));
            }

            await _invoiceRepository.AddAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created draft invoice {InvoiceId} from treatment plan {PlanId}", invoice.Id, plan.Id);

            return Result<InvoiceDto>.Success(invoice.ToDto(patient.GetFullName()));
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating invoice from treatment plan {PlanId}", request.TreatmentPlanId);
            return Result<InvoiceDto>.Failure("Erreur lors de la facturation du devis.");
        }
    }
}
