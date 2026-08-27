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
