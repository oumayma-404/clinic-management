using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Take money at the chair for a multi-séance treatment: the amount goes onto the <b>treatment's</b> échéancier,
/// and a treatment that is still un-numbered is issued its devis in the same breath.
///
/// <para>
/// <b>The gap this closes, and it was a hole rather than a rough edge.</b> The fiche de soins labels its payment
/// field « Encaissé aujourd'hui » on a séance a devis carries, states « 600 encaissés, 1 400 restants » beneath
/// it, and <c>appointment-acts-picker</c> explains that on an un-numbered treatment « the money is collected
/// séance by séance on the fiche ». <b>None of that existed.</b> A devis act is priced 0 on the fiche by rule
/// (<c>PriceForPlanLinkedAct</c>), so the séance's total is 0, so:
/// </para>
/// <list type="bullet">
/// <item>the browser disabled « Confirmer » for any amount typed, since « payé » may not exceed the séance total;</item>
/// <item>and had it got through, <c>BillDentalRecordCommand</c> would have refused it — « Cette fiche de soins ne
/// comporte aucun acte facturable » — as a warning inside an HTTP 200, leaving the amount on
/// <c>DentalRecord.AmountPaid</c>, which no money read has ever touched.</item>
/// </list>
/// <para>
/// The only way through was to overtype the rule's 0 with the act's real fee, which raises a note d'honoraires
/// for work the treatment already prices — and that note carries no <c>TreatmentPlanId</c>, so
/// <c>PlanBillingRules.BilledPlanIds</c> cannot de-duplicate it. Measured: a 250 DT act with 150 collected left
/// the patient owing 250 on the devis and 100 on the note.
/// </para>
///
/// <para>
/// ⚠️ <b>Why collecting issues the devis.</b> A <c>Draft</c> has no échéancier at all (<c>Accept</c> is what
/// raises the lump-sum échéance) and <c>EnsurePayable</c> refuses one outright — so « let a Draft hold the
/// money » is not a small change but a new money state, and la caisse would not see it: both installment reads
/// filter on <c>PlanBillingRules.DebtBearingPlanStatuses</c>, which excludes <c>Draft</c>. Cash landing where the
/// till cannot see it is the defect this command exists to remove, not one to introduce. Issuing instead puts
/// the money on the one track every reader already understands — la caisse, « Solde patient », « Créances », the
/// dashboard, <c>reconcile-money</c> and the échéance's own « Reçu » — and it is the moment
/// <see cref="IssueDevisCommand"/> already names: « the moment the money becomes a claim the patient has seen ».
/// </para>
///
/// <para>
/// ⚠️ <b><see cref="Amount"/> is the séance's cumulative figure, not an increment.</b> It has the same meaning
/// « Montant payé » has on the fiche, and for the same reason: the fiche is what the user edits, so re-saving one
/// must not take the money twice. The difference is derived from
/// <see cref="TreatmentPlan.CollectedOnRecord"/> — the shape <c>BillDentalRecordCommand.TopUpAsync</c> uses.
/// </para>
/// </summary>
public class CollectOnTreatmentCommand : IRequest<Result<TreatmentCollectionResult>>
{
    /// <summary>The treatment being paid towards.</summary>
    public Guid TreatmentPlanId { get; set; }

    /// <summary>The fiche the money was handed over at. Tags the payment and derives the increment.</summary>
    public Guid DentalRecordId { get; set; }

    /// <summary>The séance's cumulative collected total — see the type remarks. 0 collects nothing.</summary>
    public decimal Amount { get; set; }

    /// <summary>Cash | Cheque | Card | Transfer. The fiche's own method, never a hard-coded default.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Defaults to the fiche's intervention date rather than "now", for <c>DentalRecordPaymentRequest</c>'s
    /// reason: a séance recorded two days late was paid on the day it happened.
    /// </summary>
    public DateTime? PaidOn { get; set; }

    /// <inheritdoc cref="DentalRecordPaymentRequest.ChequeNumber"/>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="DentalRecordPaymentRequest.ChequeNumber"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="DentalRecordPaymentRequest.ChequeNumber"/>
    public DateTime? ChequeDueDate { get; set; }
}

