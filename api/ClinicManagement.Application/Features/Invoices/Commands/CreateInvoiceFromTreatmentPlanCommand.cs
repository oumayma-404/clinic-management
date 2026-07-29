using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Devis → facture bridge (finding #7): create a draft invoice from an accepted treatment plan, seeding its
/// lines from the plan's acts and linking it back via <c>Invoice.TreatmentPlanId</c>. « Solde patient » then
/// counts the invoice instead of the plan (no double-count). Numbering/TVA/El Fatoora still happen at issue.
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
            if (plan.Items.Count == 0)
            {
                return Result<InvoiceDto>.Failure("Le devis ne comporte aucun acte à facturer.");
            }

            // Refuse a second bridge invoice for the same plan (edge case) — the existing non-cancelled one
            // already represents this work.
            //
            // Read through the light bridge projection (AC-P6.22). This used to be `GetFilteredAsync`, which
            // loads every invoice of the patient **with its lines and payments** in order to test one
            // `TreatmentPlanId` — the § 9.7 over-fetch. `GetTreatmentPlanLinksAsync` exists for exactly this
            // question and is already the shared authority the money reads de-duplicate through, so the guard
            // and those reads can no longer disagree about what « déjà facturé » means.
            var planLinks = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
            if (planLinks.Any(l => l.TreatmentPlanId == plan.Id && l.Status != InvoiceStatus.Cancelled))
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

            // Map each planned act to an invoice line (quantity 1, PlannedCost as unit HT, carry the CNAM/DCH link).
            invoice.SetLines(plan.Items.Select(i =>
                (i.DesignationFr, 1, i.PlannedCost, (Guid?)null, i.DentalActCodeId, i.CodeActe)));

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
