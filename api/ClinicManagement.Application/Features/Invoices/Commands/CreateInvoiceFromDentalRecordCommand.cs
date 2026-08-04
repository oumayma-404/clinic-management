using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Bill a fiche de soins: raise the note d'honoraires from the session's acts, issue it, and — when the patient
/// paid on the spot — record that payment, all in one action.
///
/// <para><b>The gap this closes.</b> <c>DentalRecord.AmountPaid</c> was read by nothing but the fiche's own
/// display. A dentist could type an amount there, see it on screen, and it would never appear in la caisse, on
/// the dashboard, or in the patient's balance — a field shaped exactly like a receipt that no money read has ever
/// touched. Cash reaches the till through the invoice <c>Payment</c> ledger and the devis
/// <c>InstallmentPayment</c> ledger, and nothing else; so the fix is to make the fiche able to produce a real
/// payment on a real numbered document, not to teach a fourth read about a fourth source.</para>
///
/// <para><b>Issuing is mandatory when money is taken, and that is a real cost.</b> A payment can only exist on an
/// <c>Issued</c> invoice, so this consumes a gapless per-clinic number. In Tunisia that is arguably the correct
/// outcome — if the patient paid, the note d'honoraires <b>is</b> the legal receipt — but it is irreversible: a
/// mis-keyed amount is corrected with an <b>avoir</b>, never an edit. The UI must confirm before saving.</para>
///
/// <para><b>Atomic.</b> Create → issue → pay is one <c>SaveChangesAsync</c> inside one transaction. Composing the
/// three existing commands would leave a half-issued invoice with no payment reachable on any failure.</para>
///
/// <para>The sibling of <c>CreateInvoiceFromTreatmentPlanCommand</c>. Until now the fiche→facture path existed only
/// as a <b>frontend prefill</b> into <c>CreateInvoiceCommand</c>, which is why the pricing rule lived in the
/// browser (see <see cref="DentalRecordInvoiceLines"/>).</para>
/// </summary>
public class CreateInvoiceFromDentalRecordCommand : IRequest<Result<InvoiceDto>>
{
    public Guid DentalRecordId { get; set; }

    /// <summary>
    /// The cash taken at the end of the session, or null to bill without collecting. Null is the « facturer, le
    /// patient paiera plus tard » path and leaves the invoice <c>Issued</c> with nothing collected.
    /// </summary>
    public DentalRecordPaymentRequest? PaidNow { get; set; }
}

/// <summary>What the patient handed over at the end of the session.</summary>
public class DentalRecordPaymentRequest
{
    public decimal Amount { get; set; }

    /// <summary>Cash | Cheque | Card | Transfer.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Defaults to the fiche's own intervention date rather than "now": a session recorded two days late was paid
    /// on the day it happened, and booking that cash to today would put it in the wrong day's caisse.
    /// </summary>
    public DateTime? PaidOn { get; set; }

    /// <summary>
    /// The cheque's number, bank and due date (L8) — optional, and refused for any method but <c>Cheque</c>. A
    /// patient handing over a cheque at the end of a session is exactly as common here as at the till, so the
    /// fiche's own billing path carries the fields rather than being the one route that drops them.
    /// </summary>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="ChequeNumber"/>
    public DateTime? ChequeDueDate { get; set; }
}

