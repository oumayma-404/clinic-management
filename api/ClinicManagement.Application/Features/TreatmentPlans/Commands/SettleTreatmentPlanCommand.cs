using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Application.Features.Invoices;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Régler le devis » (S3) — one payment against the whole treatment, spread over the échéancier from the
/// earliest unpaid row onwards.
///
/// <para>
/// ⚠️ <b>What was missing was a route, not a rule.</b> <c>TreatmentPlan.CollectChairside</c> has spread
/// correctly since the fiche started collecting, and it was reachable from <b>one</b> place: a séance's own
/// fiche de soins. A patient settling three instalments at the desk therefore had to be taken through the
/// « Encaisser » modal once per row — three dialogs, three receipts, three dates to keep in step — because
/// <c>Installment.RecordPayment</c> refuses more than one row's remainder and the modal is addressed to a row.
/// </para>
/// <para>
/// ⚠️ <b>It is the same spreading, not a second implementation.</b> The only difference from the fiche path is
/// that there is no <c>dentalRecordId</c>: this money was not collected at a séance. That also means it is
/// deliberately <b>not</b> idempotent the way the fiche's cumulative figure is — pressing « Régler » twice takes
/// the money twice, exactly as pressing « Encaisser » twice does, and the <see cref="TreatmentPlan.Outstanding"/>
/// bound is what actually stops the second press.
/// </para>
/// <para>
/// ⚠️ Refused on a devis a note d'honoraires represents, for <c>RecordInstallmentPaymentCommand</c>'s reason
/// and through the same shared <c>PlanBridgeLookup</c>: money taken on a bridged plan reduces the patient's
/// balance and reaches no money read at all.
/// </para>
/// </summary>
public class SettleTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>What the patient is handing over now. Bounded by the plan's own outstanding, with a named refusal.</summary>
    public decimal Amount { get; set; }

    /// <summary>Cash | Cheque | Card | Transfer.</summary>
    public string Method { get; set; } = string.Empty;

    public DateTime PaidOn { get; set; }

    /// <inheritdoc cref="RecordInstallmentPaymentCommand.ChequeNumber"/>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="RecordInstallmentPaymentCommand.ChequeNumber"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="RecordInstallmentPaymentCommand.ChequeNumber"/>
    public DateTime? ChequeDueDate { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class SettleTreatmentPlanCommandHandler
    : IRequestHandler<SettleTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SettleTreatmentPlanCommandHandler> _logger;

    public SettleTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<SettleTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        SettleTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
            {
                return Result<TreatmentPlanDto>.Failure("Mode de paiement invalide.");
            }

            // J2 — the same guard the other two ledgers call. A payment dated 0001-01-01 (a client omitting the
            // key on a non-nullable DateTime) drops the patient's balance now and appears in no caisse ever.
            var dateError = PaymentDateRules.Validate(request.PaidOn, "La date du paiement");
            if (dateError != null)
            {
                return Result<TreatmentPlanDto>.Failure(dateError);
            }

            // J1 — see `RecordInstallmentPaymentCommand`; the same rule through the same shared lookup.
            var bridge = await PlanBridgeLookup.RepresentingNoteAsync(
                _invoiceRepository, clinicResult.Value, plan.Id, cancellationToken);
            if (bridge != null)
            {
                return Result<TreatmentPlanDto>.Failure(
                    $"Ce devis est facturé (note n° {bridge}). Enregistrez le paiement sur la note d'honoraires.");
            }

            if (plan.Installments.Count == 0)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Ce traitement n'a pas d'échéancier : éditez le devis avant d'encaisser.");
            }

            // Bounded here rather than left to the aggregate: `CollectChairside` throws once the schedule is
            // full, which is right as an invariant and unusable as a message.
            if (request.Amount > plan.Outstanding)
            {
                return Result<TreatmentPlanDto>.Failure(
                    $"Le montant encaissé dépasse ce qui reste dû sur ce devis ({plan.Outstanding:0.000} DT).");
            }

            // `ArgumentException` on a non-cheque method carrying cheque details — caught below.
            var cheque = ChequeDetails.For(
                method, request.ChequeNumber, request.ChequeBankName, request.ChequeDueDate);

            // ⚠️ `dentalRecordId: null` — this money was not collected at a séance, and claiming a fiche it did
            // not come from would make the échéancier's « encaissé en séance » line a false statement and would
            // make `CollectedOnRecord` count it against that fiche's cumulative figure.
            var written = plan.CollectChairside(
                request.Amount, method, request.PaidOn, cheque, dentalRecordId: null);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Settled {Amount} on treatment plan {PlanId} across {Rows} installment(s)",
                request.Amount, plan.Id, written.Count);

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
            _logger.LogError(ex, "Error settling treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'encaissement du devis.");
        }
    }
}
