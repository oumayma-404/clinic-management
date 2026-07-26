using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-24a] Pins the treatment-plan endpoints' authorization surface. The repo gates exactly one class of
/// operation with <c>AdminOrDoctor</c>: reversing or altering an already-issued financial document. Cancelling
/// a numbered devis belongs to it — and nothing pinned that until now, so a refactor could have silently
/// dropped the policy and let a secretary void a devis.
/// <para>
/// Slice B adds <c>AmendPlan</c> and <c>ReviseInstallments</c> (both <c>AdminOrDoctor</c>) and
/// <c>ReorderItems</c> (no method-level policy — reordering is cosmetic). Add them to the arrays below in the
/// same pass that adds the endpoints.
/// </para>
/// </summary>
public class TreatmentPlansControllerAuthorizationTests
{
    /// <summary>Alters a numbered financial document → same class as invoice cancel / avoir.</summary>
    private static readonly string[] AdminOrDoctorActions =
    {
        nameof(TreatmentPlansController.CancelPlan),
    };

    /// <summary>Everyday clinical/billing work — any authenticated clinic member, via the class-level gate.</summary>
    private static readonly string[] AnyAuthenticatedActions =
    {
        nameof(TreatmentPlansController.GetPlans),
        nameof(TreatmentPlansController.GetPlan),
        nameof(TreatmentPlansController.CreatePlan),
        nameof(TreatmentPlansController.UpdatePlan),
        nameof(TreatmentPlansController.AcceptPlan),
        nameof(TreatmentPlansController.CompletePlan),
        nameof(TreatmentPlansController.RecordInstallmentPayment),
        nameof(TreatmentPlansController.MarkItemDone),
        nameof(TreatmentPlansController.DeletePlan),
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
    [MemberData(nameof(AnyAuthenticatedActionData))]
    public void Everyday_Endpoints_Carry_No_Method_Level_Policy(string action) // [AC-24a]
    {
        var method = typeof(TreatmentPlansController).GetMethod(action)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        // They inherit the class-level [Authorize]; adding a role policy here would be a behaviour change.
        Assert.True(authorize is null || string.IsNullOrEmpty(authorize.Policy));
    }

    [Fact]
    public void Controller_Requires_Authentication_At_Class_Level() // [AC-24a]
    {
        var authorize = typeof(TreatmentPlansController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
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

        var classified = AdminOrDoctorActions.Concat(AnyAuthenticatedActions).ToHashSet();

        Assert.Empty(actions.Except(classified));
    }

    public static IEnumerable<object[]> AdminOrDoctorActionData() => AdminOrDoctorActions.Select(a => new object[] { a });
    public static IEnumerable<object[]> AnyAuthenticatedActionData() => AnyAuthenticatedActions.Select(a => new object[] { a });
}
