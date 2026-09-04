using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-24a] Pins the treatment-plan endpoints' authorization surface, action by action.
///
/// <para><b>Rewritten by <c>adoption-qa-i-access-control-and-audit</c> (I1).</b> The old split was
/// « financial reversal -> <c>AdminOrDoctor</c>, everything else -> no method policy, inheriting a bare
/// <c>[Authorize]</c> ». That bare class attribute meant *any authenticated user, any role*, so the second group
/// was not a decision — it was the absence of one, and it let a secretary create, price, accept and delete a
/// devis. The class-level policy is now <c>AnyClinicRole</c> and the split is deliberate on both sides:</para>
///
/// <list type="bullet">
///   <item><b><c>AdminOrDoctor</c> — authoring or altering the plan.</b> Creating one numbers *and* accepts it in
///   the same save, so a gapless devis number is consumed and the amount enters « Solde patient » and
///   « Créances » immediately. That is a clinical decision with a fiscal consequence.</item>
///   <item><b>Class policy — collecting on it and printing it.</b> Recording an échéance payment and producing a
///   receipt or the devis PDF is reception's job, and gating it would break the front desk (the spec's own
///   critical edge case).</item>
/// </list>
///
/// <para>The <c>ReorderItems</c> row moved groups, and that is worth reading: it used to justify itself as
/// « cosmetic — matches the unpoliced accept/complete ». Those are no longer unpoliced, so the analogy had
/// inverted; and the sequence is what the workspace proposes booking next, which is a treatment decision.</para>
///
/// <para>This class stays even though <c>ControllerAuthorizationCoverageTests</c> now proves *every* action
/// carries a named policy: that guard cannot know which policy is the RIGHT one for a devis. Both are needed —
/// one refuses an unclassified action anywhere, this one pins the intent on the controller where money and
/// clinical authorship meet.</para>
/// </summary>
public class TreatmentPlansControllerAuthorizationTests
{
    /// <summary>
    /// Authoring or altering the plan — a clinical decision with a fiscal consequence. Same class as invoice
    /// cancel / avoir.
    /// </summary>
    private static readonly string[] AdminOrDoctorActions =
    {
        nameof(TreatmentPlansController.CancelPlan),
        nameof(TreatmentPlansController.AmendPlan),
        nameof(TreatmentPlansController.ReviseInstallments),
        // « Arrêter le traitement » / « Reprendre le traitement ». Both alter the plan itself — stopping parks
        // the unstarted acts, re-spreads the échéancier onto the surviving total and closes the devis, and
        // reopening puts every parked act back — so they belong with amend rather than with the till.
        nameof(TreatmentPlansController.StopTreatment),
        nameof(TreatmentPlansController.ReopenTreatment),
        // « Suivre ce traitement » authors a treatment and sets its total, and « Éditer le devis » consumes a
        // devis number — both are the plan's own authorship, so they sit with create and amend.
        //
        // ⚠️ The consequence is that a secretary pressing « Suivre ce traitement » in the booking dialog would
        // meet a 403, so the control is hidden for them the way « Exporter » is — the endpoint stays the
        // authority and the UI does not offer what it will refuse.
        nameof(TreatmentPlansController.StartTreatment),
        nameof(TreatmentPlansController.IssueDevis),
        // L5 — the CSV export. A file listing every devis with what each patient owes is the clinic-wide money
        // read in a more portable form than the screen, so it cannot be laxer than the screen: leaving it on the
        // class-level AnyClinicRole would have handed reception the whole receivables book in one click. The
        // drift guard below is what caught this action arriving unclassified.
        nameof(TreatmentPlansController.ExportPlans),
        // I1 — the six that used to inherit a bare [Authorize], i.e. any role. Creating a devis numbers and
        // accepts it in one save: a gapless number is consumed and the amount enters « Créances » at once.
        nameof(TreatmentPlansController.CreatePlan),
        nameof(TreatmentPlansController.UpdatePlan),
        nameof(TreatmentPlansController.AcceptPlan),
        nameof(TreatmentPlansController.CompletePlan),
        nameof(TreatmentPlansController.DeletePlan),
        // Reordering changes no money, but the sequence IS the treatment sequence — see the class remarks on why
        // its old « cosmetic » justification stopped holding.
        nameof(TreatmentPlansController.ReorderItems),
        // Voiding an installment payment alters what the patient has paid on a numbered devis — the same
        // class as cancelling an invoice or establishing an avoir, not everyday collection.
        nameof(TreatmentPlansController.VoidInstallmentPayment),
        // Group B — marking an échéance cheque encaissé en banque. It moves no money, so it is NOT here for the
        // fiscal reason its neighbours are: « quels chèques ai-je portés en banque ? » is the clinic's uncashed
        // exposure viewed one row at a time, the same clinic-wide money read as `/cheques` itself, which is
        // AdminOrDoctor. Reception collects a cheque; reconciling the drawer against the bank is the owner's.
        nameof(TreatmentPlansController.SetInstallmentPaymentBanked),
        // Marking an act réalisé auto-completes the devis once it is the last one, and it is the clinical
        // assertion the invoice is built from. It carried NO policy at all before (adjacent defect A-13), so a
        // secretary could close a devis. Moved out of AnyAuthenticatedActions deliberately.
        nameof(TreatmentPlansController.MarkItemDone),
        // The correction path for that same assertion — it reopens a closed devis, so it cannot be looser.
        nameof(TreatmentPlansController.UnmarkItemDone),
        // Multi-séance acts. The step-level twin of UnmarkItemDone: detaching one step of a bridge from the fiche
        // that evidenced it reopens the act, and once it was the last step, the devis with it. It cannot be
        // looser than the act-level row beside it.
        nameof(TreatmentPlansController.UnmarkItemStep),
        // Setting an act's steps moves NO money — the price, the total and the échéancier are all untouched, and
        // it does not bump the revision — so it is NOT here for the fiscal reason most of this group is. It is
        // here for ReorderItems' reason, which this controller has already settled: the sequence *is* the
        // treatment sequence, and « ce bridge se fait en trois séances, pas en quatre » is a clinical judgement
        // rather than front-desk work. Classifying it as reception's while its coarser sibling is the dentist's
        // would be exactly the drift this guard exists to catch.
        nameof(TreatmentPlansController.SetItemSteps),
        // Turning an act already carried out into a treatment. It consumes a gapless devis number in the same
        // save — CreatePlan's own reason — and it can additionally ATTACH an issued note d'honoraires to the plan
        // it creates, which decides whether that plan appears in « Solde patient » at all. Nothing else on this
        // controller reaches across to a numbered note; classifying it as reception's would put the clinic's own
        // billing arithmetic behind the front desk.
        nameof(TreatmentPlansController.ContinueRecordedAct),
    };

