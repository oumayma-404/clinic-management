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
/// paid on the spot — record that payment, all in one action. On a fiche that is <b>already</b> billed it tops the
/// existing note up rather than refusing.
///
/// <para><b>The gap this closes.</b> <c>DentalRecord.AmountPaid</c> was read by nothing but the fiche's own
/// display. A dentist could type an amount there, see it on screen, and it would never appear in la caisse, on
/// the dashboard, or in the patient's balance — a field shaped exactly like a receipt that no money read has ever
/// touched. Cash reaches the till through the invoice <c>Payment</c> ledger and the devis
/// <c>InstallmentPayment</c> ledger, and nothing else; so the fix is to make the fiche able to produce a real
/// payment on a real numbered document, not to teach a fourth read about a fourth source.</para>
///
/// <para><b>And the second half of that gap was the re-save.</b> A fiche is edited routinely — « le patient a
/// réglé le reste » is the most ordinary edit there is — and the already-billed case used to be a flat refusal, so
/// the extra cash was silently lost in exactly the same way the original field lost it. Raising « Montant payé »
/// now records the difference as an additional payment on the <b>same</b> note (AC-1): one document, one séance.
/// Lowering it, changing the acts, or a note that is cancelled or fully credited are refusals with a
/// <see cref="Result.Code"/> — see <see cref="DentalRecordBillingGuard"/>.</para>
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
public class BillDentalRecordCommand : IRequest<Result<DentalRecordBillingResult>>
{
    public Guid DentalRecordId { get; set; }

    /// <summary>
    /// The cash taken at the end of the session, or null to bill without collecting. Null is the « facturer, le
    /// patient paiera plus tard » path and leaves the invoice <c>Issued</c> with nothing collected.
    ///
    /// <para>⚠️ On an already-billed fiche this is the séance's <b>cumulative</b> settled amount — the same
    /// meaning « Montant payé » has on the fiche — not the increment. The increment is derived from what the note
    /// has already collected, because the fiche is what the user edits and the difference is arithmetic nobody
    /// should be asked to do in their head.</para>
    /// </summary>
    public DentalRecordPaymentRequest? PaidNow { get; set; }

