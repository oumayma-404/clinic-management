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
                    invoice.Issue(number, clinic.VatApplicable, clinic.VatRate, clinic.StampDutyEnabled, clinic.StampDutyAmount);

                    // The totals are frozen now, so this is the first moment the carry-over can be bounded.
                    var carryOver = await CarryOverPlanPaymentsAsync(invoice, cancellationToken);
                    if (carryOver.IsFailure)
                    {
                        return Result<InvoiceDto>.Failure(carryOver.Error!);
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
            return Result.Failure(
                $"Ce devis a déjà encaissé {total:0.000} DT, soit plus que le total de la facture "
                + $"({invoice.TotalTtc:0.000} DT). Corrigez le devis ou les actes facturés avant d'émettre.");
        }

        foreach (var payment in collected)
        {
            // ⚠️ The cheque's identity travels with the money (L8). The plan side stops being counted the moment
            // this bridge invoice is issued, so a cheque left behind here would vanish from « chèques à
            // encaisser » entirely — the row that still has to be banked becoming the one row nothing lists.
            // `ToChequeDetails()` rebuilds it through `ChequeDetails.For`, so the method/details invariant is
            // re-checked on the way across rather than trusted.
            invoice.RecordPayment(
                payment.Amount, payment.Method, payment.PaidOn, payment.Id, payment.ToChequeDetails());
        }

        _logger.LogInformation(
            "Carried {Amount} DT of installment payments from plan {PlanId} onto invoice {InvoiceId}",
            total, plan.Id, invoice.Id);

        return Result.Success();
    }
}
