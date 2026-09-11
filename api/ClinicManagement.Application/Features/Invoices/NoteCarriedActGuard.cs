using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// The refusal that protects a note d'honoraires whose act a continuation devis holds at <b>0</b> — written
/// <b>once</b> and shared by the two commands that can make a note stop billing (cancel and delete).
///
/// <para><b>The money it protects.</b> <c>ContinueRecordedActCommand</c>'s billed path prices the
/// already-invoiced act 0 on the new devis and deliberately leaves the note <b>unattached</b>, so the two
/// documents stay disjoint and each is counted once. The cost of that correctness is that nothing links the
/// note back to the devis: void the note and the fee is on no document at all — the devis line stays 0 (there
/// is no bridge for a cascade to follow) and the cancelled note is dropped by « Solde patient »,
/// « Créances », la caisse and the dashboard alike. A 90 DT act simply stops existing, with no error on any
/// screen.</para>
///
/// <para>⚠️ <b>The mirror case is already safe, and reading it as cover for this one is the mistake.</b>
/// Cancelling a note <i>bridged</i> to a plan hands that plan's balance straight back — pinned by
/// <c>Cancelling_The_Bridge_Invoice_Returns_The_Plan_To_The_Balance</c> — but only because a bridged plan's
/// lines were never zeroed. Here they were, at write time, so cancellation has nothing to give back.</para>
///
/// <para>⚠️ <b>Reachable in two clicks, and cheaply.</b> <c>Invoice.CanCancel</c> only blocks a note carrying a
/// live payment, and a Draft note is <i>deleted</i> rather than cancelled — so the unpaid and draft cases, which
/// are exactly the ones a continuation is most often built on, had no guard at all.</para>
///
/// <para><b>Refused rather than repaired</b>, deliberately. Moving the fee back onto the devis line would be a
/// money write inside a cancellation, on a document whose échéancier is already spread — and the honest
/// correction is the one the sentence names: undo the continuation first, then void the note. That is also the
/// precedent <c>AmendTreatmentPlanCommand</c> set for the same family of state, refusing it in as many
/// words.</para>
/// </summary>
public static class NoteCarriedActGuard
{
    /// <summary>A live devis holds an act this note bills, at 0. Clients branch on this, never on the sentence.</summary>
    public const string CarriedByPlanCode = "invoice_carries_a_treatment_plan_act";

    /// <summary>
    /// Refuse when a devis that still claims something holds an act billed on <paramref name="invoiceId"/>.
    ///
    /// <para>A <c>Cancelled</c> devis claims nothing, so it does not hold the note hostage — that is the
    /// recovery path the refusal names, and gating on anything narrower (say <c>PlanBillingRules.CarriesDebt</c>,
    /// which is false for a <c>Draft</c>) would let a followed treatment be silently emptied of its first act's
    /// fee.</para>
    /// </summary>
    /// <returns><c>Result.Success()</c> when the note may be voided; a failure carrying
    /// <see cref="CarriedByPlanCode"/> otherwise.</returns>
    public static async Task<Result> EnsureNotCarriedAsync(
        Guid clinicId,
        Invoice invoice,
        ITreatmentPlanRepository planRepository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var numbers = new List<string?>();

        // The devis that name this note directly — the ordinary case, and the only one when nothing has been
        // corrected.
        foreach (var row in await planRepository.GetPlansBilledOnInvoiceAsync(
            clinicId, invoice.Id, cancellationToken))
        {
            if (row.Status != TreatmentPlanStatus.Cancelled)
            {
                numbers.Add(row.Number);
            }
        }

        /*
         * ⚠️ **And the devis that carry an act of a fiche THIS note bills, whatever id they stored.** A note is
         * replaced, not edited: correcting a note or correcting its séance both cancel the old one and raise a
         * fresh one, and the marker keeps naming the shell. Asking by id alone would leave the *replacement* —
         * the note that actually holds the money now — unguarded, so deleting it would orphan the fee exactly as
         * before. The read path resolves the same way, by fiche, for the same reason.
         *
         * A cancelled devis claims nothing (see the summary), and an act with no marker is an ordinary devis
         * line the note has nothing to do with — so both are skipped rather than swept in by the fiche match.
         */
        var recordIds = invoice.Lines
            .Where(l => l.DentalRecordId.HasValue)
            .Select(l => l.DentalRecordId!.Value)
            .Distinct()
            .ToList();

        foreach (var recordId in recordIds)
        {
            foreach (var plan in await planRepository.GetByLinkedDentalRecordAsync(
                clinicId, recordId, cancellationToken))
            {
                if (plan.Status == TreatmentPlanStatus.Cancelled)
                {
                    continue;
                }
                if (plan.Items.Any(i => i.BilledOnInvoiceId.HasValue))
                {
                    numbers.Add(plan.Number);
                }
            }
        }

        var claiming = numbers.Distinct().OrderBy(n => n ?? string.Empty).ToList();
        if (claiming.Count == 0)
        {
            return Result.Success();
        }

        return Result.Failure(Refusal(claiming), CarriedByPlanCode);
    }

    /// <summary>
    /// The sentence. It names the devis, says what voiding the note would cost, and gives the one route out —
    /// a refusal that does not say what to do instead reads as a bug (<see cref="DentalRecordBillingRefusals"/>
    /// takes the same line).
    /// </summary>
    public static string Refusal(IEnumerable<string?> planNumbers)
    {
        var named = planNumbers
            .Select(n => n is null ? "un traitement suivi" : $"le devis n° {n}")
            .Distinct()
            .ToList();

        var subject = named.Count == 1 ? named[0] : string.Join(", ", named);

        return $"Cette note d'honoraires est reprise par {subject}, qui porte à 0 l'acte qu'elle facture : "
            + "l'annuler ou la supprimer ferait disparaître cet honoraire de tous les soldes. "
            + "Annulez d'abord le traitement, puis reprenez cette note.";
    }
}