    /// <summary>
    /// True when this came from saving the fiche rather than from « Facturer cette intervention ».
    ///
    /// <para>It changes exactly one decision (A-1): when the only note billing this séance is <b>cancelled</b>, an
    /// automatic call refuses and names it, while a manual one raises a fresh note. The acceptance criterion is
    /// « never <i>silently</i> create a second document » — and a re-save is the silent path, whereas pressing
    /// « Facturer cette intervention » on a séance whose note was annulée is precisely the deliberate act of
    /// re-billing it. Defaulting to false keeps the manual route, so a caller that forgets the flag gets the
    /// door that asks nothing of it.</para>
    /// </summary>
    public bool IsAutomatic { get; set; }
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

public class BillDentalRecordCommandHandler
    : IRequestHandler<BillDentalRecordCommand, Result<DentalRecordBillingResult>>
{
    private const int MaxNumberingAttempts = 5;

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IDentalRecordRepository _recordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BillDentalRecordCommandHandler> _logger;

    public BillDentalRecordCommandHandler(
        IInvoiceRepository invoiceRepository,
        IDentalRecordRepository recordRepository,
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<BillDentalRecordCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _recordRepository = recordRepository;
        _patientRepository = patientRepository;
        _clinicRepository = clinicRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DentalRecordBillingResult>> Handle(
        BillDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalRecordBillingResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var record = await _recordRepository.GetByIdAsync(request.DentalRecordId, cancellationToken);
            if (record == null)
            {
                return Result<DentalRecordBillingResult>.Failure("Fiche de soins introuvable.");
            }

            // A DentalRecord has no ClinicId of its own on the read path — it is a child of Patient — so the tenant
            // check goes through the patient, and a cross-clinic fiche must read as "not found" rather than 403.
            var patient = await _patientRepository.GetByIdAsync(record.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<DentalRecordBillingResult>.Failure("Fiche de soins introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                return Result<DentalRecordBillingResult>.Failure("Cabinet introuvable.");
            }

            // Already billed? One light projection plus, at most, one invoice — the same read the pre-commit guard
            // in UpdateDentalRecordCommand takes, so the backstop cannot disagree with the guard.
            var existing = await DentalRecordBillingGuard.LoadAsync(
                _invoiceRepository, _creditNoteRepository, clinicId, record.Id, cancellationToken);

            if (existing is { } billed && !(billed.Status == InvoiceStatus.Cancelled && !request.IsAutomatic))
            {
                return await TopUpAsync(request, record, patient, billed, cancellationToken);
            }

            return await RaiseNewNoteAsync(request, record, patient, clinic, clinicId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Result<DentalRecordBillingResult>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<DentalRecordBillingResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error billing dental record {RecordId}", request.DentalRecordId);
            return Result<DentalRecordBillingResult>.Failure("Erreur lors de la facturation de la fiche de soins.");
        }
    }

    /// <summary>
    /// The already-billed branch (AC-1, AC-2, AC-3b, A-1). Replaces what used to be an unconditional refusal.
    /// </summary>
    private async Task<Result<DentalRecordBillingResult>> TopUpAsync(
        BillDentalRecordCommand request,
        DentalRecord record,
        Patient patient,
        DentalRecordBillingGuard.Snapshot billed,
        CancellationToken cancellationToken)
    {
        var requested = request.PaidNow is { } paid ? InvoiceCalculator.RoundMoney(paid.Amount) : 0m;

        // The shared guard: cancelled / fully credited, acts moved, amount lowered. Its refusals carry the codes
        // the client branches on, and the wording is the same the pre-commit guard already showed the user.
        var allowed = DentalRecordBillingGuard.Check(billed, record.Cost, requested);
        if (allowed.IsFailure)
        {
            return Result<DentalRecordBillingResult>.FailureFrom(allowed);
        }

        var invoice = await _invoiceRepository.GetByIdAsync(billed.InvoiceId, cancellationToken);
        if (invoice == null)
        {
            return Result<DentalRecordBillingResult>.Failure("Note d'honoraires introuvable.");
        }

        var delta = InvoiceCalculator.RoundMoney(requested - invoice.AmountCollected);
        if (delta <= 0m)
        {
            // Nothing to add — the ordinary outcome of re-saving a fiche whose money is already in the till. It is
            // an outcome and not an error, which is the whole reason this command returns a typed result.
            return Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
            {
                Outcome = DentalRecordBillingOutcome.AlreadyBilled,
                Invoice = invoice.ToDto(patient.GetFullName()),
                AmountCollected = 0m,
                Message = billed.Number is null
                    ? "Cette fiche de soins est déjà facturée sur un brouillon de note d'honoraires."
                    : $"Cette fiche de soins est déjà facturée sur la note n° {billed.Number}."
            });
        }

        var payment = ResolvePayment(request, record, delta, invoice.TotalTtc - invoice.AmountCollected);
        if (payment.IsFailure)
        {
            return Result<DentalRecordBillingResult>.FailureFrom(payment);
        }
        var (method, paidOn, cheque) = payment.Value;

        // No transaction and no numbering retry: the document already exists and keeps its number, so this is one
        // payment row on a tracked aggregate — the same shape as RecordPaymentCommand.
        invoice.RecordPayment(delta, method, paidOn, cheque: cheque);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Topped up invoice {Number} from fiche {RecordId} by {Amount}", invoice.Number, record.Id, delta);

        return Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
        {
            Outcome = DentalRecordBillingOutcome.ToppedUp,
            Invoice = invoice.ToDto(patient.GetFullName()),
            AmountCollected = delta
        });
    }

    /// <summary>The create → issue → pay chain, unchanged in substance from the original command.</summary>
    private async Task<Result<DentalRecordBillingResult>> RaiseNewNoteAsync(
        BillDentalRecordCommand request,
        DentalRecord record,
        Patient patient,
        Clinic clinic,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        var lines = DentalRecordInvoiceLines.For(record);
        if (lines.Count == 0 || lines.All(l => l.UnitPriceHt <= 0m))
        {
            return Result<DentalRecordBillingResult>.Failure("Cette fiche de soins ne comporte aucun acte facturable.");
        }

        // Over-payment is checked against the total the invoice *will* freeze, and not after `Issue()` — every
        // refusal must happen before a number is consumed, or a typo in an amount leaves a numbered, unpaid note
        // d'honoraires behind. `InvoiceCalculator.Compute` is the same arithmetic `Issue` runs, from the same
        // authority — which is now simply the sum of the acts, since there is no TVA and no timbre to add. That
        // is what makes the fiche de soins' « Reste à payer » true: the figure the dentist reads chairside and
        // the total of the note this produces are the same number by construction.
        var expectedTtc = InvoiceCalculator.Compute(
            lines.Sum(l => InvoiceCalculator.LineTotal(l.Quantity, l.UnitPriceHt))).TotalTtc;

        PaymentMethod? method = null;
        DateTime paidOn = default;
        ChequeDetails? cheque = null;
        if (request.PaidNow is not null)
        {
            var payment = ResolvePayment(request, record, request.PaidNow.Amount, expectedTtc);
            if (payment.IsFailure)
            {
                return Result<DentalRecordBillingResult>.FailureFrom(payment);
            }
            (method, paidOn, cheque) = payment.Value;
        }

        // ⚠️ `appointmentId` is passed, and it is not decoration. `Invoice.AppointmentId` is what
        // `IInvoiceRepository.GetAppointmentLinksAsync` reads to answer « cette consultation a-t-elle été
        // facturée ? », which the agenda renders as « Facturé ». It used to be omitted here while the fiche
        // carried the id all along, so a visit billed through its own fiche de soins — the product's most common
        // billing route — read as unbilled for ever, and staff could raise a second note for work already
        // invoiced. The fiche's own appointment is the right answer: this note bills that visit.
        var invoice = new Invoice(
            Guid.NewGuid(),
            clinicId,
            record.PatientId,
            dentalRecordId: record.Id,
            appointmentId: record.AppointmentId);

        // L9 — the attribution travels with the money. The fiche already knows who performed the séance, so the
        // note d'honoraires it produces takes that practitioner verbatim rather than re-deriving it: this command
        // bills work that has already happened, and « who earned it » was settled when the fiche was saved.
        // Re-resolving here would let the *biller* (often reception) be credited instead.
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
                    invoice.Issue(number);

                    if (method is { } resolvedMethod)
                    {
                        // Already bounded above; the aggregate refuses an over-payment too, so a drift between the
                        // pre-check and `Issue`'s own arithmetic surfaces as a rolled-back failure rather than a
                        // wrong total.
                        invoice.RecordPayment(
                            request.PaidNow!.Amount, resolvedMethod, paidOn, cheque: cheque);
                    }
                }
                else
                {
                    // A concurrent issuance took our number; keep the frozen totals and the payment, reassign the
                    // number only. Same shape as IssueInvoiceCommand's retry.
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

                    return Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
                    {
                        Outcome = DentalRecordBillingOutcome.Billed,
                        Invoice = invoice.ToDto(patient.GetFullName()),
                        AmountCollected = invoice.AmountCollected
                    });
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
            return Result<DentalRecordBillingResult>.Failure(
                "Impossible d'attribuer un numéro de facture unique. Veuillez réessayer.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Validates the method, the cheque details and the date, and bounds the amount by
    /// <paramref name="collectable"/> — all of it <b>before</b> anything is written.
    ///
    /// <para>Shared by both branches, and on the create path that ordering is load-bearing: every refusal must
    /// happen before a gapless number is consumed, or a mis-set form field leaves a numbered, unpaid note behind.
    /// <c>ChequeDetails.For</c> throws on details attached to a non-cheque method, so it is called here rather
    /// than inside the transaction.</para>
    /// </summary>
    /// <param name="amount">
    /// What will actually be recorded — the cumulative figure on the create path, and only the <b>difference</b>
    /// on a top-up. Passed explicitly rather than read off <c>PaidNow.Amount</c>: on a top-up the request carries
    /// the séance's whole settled total, so bounding that against what the note has *left* to take would refuse
    /// every ordinary « le patient a fini de payer » edit.
    /// </param>
    private static Result<(PaymentMethod Method, DateTime PaidOn, ChequeDetails? Cheque)> ResolvePayment(
        BillDentalRecordCommand request, DentalRecord record, decimal amount, decimal collectable)
    {
        var paidNow = request.PaidNow!;

        if (amount <= 0m)
        {
            return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Failure(
                "Le montant encaissé doit être supérieur à 0.");
        }

        if (!Enum.TryParse<PaymentMethod>(paidNow.Method, ignoreCase: true, out var method))
        {
            return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Failure("Mode de paiement invalide.");
        }

        ChequeDetails? cheque;
        try
        {
            cheque = ChequeDetails.For(method, paidNow.ChequeNumber, paidNow.ChequeBankName, paidNow.ChequeDueDate);
        }
        catch (ArgumentException ex)
        {
            return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Failure(ex.Message);
        }

        var paidOn = paidNow.PaidOn ?? record.InterventionDate;
        var dateError = PaymentDateRules.Validate(paidOn, "La date de paiement");
        if (dateError != null)
        {
            return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Failure(dateError);
        }

        // On a top-up `collectable` is what the note has left to take, so the same sentence covers both branches:
        // in either case the money offered exceeds what this document can hold.
        if (InvoiceCalculator.RoundMoney(amount) > InvoiceCalculator.RoundMoney(collectable))
        {
            return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Failure(
                "Le montant encaissé dépasse le total de la note d'honoraires.");
        }

        return Result<(PaymentMethod, DateTime, ChequeDetails?)>.Success((method, paidOn, cheque));
    }
}
