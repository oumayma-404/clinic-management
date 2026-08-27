using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Saving a fiche de soins with a « Montant payé » raises the note d'honoraires and records that payment — so the
/// amount the dentist typed at the end of the session actually reaches la caisse.
///
/// <para><b>Why this exists.</b> <c>DentalRecord.AmountPaid</c> was read by nothing but the fiche's own display,
/// and the form <i>pre-fills it with the session's running total</i> — so the dentist never even had to type it.
/// The result was « Montant payé 400,000 · Reste à payer 0 » on screen and zero in every money read: four fiches
/// worth 1 280 DT with no invoice, no payment row and a caisse showing nothing. A field that fills itself in with
/// the full amount and means nothing is worse than no field at all.</para>
///
/// <para><b>Best-effort for the record, never silent about the money.</b> The billing runs <b>post-commit</b>, so a
/// billing failure can never lose the clinical record — the same contract as
/// <c>IStockConsumptionService</c> and <c>INotificationGenerator</c>. But unlike those two it does <b>not</b>
/// swallow the outcome: it is reported on <see cref="DentalRecordDto.Billing"/> so the UI can say what happened.
/// Swallowing it would rebuild the original defect one layer down — the dentist would again believe money was
/// recorded when it was not, which is exactly the lesson the reminder work already paid for
/// («&#160;report the real outcome, not a proxy for it&#160;»).</para>
///
/// <para><b>Idempotent by delegation.</b> It sends <see cref="BillDentalRecordCommand"/> rather than
/// re-implementing anything, so the already-billed branch, the tenant check, the numbering retry, the
/// over-payment refusal and the single transaction are the <i>same</i> code the manual « Facturer cette
/// intervention » action uses. That matters most on the update path: <b>a fiche is re-saved routinely</b> — a
/// corrected note, one more tooth, or the patient settling the rest — and a re-save must top the existing note up
/// rather than raise a second one.</para>
///
/// <para>⚠️ <b>The outcome is read from a typed result, never from the message.</b> This helper used to recover
/// « déjà facturée » by matching that substring against the error text, so rewording a French sentence anywhere in
/// the billing command silently changed what the user was told here. The outcome and the refusal codes are data
/// now, and the substring match is gone.</para>
/// </summary>
public static class DentalRecordAutoBilling
{
    /// <summary>
    /// Bills <paramref name="record"/> when it carries a payment. Never throws.
    /// </summary>
    /// <param name="amountPaid">
    /// What the patient has handed over in total for this séance, as saved on the fiche. Zero or negative means
    /// "nothing was collected", which is a legitimate outcome and not an error — the fiche is simply not billed,
    /// exactly as before this existed. A fiche billed later still goes through « Facturer cette intervention ».
    /// </param>
    public static async Task<DentalRecordBillingDto> BillIfPaidAsync(
        ISender sender,
        DentalRecord record,
        decimal amountPaid,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (amountPaid <= 0m)
        {
            return new DentalRecordBillingDto { Outcome = nameof(DentalRecordBillingOutcome.NotCollected) };
        }

        try
        {
            var result = await sender.Send(
                new BillDentalRecordCommand
                {
                    DentalRecordId = record.Id,
                    // Saving the fiche is the *silent* path, and A-1 turns on exactly that: a séance whose note was
                    // cancelled must not quietly acquire a second one behind a routine re-save.
                    IsAutomatic = true,
                    PaidNow = new DentalRecordPaymentRequest
                    {
                        Amount = amountPaid,
                        // The fiche's own method (L8) — it used to be a hard-coded `Cash`, so a session settled by
                        // cheque produced a payment indistinguishable from notes in the drawer: absent from
                        // « Chèques à encaisser » and counted under « dont espèces ». A fiche with none recorded
                        // is cash, which is what every historical row is.
                        Method = (record.PaymentMethod ?? Domain.Enums.PaymentMethod.Cash).ToString(),
                        ChequeNumber = record.ChequeNumber,
                        ChequeBankName = record.ChequeBankName,
                        ChequeDueDate = record.ChequeDueDate,
                        // The session's own date, not "now": a fiche recorded two days late was paid on the day it
                        // happened, and booking that cash to today puts it in the wrong day's caisse.
                        PaidOn = record.InterventionDate,
                    },
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                var billing = result.Value!;
                return new DentalRecordBillingDto
                {
                    Outcome = billing.Outcome.ToString(),
                    InvoiceId = billing.Invoice.Id,
                    InvoiceNumber = billing.Invoice.Number,
                    AmountCollected = billing.AmountCollected,
                    Message = billing.Message,
                };
            }

            // A refusal is not a failure: a rule said no, the user has a defined next step, and the message names
            // it. Told apart from a genuine failure by the code — never by the sentence.
            if (IsRefusal(result.Code))
            {
                logger.LogInformation(
                    "Fiche {RecordId} saved; billing refused ({Code}): {Error}", record.Id, result.Code, result.Error);
                return new DentalRecordBillingDto
                {
                    Outcome = nameof(DentalRecordBillingOutcome.Refused),
                    Message = result.Error,
                };
            }

            logger.LogWarning(
                "Fiche {RecordId} saved, but auto-billing refused it: {Error}", record.Id, result.Error);
            return new DentalRecordBillingDto
            {
                Outcome = nameof(DentalRecordBillingOutcome.Failed),
                Message = result.Error,
            };
        }
        catch (Exception ex)
        {
            // The record is already committed. Log at Error — a genuine bug here must stay discoverable — and tell
            // the caller, so the user is never left believing the money landed.
            logger.LogError(ex, "Auto-billing threw for fiche {RecordId}; the record itself is saved", record.Id);
            return new DentalRecordBillingDto
            {
                Outcome = nameof(DentalRecordBillingOutcome.Failed),
                Message = "La fiche est enregistrée, mais la facturation automatique a échoué.",
            };
        }
    }

    private static bool IsRefusal(string? code) =>
        code is DentalRecordBillingRefusals.PaymentLoweredCode
            or DentalRecordBillingRefusals.ActsChangedCode
            or DentalRecordBillingRefusals.InvoiceNotLiveCode;
}