    /// <summary>
    /// Reading the plan, collecting on it, printing it — reception's job, so they run on the controller's
    /// <c>AnyClinicRole</c> with no method-level policy of their own.
    /// </summary>
    private static readonly string[] AnyClinicRoleActions =
    {
        nameof(TreatmentPlansController.GetPlans),
        nameof(TreatmentPlansController.GetPlan),
        // The one row that must never tighten: reception takes the money. Gating this is the spec's own
        // « the product becomes unusable at the front desk » failure.
        nameof(TreatmentPlansController.RecordInstallmentPayment),
        nameof(TreatmentPlansController.GetDevisPdf),
        nameof(TreatmentPlansController.GetInstallmentReceiptPdf),
        // « Traitements en cours » — the acts started and not finished, with the next step to book. Reception is
        // exactly who acts on this list, which is the same reasoning that kept the visit-closure worklist off
        // the AdminOrDoctor dashboard endpoint. It carries no money figure at all, which is what makes the
        // wider audience safe: a devis total or a « reste à payer » on it would put it in the group above.
        nameof(TreatmentPlansController.GetTreatmentsInProgress),
        // The candidate séances behind « C'est la suite d'une séance précédente ? ». A READ, and reception books
        // the visit that finishes a treatment — the same reasoning as the worklist above. It is deliberately NOT
        // grouped with its own write one row down: this returns figures from a note the patient has already been
        // handed (« reste 200 DT sur F-2026-0142 »), which is that patient's own balance rather than the clinic's
        // — the distinction `GetPatientBillingSummaryQuery` is already open to reception for.
        nameof(TreatmentPlansController.GetContinuableActs),
    };

    [Theory]
    [MemberData(nameof(AdminOrDoctorActionData))]
    public void Financial_Reversal_Endpoints_Require_AdminOrDoctor(string action) // [AC-24a]
    {
        var method = typeof(TreatmentPlansController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AdminOrDoctor, authorize!.Policy);
    }

    [Theory]
    [MemberData(nameof(AnyClinicRoleActionData))]
    public void Collection_And_Print_Endpoints_Carry_No_Method_Level_Policy(string action) // [AC-24a][I1]
    {
        var method = typeof(TreatmentPlansController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        // They inherit the class-level AnyClinicRole; adding a role policy here would take the till away from
        // reception, which is the one change this feature must not make.
        Assert.True(authorize is null || string.IsNullOrEmpty(authorize.Policy),
            $"{action} gained a method-level policy — if that is deliberate, move it into AdminOrDoctorActions "
            + "and check it is not one of the rows reception needs.");
    }

    // [I1] The class-level gate is a NAMED policy now, not a bare [Authorize]. Before this, the second group
    // above inherited « any authenticated user, any role » — so the group was not a decision at all.
    [Fact]
    public void Controller_Carries_The_AnyClinicRole_Policy_At_Class_Level() // [AC-24a][I1]
    {
        var authorize = typeof(TreatmentPlansController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.AnyClinicRole, authorize!.Policy);
    }

    // [AC-24a] A devis is patient financial data — no endpoint here may ever be anonymous. In Local mode the
    // fail-closed FallbackPolicy would already 401 an un-attributed action; this keeps Cloud honest too.
    [Fact]
    public void No_Endpoint_Is_Anonymous()
    {
        var anonymous = typeof(TreatmentPlansController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(anonymous);
    }

    // Guards the two arrays above against drift: a newly added action must be classified here, not silently
    // inherit whatever the class-level gate happens to be.
    [Fact]
    public void Every_Action_Is_Classified_By_This_Test()
    {
        var actions = typeof(TreatmentPlansController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToHashSet();

        var classified = AdminOrDoctorActions.Concat(AnyClinicRoleActions).ToHashSet();

        Assert.Empty(actions.Except(classified));
    }

    public static IEnumerable<object[]> AdminOrDoctorActionData() => AdminOrDoctorActions.Select(a => new object[] { a });
    public static IEnumerable<object[]> AnyClinicRoleActionData() => AnyClinicRoleActions.Select(a => new object[] { a });
}