public class CreateInvoiceFromDentalRecordCommandHandler
    : IRequestHandler<CreateInvoiceFromDentalRecordCommand, Result<InvoiceDto>>
{
    private const int MaxNumberingAttempts = 5;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IDentalRecordRepository _recordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateInvoiceFromDentalRecordCommandHandler> _logger;

    public CreateInvoiceFromDentalRecordCommandHandler(
        IInvoiceRepository invoiceRepository,
        IDentalRecordRepository recordRepository,
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateInvoiceFromDentalRecordCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _recordRepository = recordRepository;
        _patientRepository = patientRepository;
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        CreateInvoiceFromDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var record = await _recordRepository.GetByIdAsync(request.DentalRecordId, cancellationToken);
            if (record == null)
            {
                return Result<InvoiceDto>.Failure("Fiche de soins introuvable.");
            }

            // A DentalRecord has no ClinicId — it is a child of Patient — so the tenant check goes through the
            // patient, and a cross-clinic fiche must read as "not found" rather than 403.
            var patient = await _patientRepository.GetByIdAsync(record.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Fiche de soins introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<InvoiceDto>.Failure("Cabinet introuvable.");
            }

            // Already billed? The light act-level projection exists for exactly this question; loading every
            // invoice of the patient with its lines and payments to answer it is the § 9.7 over-fetch.
            var recordLinks = await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken);
            var existing = recordLinks.FirstOrDefault(
                l => l.DentalRecordId == record.Id && PlanBillingRules.RepresentsItsPlan(l.Status));
            if (existing.InvoiceId != Guid.Empty)
            {
                // Name the invoice: « déjà facturée » with no number sends the user hunting through /factures.
                return Result<InvoiceDto>.Failure(existing.Number is null
                    ? "Cette fiche de soins est déjà facturée sur un brouillon de note d'honoraires."
                    : $"Cette fiche de soins est déjà facturée sur la note n° {existing.Number}.");
            }

            var lines = DentalRecordInvoiceLines.For(record);
            if (lines.Count == 0 || lines.All(l => l.UnitPriceHt <= 0m))
            {
                return Result<InvoiceDto>.Failure("Cette fiche de soins ne comporte aucun acte facturable.");
            }

            // The payment date is validated BEFORE a number is consumed. Failing after the invoice is issued
            // would leave a numbered, unpaid note behind for a typo in a date field.
            PaymentMethod? method = null;
            DateTime paidOn = default;
            ChequeDetails? cheque = null;
            if (request.PaidNow is { } paidNow)
            {
                if (paidNow.Amount <= 0m)
                {
                    return Result<InvoiceDto>.Failure("Le montant encaissé doit être supérieur à 0.");
                }
                if (!Enum.TryParse<PaymentMethod>(paidNow.Method, ignoreCase: true, out var parsedMethod))
                {
                    return Result<InvoiceDto>.Failure("Mode de paiement invalide.");
                }
                method = parsedMethod;

                // Resolved HERE, with the rest of the pre-flight, and not at the `RecordPayment` call below: this
                // command's whole shape is that every refusal happens before a gapless number is consumed, and
                // `ChequeDetails.For` throws on cheque details attached to a non-cheque method. Building it inside
                // the transaction would leave a numbered, unpaid note behind for a mis-set form field.
                try
                {
                    cheque = ChequeDetails.For(
                        parsedMethod, paidNow.ChequeNumber, paidNow.ChequeBankName, paidNow.ChequeDueDate);
                }
                catch (ArgumentException ex)
                {
                    return Result<InvoiceDto>.Failure(ex.Message);
                }

                paidOn = paidNow.PaidOn ?? record.InterventionDate;
                var dateError = PaymentDateRules.Validate(paidOn, "La date de paiement");
                if (dateError != null)
                {
                    return Result<InvoiceDto>.Failure(dateError);
                }

                // Over-payment is checked here, against the TTC the invoice *will* freeze, and not after
                // `Issue()` — every refusal must happen before a number is consumed, or a typo in an amount
                // leaves a numbered, unpaid note d'honoraires behind. `InvoiceCalculator.Compute` with the
                // clinic's own VAT/stamp settings is the same arithmetic `Issue` runs, from the same authority.
                var expectedTtc = InvoiceCalculator.Compute(
                    lines.Sum(l => InvoiceCalculator.LineTotal(l.Quantity, l.UnitPriceHt)),
                    clinic.VatApplicable,
                    clinic.VatApplicable ? clinic.VatRate : 0m,
                    clinic.StampDutyEnabled ? clinic.StampDutyAmount : 0m).TotalTtc;

                if (paidNow.Amount > expectedTtc)
                {
                    return Result<InvoiceDto>.Failure(
                        "Le montant encaissé dépasse le total de la note d'honoraires.");
                }
            }

            var invoice = new Invoice(Guid.NewGuid(), clinicId, record.PatientId, dentalRecordId: record.Id);

            // L9 — the attribution travels with the money. The fiche already knows who performed the séance, so
            // the note d'honoraires it produces takes that practitioner verbatim rather than re-deriving it: this
            // command bills work that has already happened, and « who earned it » was settled when the fiche was
            // saved. Re-resolving here would let the *biller* (often reception) be credited instead.
            invoice.SetDoctor(record.DoctorId);
            invoice.SetLines(lines.Select(l =>
                (l.Designation, l.Quantity, l.UnitPriceHt, (Guid?)record.Id, (Guid?)null, (string?)null)));

            // One transaction around the whole create → issue → pay chain (see the type remarks).
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _invoiceRepository.AddAsync(invoice, cancellationToken);

                var year = ClinicClock.ClinicYear();
                for (var attempt = 1; attempt <= MaxNumberingAttempts; attempt++)
                {
                    var nextSequence =
                        await _invoiceRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
                    var number = $"{year}-{nextSequence:D4}";

                    if (attempt == 1)
                    {
                        invoice.Issue(
                            number, clinic.VatApplicable, clinic.VatRate, clinic.StampDutyEnabled, clinic.StampDutyAmount);

                        // Only now are the totals frozen, so this is the first moment an over-payment can be
                        // refused against a real TTC rather than a draft's HT.
                        if (method is { } resolvedMethod)
                        {
                            // Already bounded above; the aggregate refuses an over-payment too, so a drift between
                            // the pre-check and `Issue`'s own arithmetic surfaces as a rolled-back failure rather
                            // than a wrong total.
                            invoice.RecordPayment(
                                request.PaidNow!.Amount, resolvedMethod, paidOn, cheque: cheque);
                        }
                    }
                    else
                    {
                        // A concurrent issuance took our number; keep the frozen totals and the payment, reassign
                        // the number only. Same shape as IssueInvoiceCommand's retry.
                        invoice.SetIssuedNumber(number);
                    }

                    await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

                    try
                    {
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        await _unitOfWork.CommitTransactionAsync(cancellationToken);

                        _logger.LogInformation(
                            "Billed dental record {RecordId} as invoice {Number} (collected {Amount})",
                            record.Id, invoice.Number, invoice.AmountCollected);

                        return Result<InvoiceDto>.Success(invoice.ToDto(patient.GetFullName()));
                    }
                    catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
                    {
                        _logger.LogWarning(
                            "Invoice number {Number} collided billing a fiche on attempt {Attempt}; recomputing",
                            number, attempt);
                    }
                }

                // Every retry collided. Roll back explicitly: this is the one non-throwing exit inside the
                // transaction, and leaving it open would hold the connection for the rest of the request.
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(
                    "Impossible d'attribuer un numéro de facture unique. Veuillez réessayer.");
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
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
            _logger.LogError(ex, "Error billing dental record {RecordId}", request.DentalRecordId);
            return Result<InvoiceDto>.Failure("Erreur lors de la facturation de la fiche de soins.");
        }
    }
}