/// <summary>What collecting on a treatment did — the outcome is typed, because « rien à encaisser » is one.</summary>
public class TreatmentCollectionResult
{
    public TreatmentCollectionOutcome Outcome { get; set; }

    /// <summary>The devis number, minted by this call or already held. Null only when nothing was collected.</summary>
    public string? PlanNumber { get; set; }

    /// <summary>What this call actually put on the échéancier — the increment, never the cumulative figure.</summary>
    public decimal AmountCollected { get; set; }

    /// <summary>What the patient still owes on the treatment afterwards.</summary>
    public decimal Outstanding { get; set; }

    /// <summary>True when this call is what gave the treatment its number.</summary>
    public bool DevisIssued { get; set; }

    public string? Message { get; set; }
}

public enum TreatmentCollectionOutcome
{
    /// <summary>Nothing was offered — the ordinary séance where the patient pays nothing today.</summary>
    NotCollected = 0,

    /// <summary>Money went onto the treatment's échéancier.</summary>
    Collected = 1,

    /// <summary>The séance's figure was already on the échéancier — an ordinary re-save.</summary>
    AlreadyCollected = 2,

    /// <summary>A rule said no, and <see cref="TreatmentCollectionResult.Message"/> names it.</summary>
    Refused = 3,
}

public class CollectOnTreatmentCommandHandler
    : IRequestHandler<CollectOnTreatmentCommand, Result<TreatmentCollectionResult>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IDentalRecordRepository _recordRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CollectOnTreatmentCommandHandler> _logger;

    public CollectOnTreatmentCommandHandler(
        ITreatmentPlanRepository planRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IDentalRecordRepository recordRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CollectOnTreatmentCommandHandler> logger)
    {
        _planRepository = planRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _recordRepository = recordRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentCollectionResult>> Handle(
        CollectOnTreatmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentCollectionResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.TreatmentPlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentCollectionResult>.Failure("Plan de traitement introuvable.");
            }

            var record = await _recordRepository.GetByIdAsync(request.DentalRecordId, cancellationToken);
            if (record == null || record.PatientId != plan.PatientId)
            {
                return Result<TreatmentCollectionResult>.Failure("Fiche de soins introuvable.");
            }

            // The increment, from what THIS fiche has already put on the treatment. See the type remarks: the
            // request carries the séance's cumulative figure, so a re-save must add the difference or nothing.
            var requested = InvoiceCalculator.RoundMoney(request.Amount);
            var already = plan.CollectedOnRecord(record.Id);
            var delta = InvoiceCalculator.RoundMoney(requested - already);

            if (requested < already)
            {
                // Money on a numbered devis is un-received by voiding the payment, never by retyping a field —
                // the same rule the note d'honoraires enforces, stated here before the round trip rather than
                // after it. Coded, so the client branches on the code and never on this sentence.
                return Result<TreatmentCollectionResult>.Failure(
                    $"{already:0.000} DT ont déjà été encaissés sur ce traitement pour cette séance. "
                    + "Un encaissement ne se diminue pas ici : annulez le paiement sur l'échéancier du devis.",
                    TreatmentCollectionRefusals.CollectionLoweredCode);
            }

            if (delta <= 0m)
            {
                return Result<TreatmentCollectionResult>.Success(new TreatmentCollectionResult
                {
                    Outcome = requested <= 0m
                        ? TreatmentCollectionOutcome.NotCollected
                        : TreatmentCollectionOutcome.AlreadyCollected,
                    PlanNumber = plan.Number,
                    AmountCollected = 0m,
                    Outstanding = plan.Outstanding,
                });
            }

            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
            {
                return Result<TreatmentCollectionResult>.Failure("Mode de paiement invalide.");
            }

            ChequeDetails? cheque;
            try
            {
                cheque = ChequeDetails.For(
                    method, request.ChequeNumber, request.ChequeBankName, request.ChequeDueDate);
            }
            catch (ArgumentException ex)
            {
                return Result<TreatmentCollectionResult>.Failure(ex.Message);
            }

            var paidOn = request.PaidOn ?? record.InterventionDate;
            // J2 — the same guard the other two ledgers call. A payment dated 0001-01-01 (a client omitting the
            // key on a non-nullable DateTime) drops the patient's balance now and appears in no caisse ever.
            var dateError = PaymentDateRules.Validate(paidOn, "La date du paiement");
            if (dateError != null)
            {
                return Result<TreatmentCollectionResult>.Failure(dateError);
            }

            /*
             * ⚠️ Every refusal above this line, and none below it: the next step spends a gapless devis number,
             * and a number consumed by a save that then fails can only be released by a cancellation carrying a
             * motif. The same ordering `BillDentalRecordCommand` keeps around `Issue()`, for the same reason.
             */
            var issued = false;
            if (plan.Number is null)
            {
                var issuance = await IssueForCollectionAsync(plan, clinicId, cancellationToken);
                if (issuance.IsFailure)
                {
                    return Result<TreatmentCollectionResult>.FailureFrom(issuance);
                }
                issued = true;
            }

            // Bounded here rather than left to the aggregate: `CollectChairside` throws once the schedule is
            // full, which is right as an invariant and unusable as a message. Checked AFTER issuance because a
            // Draft has no échéancier, so `Outstanding` only means anything once the devis exists.
            if (delta > plan.Outstanding)
            {
                return Result<TreatmentCollectionResult>.Failure(
                    $"Le montant encaissé dépasse ce qui reste dû sur ce traitement "
                    + $"({plan.Outstanding:0.000} DT).",
                    TreatmentCollectionRefusals.ExceedsOutstandingCode);
            }

            plan.CollectChairside(delta, method, paidOn, cheque, record.Id);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Collected {Amount} on treatment {PlanId} ({Number}) from fiche {RecordId}; devis issued: {Issued}",
                delta, plan.Id, plan.Number, record.Id, issued);

            return Result<TreatmentCollectionResult>.Success(new TreatmentCollectionResult
            {
                Outcome = TreatmentCollectionOutcome.Collected,
                PlanNumber = plan.Number,
                AmountCollected = delta,
                Outstanding = plan.Outstanding,
                DevisIssued = issued,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentCollectionResult>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentCollectionResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(
                ex, "Error collecting on treatment {PlanId} from fiche {RecordId}",
                request.TreatmentPlanId, request.DentalRecordId);
            return Result<TreatmentCollectionResult>.Failure("Erreur lors de l'encaissement sur le traitement.");
        }
    }

    /// <summary>
    /// Number the treatment so it can hold the money — <see cref="IssueDevisCommand"/>'s body, called directly
    /// rather than through the mediator because both halves must land in one save.
    ///
    /// <para>
    /// ⚠️ The Draft's existing steps are echoed back <b>by position and with their own ids</b>, exactly as
    /// <see cref="IssueDevisCommand"/> does: they are the confirmed sequence, so the catalogue protocol must not
    /// be laid over them a second time, and echoing each step's id is what preserves its réalisé date and its
    /// fiche link through the promotion. Getting this wrong would detach the séance that is being paid for from
    /// the step it carried out.
    /// </para>
    /// </summary>
    private Task<Result> IssueForCollectionAsync(
        TreatmentPlan plan, Guid clinicId, CancellationToken cancellationToken) =>
        DevisNumbering.AcceptAndSaveAsync(
            plan, clinicId, _planRepository, _procedureTypeRepository, _unitOfWork,
            ct => _planRepository.UpdateAsync(plan, ct),
            plan.Items
                .OrderBy(i => i.SequenceNumber)
                .Select(i => (IReadOnlyList<TreatmentPlanItemStepInput>?)i.Steps
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => new TreatmentPlanItemStepInput(
                        s.Id, s.Label, s.EstimatedDurationMinutes, s.MinDaysAfterPrevious))
                    .ToList())
                .ToList(),
            _logger, cancellationToken);
}

/// <summary>
/// The coded refusals of chairside collection. Codes, never sentences — « never recover an outcome by matching
/// French prose » (a <c>Contains("déjà facturée")</c> once made rewording a message change behaviour).
/// </summary>
public static class TreatmentCollectionRefusals
{
    public const string CollectionLoweredCode = "treatment_collection_lowered";
    public const string ExceedsOutstandingCode = "treatment_collection_exceeds_outstanding";
}
