using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Saving a fiche de soins with an « Encaissé sur le traitement » puts that money on the treatment's échéancier —
/// the second money route out of a séance, beside <see cref="DentalRecordAutoBilling"/>'s note d'honoraires.
///
/// <para>
/// <b>Why there are two, and why neither may absorb the other.</b> A séance settles two different things. Its own
/// acts are billed on a note d'honoraires — that is <see cref="DentalRecordAutoBilling"/>. An act that spans
/// several séances is priced <b>once</b>, on its treatment, and each visit collects part of that one figure; the
/// act therefore appears on the fiche at 0 by rule (<c>PriceForPlanLinkedAct</c>), and money against it belongs
/// to the devis' échéancier. Folding the two into one field would put a share of the treatment's money on the
/// note, which is precisely the double-billing the 0 exists to prevent.
/// </para>
///
/// <para>
/// <b>The defect this closes.</b> Before it, the second route simply did not exist: the fiche labelled its
/// payment field « Encaissé aujourd'hui » on a devis séance and stated « reste 1 400 après cette séance », the
/// acts picker explained that an un-numbered treatment « collects séance by séance on the fiche » — and there
/// was no code path anywhere that could do it. The browser disabled the save for any amount typed (a 0-total
/// séance cannot take a payment), and had it got through, <c>BillDentalRecordCommand</c> would have refused with
/// « aucun acte facturable » as a warning inside an HTTP 200. The only way through was to overtype the act's 0
/// with its real fee, raising a second, unlinked claim for work the treatment already prices.
/// </para>
///
/// <para>
/// <b>Best-effort for the record, never silent about the money</b> — <see cref="DentalRecordAutoBilling"/>'s
/// contract exactly, and for its reason. It runs post-commit so a collection failure can never lose the clinical
/// record, and it reports its outcome on <see cref="DentalRecordDto.TreatmentCollection"/> rather than swallowing
/// it, so the dentist is never left believing money landed when it did not.
/// </para>
///
/// <para>
/// ⚠️ <b>Idempotent by delegation</b>, again like its sibling: it sends <see cref="CollectOnTreatmentCommand"/>,
/// which derives the increment from what this fiche has already collected. A fiche is re-saved routinely, and a
/// re-save must add the difference or nothing.
/// </para>
/// </summary>
public static class DentalRecordTreatmentCollection
{
    /// <summary>
    /// Collects <paramref name="amountCollected"/> onto <paramref name="treatmentPlanId"/>'s échéancier when the
    /// séance carries a treatment act and money was taken for it. Never throws.
    /// </summary>
    /// <param name="treatmentPlanId">
    /// The treatment this séance carries out. Null is the ordinary fiche — no treatment, nothing to collect on —
    /// and is not an error.
    /// </param>
    /// <param name="amountCollected">
    /// The séance's <b>cumulative</b> collection towards the treatment, as saved on the fiche. Zero or negative
    /// means nothing was taken for it, which is the common case (the patient pays at the end, or not today).
    /// </param>
    public static async Task<TreatmentCollectionDto?> CollectIfPaidAsync(
        ISender sender,
        DentalRecord record,
        Guid? treatmentPlanId,
        decimal amountCollected,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // No treatment on this séance: not « collected nothing », but « the question does not arise ». Reported
        // as null rather than as a NotCollected outcome, so the UI can tell the two apart.
        if (treatmentPlanId is not { } planId)
        {
            return null;
        }

        if (amountCollected <= 0m)
        {
            return new TreatmentCollectionDto
            {
                Outcome = nameof(TreatmentCollectionOutcome.NotCollected),
                TreatmentPlanId = planId,
            };
        }

        try
        {
            var result = await sender.Send(
                new CollectOnTreatmentCommand
                {
                    TreatmentPlanId = planId,
                    DentalRecordId = record.Id,
                    Amount = amountCollected,
                    // The fiche's own method, never a hard-coded Cash — the mistake `DentalRecordAutoBilling`
                    // already had to correct: a séance settled by cheque produced a payment absent from
                    // « Chèques à encaisser » and counted under « dont espèces ».
                    Method = (record.PaymentMethod ?? Domain.Enums.PaymentMethod.Cash).ToString(),
                    ChequeNumber = record.ChequeNumber,
                    ChequeBankName = record.ChequeBankName,
                    ChequeDueDate = record.ChequeDueDate,
                    // The séance's own date, not "now": a fiche recorded two days late was paid on the day it
                    // happened, and booking that cash to today puts it in the wrong day's caisse.
                    PaidOn = record.InterventionDate,
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                var collection = result.Value!;
                return new TreatmentCollectionDto
                {
                    Outcome = collection.Outcome.ToString(),
                    TreatmentPlanId = planId,
                    PlanNumber = collection.PlanNumber,
                    AmountCollected = collection.AmountCollected,
                    Outstanding = collection.Outstanding,
                    DevisIssued = collection.DevisIssued,
                    Message = collection.Message,
                };
            }

            // A rule said no, the user has a defined next step, and the message names it. Told apart from a
            // genuine failure by the CODE — never by the sentence.
            if (IsRefusal(result.Code))
            {
                logger.LogInformation(
                    "Fiche {RecordId} saved; collection on treatment {PlanId} refused ({Code}): {Error}",
                    record.Id, planId, result.Code, result.Error);
                return new TreatmentCollectionDto
                {
                    Outcome = nameof(TreatmentCollectionOutcome.Refused),
                    TreatmentPlanId = planId,
                    Message = result.Error,
                };
            }

            logger.LogWarning(
                "Fiche {RecordId} saved, but collecting on treatment {PlanId} failed: {Error}",
                record.Id, planId, result.Error);
            return new TreatmentCollectionDto
            {
                Outcome = nameof(TreatmentCollectionOutcome.Refused),
                TreatmentPlanId = planId,
                Message = result.Error,
            };
        }
        catch (Exception ex)
        {
            // The record is already committed. Log at Error — a genuine bug here must stay discoverable — and
            // tell the caller, so the user is never left believing the money landed.
            logger.LogError(
                ex, "Collecting on treatment {PlanId} threw for fiche {RecordId}; the record itself is saved",
                planId, record.Id);
            return new TreatmentCollectionDto
            {
                Outcome = nameof(TreatmentCollectionOutcome.Refused),
                TreatmentPlanId = planId,
                Message = "La fiche est enregistrée, mais l'encaissement sur le traitement a échoué.",
            };
        }
    }

    private static bool IsRefusal(string? code) =>
        code is TreatmentCollectionRefusals.CollectionLoweredCode
            or TreatmentCollectionRefusals.ExceedsOutstandingCode;
}
