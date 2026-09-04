using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Issue a draft invoice: assign the per-clinic sequential number (<c>AAAA-NNNN</c>) and freeze the
/// clinic's VAT/stamp settings + totals. Numbering is gapless and concurrency-safe (unique index +
/// recompute-and-retry on collision).
/// </summary>
public class IssueInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid Id { get; set; }
}

public class IssueInvoiceCommandHandler : IRequestHandler<IssueInvoiceCommand, Result<InvoiceDto>>
{
    // Bounds the recompute-and-retry loop when concurrent issuances collide on the unique number index.
    private const int MaxNumberingAttempts = 5;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IssueInvoiceCommandHandler> _logger;

    public IssueInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository planRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<IssueInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _planRepository = planRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(IssueInvoiceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<InvoiceDto>.Failure("Cabinet introuvable.");
            }

            // The clinic's fiscal year, not the UTC one (AC-P6.7). A note d'honoraires issued at 00:30 on
            // 1 January Tunis is still 31 December in UTC, so `DateTime.UtcNow.Year` numbered it into the year
            // that had just closed — and the number is the invoice's legal identity, gapless per year and
            // unique per clinic, so there is no correcting it afterwards.
            var year = ClinicClock.ClinicYear();

            for (var attempt = 1; attempt <= MaxNumberingAttempts; attempt++)
            {
                var nextSequence = await _invoiceRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
                var number = $"{year}-{nextSequence:D4}";

                if (attempt == 1)
                {
                    invoice.Issue(number);

                    // The totals are frozen now, so this is the first moment the carry-over can be bounded.
                    var carryOver = await CarryOverPlanPaymentsAsync(invoice, cancellationToken);
                    if (carryOver.IsFailure)
                    {
                        return Result<InvoiceDto>.Failure(carryOver.Error!);
                    }

                    var supersede = await SupersedePredecessorAsync(invoice, cancellationToken);
                    if (supersede.IsFailure)
                    {
                        return Result<InvoiceDto>.Failure(supersede.Error!);
                    }
                }
                else
                {
                    // A concurrent issuance took our number; keep the frozen totals, reassign the number only.
                    invoice.SetIssuedNumber(number);
                }

                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Issued invoice {InvoiceId} as {Number}", invoice.Id, invoice.Number);
                    return Result<InvoiceDto>.Success(await MapAsync(invoice, cancellationToken));
                }
                catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
                {
                    _logger.LogWarning(
                        "Invoice number {Number} collided on issue attempt {Attempt}; recomputing", number, attempt);
                }
            }

            return Result<InvoiceDto>.Failure("Impossible d'attribuer un numéro de facture unique. Veuillez réessayer.");
        }
        catch (InvalidOperationException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error issuing invoice {InvoiceId}", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de l'émission de la facture.");
        }
    }

    private async Task<InvoiceDto> MapAsync(Domain.Entities.Invoice invoice, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);
        return invoice.ToDto(patient?.GetFullName());
    }

    /// <summary>
    /// Retire the note this one corrects, and bring its money across — the second half of
    /// <c>CorrectInvoiceCommand</c>, deliberately deferred to this moment.
    ///
    /// <para><b>Why here and not when the correction was opened.</b> Voiding the predecessor's payments takes real
    /// money out of la caisse. Doing it while the dentist edits would empty the till for the duration, and
    /// permanently if they walked away. So the original stays live and paid until its replacement actually exists,
    /// and the swap happens inside this one transaction: void, cancel, re-record. Either the whole correction
    /// lands or none of it does.</para>
    ///
    /// <para><b>Each payment keeps its original <c>PaidOn</c></b>, exactly as the devis carry-over does. Correcting
    /// a mistake today must not move yesterday's takings — the money changed hands when it changed hands, and
    /// every money read in the product attributes it by that date.</para>
    /// </summary>
    private async Task<Result> SupersedePredecessorAsync(
        Domain.Entities.Invoice replacement,
        CancellationToken cancellationToken)
    {
        if (replacement.SupersedesInvoiceId is not { } originalId)
        {
            return Result.Success();
        }

        var reason = replacement.SupersedesReason ?? "Correction de la note d'honoraires.";

        var original = await _invoiceRepository.GetByIdAsync(originalId, cancellationToken);
        if (original == null || original.ClinicId != replacement.ClinicId)
        {
            // Refused, not skipped. Issuing the replacement while the note it replaces stays live would leave the
            // patient holding two numbered documents for one séance — the exact duplicate this whole area guards
            // against — and nothing on screen would say so.
            return Result.Failure(
                "La note que cette correction remplace est introuvable. La correction ne peut pas être émise.");
        }

        if (original.Status == Domain.Enums.InvoiceStatus.Cancelled)
        {
            return Result.Failure(
                $"La note {original.Number} a déjà été annulée entre-temps. Supprimez ce brouillon et repartez "
                + "de la note en vigueur.");
        }

        var live = original.Payments.Where(p => !p.IsVoided).OrderBy(p => p.PaidOn).ThenBy(p => p.CreatedAt).ToList();
        var collected = InvoiceCalculator.RoundMoney(live.Sum(p => p.Amount));

        foreach (var payment in live)
        {
            original.VoidPayment(payment.Id, reason, creditedTotal: 0m);
        }

        original.Cancel(reason);
        original.MarkSupersededBy(replacement.Id);

        // Correcting says the note was WRONG, so a figure above the corrected total was never received: the carry
        // stops at what the séance is now worth. Refusing here instead sent the dentist to an avoir, which states
        // a refund that did not happen — and the fiche path (`UpdateDentalRecordCommand`) has never refused it.
        var remaining = replacement.TotalTtc;
        foreach (var payment in live)
        {
            // Cheque identity and the banked stamp travel with the money, for the reason the devis bridge spells
            // out one method down: a cheque left behind would vanish from « chèques à encaisser » entirely.
            var carried = InvoiceCalculator.RoundMoney(Math.Min(payment.Amount, remaining));
            if (carried <= 0m)
            {
                break;
            }

            replacement.RecordPayment(
                carried, payment.Method, payment.PaidOn, payment.SourceInstallmentPaymentId,
                payment.ToChequeDetails(), payment.ToBankedStamp());
            remaining = InvoiceCalculator.RoundMoney(remaining - carried);
        }

        await _invoiceRepository.UpdateAsync(original, cancellationToken);

        var dropped = InvoiceCalculator.RoundMoney(collected - replacement.AmountCollected);
        _logger.LogInformation(
            "Invoice {Number} cancelled and superseded by {ReplacementId}; carried {Amount} DT across {Count} "
            + "payments, {Dropped} DT written off as never received",
            original.Number, replacement.Id, replacement.AmountCollected, live.Count, dropped);

        return Result.Success();
    }

    /// <summary>
    /// Carry money already collected on the devis's échéancier onto the invoice that now bills it.
    ///
    /// <para>
    /// Without this, bridging a plan that had taken a deposit re-billed the patient for money they had already
    /// paid: the invoice was created with <c>AmountCollected = 0</c>, and the moment it left Draft the plan's
    /// outstanding was suppressed everywhere — so the deposit simply vanished from the balance and reappeared
    /// as invoice debt.
    /// </para>
    /// <para>
    /// Each carried payment keeps its <b>original</b> date, so no month's takings move, and records
    /// <c>SourceInstallmentPaymentId</c> as provenance. Nothing is written to the plan: de-duplication is
    /// read-side (<c>GetInstallmentCollectedBetweenAsync</c> excludes bridged plans), which keeps this a
    /// single-aggregate write and makes it self-correcting — cancelling the bridge hands the money straight
    /// back to the plan track.
    /// </para>
    /// </summary>
    private async Task<Result> CarryOverPlanPaymentsAsync(
        Domain.Entities.Invoice invoice,
        CancellationToken cancellationToken)
    {
        if (invoice.TreatmentPlanId is not { } planId)
        {
            return Result.Success();
        }

        var plan = await _planRepository.GetByIdAsync(planId, cancellationToken);
        if (plan == null || plan.ClinicId != invoice.ClinicId)
        {
            // The bridge link is a soft reference with no FK. A missing plan must not block issuing a numbered
            // fiscal document — there is simply nothing to carry.
            _logger.LogWarning(
                "Invoice {InvoiceId} references treatment plan {PlanId}, which was not found; nothing carried over",
                invoice.Id, planId);
            return Result.Success();
        }

        var collected = plan.Installments
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsVoided)
            .OrderBy(p => p.PaidOn)
            .ThenBy(p => p.CreatedAt)
            .ToList();

        if (collected.Count == 0)
        {
            return Result.Success();
        }

        var total = InvoiceCalculator.RoundMoney(collected.Sum(p => p.Amount));
        if (total > invoice.TotalTtc)
        {
            // Refused rather than clamped, and refused BEFORE any payment is recorded. Letting
            // Invoice.RecordPayment throw its over-payment guard mid-loop would strand a numbered invoice that
            // can then be neither issued nor rebuilt. Reachable when acts were removed from the plan after
            // money was taken, or when the clinic's VAT settings changed.
            //
            // ⚠️ Refusing here is the LAST cheap moment. Once this carry succeeds it is one-way and one-time: the
            // plan stops being counted by every money read, and the invoice now holds non-voided payments, which
            // makes it uncancellable — so the only correction left is an avoir. Nothing hands the receipts back to
            // the devis.
            return Result.Failure(
                $"Ce devis a déjà encaissé {total:0.000} DT, soit plus que le total de la facture "
                + $"({invoice.TotalTtc:0.000} DT). Corrigez le devis ou les actes facturés avant d'émettre : "
                + "une fois la facture émise, les encaissements y sont reportés et ne se corrigent plus que par "
                + "un avoir.");
        }

        foreach (var payment in collected)
        {
            // ⚠️ The cheque's identity travels with the money (L8). The plan side stops being counted the moment
            // this bridge invoice is issued, so a cheque left behind here would vanish from « chèques à
            // encaisser » entirely — the row that still has to be banked becoming the one row nothing lists.
            // `ToChequeDetails()` rebuilds it through `ChequeDetails.For`, so the method/details invariant is
            // re-checked on the way across rather than trusted.
            //
            // ⚠️ The banked mark travels with it, for the same reason and with the same consequence if it does
            // not: a cheque banked in September and billed in October would reappear under « à encaisser » the
            // moment the plan side stopped being counted, and re-marking it would record today instead of the day
            // it was actually deposited.
            invoice.RecordPayment(
                payment.Amount, payment.Method, payment.PaidOn, payment.Id,
                payment.ToChequeDetails(), payment.ToBankedStamp());
        }

        _logger.LogInformation(
            "Carried {Amount} DT of installment payments from plan {PlanId} onto invoice {InvoiceId}",
            total, plan.Id, invoice.Id);

        return Result.Success();
    }
}
