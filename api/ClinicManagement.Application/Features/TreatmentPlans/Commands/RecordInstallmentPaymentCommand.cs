using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>Record a payment against one installment of an accepted plan's échéancier. Over-payment refused.</summary>
public class RecordInstallmentPaymentCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid InstallmentId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }

    /// <summary>
    /// The cheque's number, bank and due date (L8) — all optional, all refused for any method but <c>Cheque</c>.
    /// An échéancier is very often settled by a series of post-dated cheques handed over at acceptance, which is
    /// the case this whole item exists for.
    /// </summary>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public DateTime? ChequeDueDate { get; set; }
}

public class RecordInstallmentPaymentCommandHandler : IRequestHandler<RecordInstallmentPaymentCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RecordInstallmentPaymentCommandHandler> _logger;

    public RecordInstallmentPaymentCommandHandler(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<RecordInstallmentPaymentCommandHandler> logger)
    {
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(RecordInstallmentPaymentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
            {
                return Result<TreatmentPlanDto>.Failure("Mode de paiement invalide.");
            }

            // J2 — the third money ledger finally calls the guard whose own docstring names it as a caller.
            // Without it an échéance could be dated next month (drops the patient's balance now, appears in no
            // caisse until then) or `0001-01-01` (drops the balance forever, appears in no caisse ever).
            var paymentDateError = PaymentDateRules.Validate(request.PaidOn, "La date du paiement");
            if (paymentDateError != null)
            {
                return Result<TreatmentPlanDto>.Failure(paymentDateError);
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            /*
             * J1 — an échéance on a plan already represented by a note d'honoraires is refused.
             *
             * `CarryOverPlanPaymentsAsync` runs **once**, when the bridge invoice is issued, while both
             * installment money reads carry `&& !excluded.Contains(p.Id)` unconditionally. So cash collected on
             * the plan *after* the bridge reduced the patient's balance and reached **no** money read: not la
             * caisse, not the dashboard, not « Encaissé » on /factures. The money was entered, receipted, and
             * invisible.
             *
             * Refusing at the write is the only correct side. Teaching the reads to include a billed plan would
             * double-count the payments the bridge already carried across — the plan's money is excluded
             * *because* the invoice now represents it.
             *
             * The authority is the same `PlanBillingRules` the reads use, read through the same light bridge-link
             * projection, so the guard and the exclusion cannot disagree about which invoices count. A `Draft`
             * bridge does not represent the plan and a `Cancelled` one is void, so collecting on a plan whose
             * bridge was later cancelled still works.
             */
            var bridge = (await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicResult.Value, cancellationToken))
                .Where(l => l.TreatmentPlanId == plan.Id && PlanBillingRules.RepresentsItsPlan(l.Status))
                .Select(l => l.Number)
                .FirstOrDefault();
            if (bridge != null)
            {
                return Result<TreatmentPlanDto>.Failure(
                    $"Ce devis est facturé (note n° {bridge}). Enregistrez le paiement sur la note d'honoraires.");
            }

            // `ArgumentException` on a non-cheque method carrying cheque details — caught below like every other
            // domain refusal on this handler.
            var cheque = ChequeDetails.For(
                method, request.ChequeNumber, request.ChequeBankName, request.ChequeDueDate);

            plan.RecordInstallmentPayment(request.InstallmentId, request.Amount, method, request.PaidOn, cheque);

            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error recording installment payment for plan {PlanId}", request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'enregistrement du paiement.");
        }
    }
}
