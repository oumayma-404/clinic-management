using ClinicManagement.Application.Features.Appointments;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// What « Annuler cet import » may and may not delete — the pure truth table, tested with no repository in
/// sight, on <see cref="VisitClosureRulesTests"/>' precedent.
///
/// <para>The whole rule in one sentence: <b>an import may be undone, work may not</b>. The moment something of
/// the practice's own is invested in a row it stops being an import artefact and becomes a record.</para>
/// </summary>
public class CalendarImportRevertRulesTests
{
    private static ImportedVisit Untouched() =>
        new(
            AppointmentId: Guid.NewGuid(),
            HasFiche: false,
            HasLiveInvoice: false,
            CoveredByPlan: false,
            HasLabOrder: false,
            HasProcedures: false,
            NothingToBill: false,
            Disregarded: false);

    [Fact]
    public void A_Visit_Nothing_Has_Touched_May_Be_Deleted()
    {
        var visit = Untouched();

        Assert.Empty(CalendarImportRevertRules.BlockersFor(visit));
        Assert.True(CalendarImportRevertRules.CanDelete(visit));
    }

    [Fact]
    public void A_Fiche_Blocks_The_Delete()
    {
        var visit = Untouched() with { HasFiche = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(CalendarImportRevertRules.FicheBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    [Fact]
    public void A_Live_Note_Dhonoraires_Blocks_The_Delete()
    {
        var visit = Untouched() with { HasLiveInvoice = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(CalendarImportRevertRules.InvoiceBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    /// <summary>
    /// ⚠️ A <b>cancelled</b> note is not a blocker, and this is the case that decides whether the feature is
    /// useful at all. It bills nothing, so nothing of the practice's is invested in the visit — and counting it
    /// would strand exactly the séances a cabinet cancelled while trying to tidy up after the import, which are
    /// the rows inflating its taux d'absence.
    ///
    /// <para>The rule reads a <c>HasLiveInvoice</c> the repository resolved with
    /// <c>Status != Cancelled</c>, matching <c>AppointmentInvoiceLinks</c>' own exclusion — so a cancelled note
    /// simply never reaches this input as <c>true</c>.</para>
    /// </summary>
    [Fact]
    public void A_Cancelled_Note_Is_Not_A_Blocker()
    {
        // What the repository hands the rule for a visit whose only invoice is cancelled.
        var visit = Untouched() with { HasLiveInvoice = false };

        Assert.True(CalendarImportRevertRules.CanDelete(visit));
    }

    [Fact]
    public void A_Devis_Step_Blocks_The_Delete()
    {
        var visit = Untouched() with { CoveredByPlan = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(CalendarImportRevertRules.PlanBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    [Fact]
    public void A_Bon_De_Prothese_Blocks_The_Delete()
    {
        var visit = Untouched() with { HasLabOrder = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(CalendarImportRevertRules.LabOrderBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    /// <summary>An imported visit is created with no acts, so any on it were typed by somebody.</summary>
    [Fact]
    public void Acts_Typed_On_The_Visit_Block_The_Delete()
    {
        var visit = Untouched() with { HasProcedures = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(CalendarImportRevertRules.ProceduresBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    /// <summary>
    /// The one blocker that is a fact about a <b>person</b> rather than about the work: somebody looked at this
    /// séance and recorded a decision about it.
    /// </summary>
    [Fact]
    public void A_Recorded_Rien_A_Facturer_Blocks_The_Delete()
    {
        var visit = Untouched() with { NothingToBill = true };

        Assert.False(CalendarImportRevertRules.CanDelete(visit));
        Assert.Contains(
            CalendarImportRevertRules.NothingToBillBlocker, CalendarImportRevertRules.BlockersFor(visit));
    }

    /// <summary>
    /// ⚠️ « Retirée de la liste » is deliberately <b>not</b> a blocker. Setting a row aside says it should not be
    /// there at all — the same thing the undo is about to act on — so refusing to delete it would make the two
    /// remedies for one problem cancel each other out, and a practice that reached for the quick one first could
    /// never use the real one.
    /// </summary>
    [Fact]
    public void A_Séance_Taken_Off_The_Worklist_Is_Still_Deletable()
    {
        var visit = Untouched() with { Disregarded = true };

        Assert.Empty(CalendarImportRevertRules.BlockersFor(visit));
        Assert.True(CalendarImportRevertRules.CanDelete(visit));
    }

    /// <summary>Every reason is returned, not the first — the row prints all of them.</summary>
    [Fact]
    public void Several_Blockers_Are_All_Reported()
    {
        var visit = Untouched() with { HasFiche = true, HasLiveInvoice = true };

        var blockers = CalendarImportRevertRules.BlockersFor(visit);

        Assert.Equal(2, blockers.Count);
        Assert.Equal(
            $"{CalendarImportRevertRules.FicheBlocker} et {CalendarImportRevertRules.InvoiceBlocker}",
            CalendarImportRevertRules.Describe(blockers));
    }

    [Fact]
    public void One_Blocker_Reads_As_Itself()
    {
        var blockers = CalendarImportRevertRules.BlockersFor(Untouched() with { HasFiche = true });

        Assert.Equal(CalendarImportRevertRules.FicheBlocker, CalendarImportRevertRules.Describe(blockers));
    }

    [Fact]
    public void No_Blocker_Describes_As_Nothing()
    {
        Assert.Equal(string.Empty, CalendarImportRevertRules.Describe(Array.Empty<string>()));
    }
}
