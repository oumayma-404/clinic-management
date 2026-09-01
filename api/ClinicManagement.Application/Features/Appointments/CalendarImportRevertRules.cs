namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// Everything <see cref="CalendarImportRevertRules"/> needs about one imported visit. A parameter object rather
/// than the <c>Appointment</c> entity, on <see cref="VisitClosureInput"/>'s precedent and for its reason: the
/// question is about the visit <i>and its surroundings</i>, none of which the aggregate can see — and keeping it
/// a plain value is what makes the whole truth table testable with no repository in sight.
/// </summary>
/// <param name="AppointmentId">The visit.</param>
/// <param name="HasFiche">A fiche de soins names it.</param>
/// <param name="HasLiveInvoice">A non-cancelled note d'honoraires names it, directly or through its fiche.</param>
/// <param name="CoveredByPlan">It carries a debt-bearing devis step.</param>
/// <param name="HasLabOrder">A bon de prothèse names it.</param>
/// <param name="HasProcedures">Somebody put acts on it. An imported visit is created with none.</param>
/// <param name="NothingToBill">Somebody recorded that it raises no document — a human decision about this visit.</param>
/// <param name="Disregarded">Somebody took it off the worklist. <b>Not a blocker</b> — see the rules.</param>
public readonly record struct ImportedVisit(
    Guid AppointmentId,
    bool HasFiche,
    bool HasLiveInvoice,
    bool CoveredByPlan,
    bool HasLabOrder,
    bool HasProcedures,
    bool NothingToBill,
    bool Disregarded);

/// <summary>
/// What an « Annuler cet import » may and may not delete.
///
/// <para><b>The whole rule in one sentence: an import may be undone, work may not.</b> A run's rows are deletable
/// because nothing of the practice's own is invested in them — the moment something is, the row stops being an
/// import artefact and becomes a record, and the undo keeps it and says so.</para>
///
/// <para><b>Why the blockers are named rather than counted.</b> A revert that silently keeps four rows leaves a
/// practice looking at a list it was told would be empty, with nothing to explain the difference. Each blocker is
/// a French phrase the screen prints beside the visit, so « pourquoi cette séance est-elle restée ? » is answered
/// where it is asked.</para>
///
/// <para>⚠️ <b>A cancelled note d'honoraires is deliberately not a blocker</b>, matching
/// <c>AppointmentInvoiceLinks</c>' own exclusion: it bills nothing, so it is not work invested in the visit — and
/// treating it as one would strand exactly the visits a practice cancelled while trying to tidy up.</para>
///
/// <para>⚠️ <b>Neither is « retirée de la liste ».</b> Setting a row aside is a statement that it should not be
/// there at all, which is the same thing the undo is about to act on — refusing to delete it would mean the two
/// remedies for one problem cancel each other out, and a practice that used the quick one first could never use
/// the real one.</para>
/// </summary>
public static class CalendarImportRevertRules
{
    /// <summary>French, because it is printed. One phrase per reason, so the screen never has to compose one.</summary>
    public const string FicheBlocker = "une fiche de soins y est enregistrée";
    public const string InvoiceBlocker = "une note d'honoraires la facture";
    public const string PlanBlocker = "elle porte une étape de devis";
    public const string LabOrderBlocker = "un bon de prothèse y est rattaché";
    public const string ProceduresBlocker = "des actes y ont été saisis";
    public const string NothingToBillBlocker = "quelqu'un a noté qu'elle ne serait pas facturée";

    /// <summary>
    /// Why this visit must survive the undo, or an empty list when nothing stands in the way.
    ///
    /// <para>Every reason is returned, not the first: « une fiche de soins y est enregistrée et une note
    /// d'honoraires la facture » tells a practice something a single reason does not, and the caller decides how
    /// many to print.</para>
    /// </summary>
    public static IReadOnlyList<string> BlockersFor(in ImportedVisit visit)
    {
        var blockers = new List<string>();

        if (visit.HasFiche)
        {
            blockers.Add(FicheBlocker);
        }

        if (visit.HasLiveInvoice)
        {
            blockers.Add(InvoiceBlocker);
        }

        if (visit.CoveredByPlan)
        {
            blockers.Add(PlanBlocker);
        }

        if (visit.HasLabOrder)
        {
            blockers.Add(LabOrderBlocker);
        }

        if (visit.HasProcedures)
        {
            blockers.Add(ProceduresBlocker);
        }

        // A recorded « rien à facturer » is a colleague's decision about this séance, which is exactly the kind of
        // investment that turns an import artefact into a record. It is the one blocker that is a fact about a
        // person rather than about the work.
        if (visit.NothingToBill)
        {
            blockers.Add(NothingToBillBlocker);
        }

        return blockers;
    }

    /// <summary>True when the undo may delete this visit.</summary>
    public static bool CanDelete(in ImportedVisit visit) => BlockersFor(visit).Count == 0;

    /// <summary>
    /// « une fiche de soins y est enregistrée et une note d'honoraires la facture » — the enumeration the row
    /// prints, so the reason reads as a sentence rather than a list of fragments.
    /// </summary>
    public static string Describe(IReadOnlyList<string> blockers) => blockers.Count switch
    {
        0 => string.Empty,
        1 => blockers[0],
        _ => string.Join(", ", blockers.Take(blockers.Count - 1)) + " et " + blockers[^1]
    };
}
