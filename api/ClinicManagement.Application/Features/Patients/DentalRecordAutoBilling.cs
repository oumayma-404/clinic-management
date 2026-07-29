using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.DTOs;
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
/// <para><b>Idempotent by delegation.</b> It sends <see cref="CreateInvoiceFromDentalRecordCommand"/> rather than
/// re-implementing anything, so the already-billed guard, the tenant check, the numbering retry, the over-payment
/// refusal and the single transaction are the <i>same</i> code the manual « Facturer cette intervention » action
/// uses. That matters most on the update path: <b>a fiche is re-saved routinely</b> — a corrected note, one more
/// tooth — and each re-save must not raise a second note d'honoraires.</para>
/// </summary>
public static class DentalRecordAutoBilling
{
    /// <summary>
    /// Bills <paramref name="record"/> when it carries a payment. Never throws.
    /// </summary>
    /// <param name="amountPaid">
    /// What the patient handed over, as saved on the fiche. Zero or negative means "nothing was collected", which
    /// is a legitimate outcome and not an error — the fiche is simply not billed, exactly as before this existed.
    /// A fiche billed later still goes through « Facturer cette intervention ».
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
                new CreateInvoiceFromDentalRecordCommand
                {
                    DentalRecordId = record.Id,
                    PaidNow = new DentalRecordPaymentRequest
                    {
                        Amount = amountPaid,
                        Method = nameof(Domain.Enums.PaymentMethod.Cash),
                        // The session's own date, not "now": a fiche recorded two days late was paid on the day it
                        // happened, and booking that cash to today puts it in the wrong day's caisse.
                        PaidOn = record.InterventionDate,
                    },
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                return new DentalRecordBillingDto
                {
                    Outcome = nameof(DentalRecordBillingOutcome.Billed),
                    InvoiceId = result.Value!.Id,
                    InvoiceNumber = result.Value.Number,
                    AmountCollected = result.Value.AmountCollected,
                };
            }

            // The commonest failure by far is « déjà facturée » on a re-save, and it is not an error the user
            // needs to act on — the money is already in the till. It is reported as its own outcome so the UI can
            // stay quiet about it instead of raising an alarm on every edit.
            var alreadyBilled = result.Error?.Contains("déjà facturée", StringComparison.OrdinalIgnoreCase) == true;
            if (alreadyBilled)
            {
                return new DentalRecordBillingDto
                {
                    Outcome = nameof(DentalRecordBillingOutcome.AlreadyBilled),
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
}
