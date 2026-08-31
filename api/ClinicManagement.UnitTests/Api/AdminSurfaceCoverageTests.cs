using System.Reflection;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Guard for the admin-only surfaces (security-hardening US-7, audit § 2 finding 8).
///
/// <para>The finding was not "one endpoint was wrong" — it was that <b>three of the four</b> reference
/// catalogs were correctly <c>AdminOnly</c> and the fourth was simply missed, alongside two other clinic-wide
/// configuration endpoints. That is a class of mistake a per-endpoint fix does not prevent from recurring, so
/// this test states the <i>rule</i> instead: <b>every mutating action on a catalog controller must be
/// admin-gated</b>. A new write added to any of them fails the build until its policy is decided.</para>
///
/// <para>It follows <c>ControllerAuthorizationCoverageTests</c>, which pins the anonymous allow-list the same
/// way. Deliberately narrower than a whole-API policy matrix: a rule that is exactly right for a well-defined
/// set of controllers is more useful than a 200-entry allow-list nobody maintains.</para>
/// </summary>
public class AdminSurfaceCoverageTests
{
    /// <summary>Controllers whose write endpoints configure the clinic's shared catalogs.</summary>
    private static readonly Type[] CatalogControllers =
    {
        typeof(ProcedureTypesController),
        typeof(DentalActsController),
        typeof(MedicationsController),
    };

    /// <summary>Individual endpoints outside the catalogs that this feature gated. Pinned by name.</summary>
    public static TheoryData<Type, string> GatedActions() => new()
    {
        // Rewriting the practitioner roster — was reachable by a secretary.
        { typeof(ClinicsController), nameof(ClinicsController.UpdateDoctors) },
        // Clinic-wide recall interval — its doc comment claimed "Admin-editable" while enforcing nothing.
        { typeof(RecallController), nameof(RecallController.SetSettings) },
    };

    /// <summary>
    /// Catalog actions that use a mutating HTTP verb but <b>write nothing</b>, with the reason each is here.
    /// Deliberately a per-action list keyed by declaring type + name, not a predicate: a rule like "POST actions
    /// whose name starts with Get" would silently exempt the next real write somebody names badly, which is the
    /// failure mode this whole test exists to prevent.
    /// </summary>
    private static readonly (Type Controller, string Action)[] NonMutatingExemptions =
    {
        // AC-P6.15: the batch reimbursement estimate. It is a read — the acts of one bulletin are a list, and a
        // GET would have to encode N cotations plus N care dates into the query string. It persists nothing and
        // never appears on the BS1 PDF (AC-P6.16); gating it AdminOnly would put the estimate out of reach of the
        // secretary who fills the bulletin in.
        (typeof(DentalActsController), nameof(DentalActsController.GetReimbursementEstimates)),
    };

    private static bool IsExemptFromWriteGate(MethodInfo method) =>
        NonMutatingExemptions.Any(e => e.Controller == method.DeclaringType && e.Action == method.Name);

    private static bool IsMutating(MethodInfo method) =>
        method.GetCustomAttribute<HttpPostAttribute>() is not null
        || method.GetCustomAttribute<HttpPutAttribute>() is not null
        || method.GetCustomAttribute<HttpPatchAttribute>() is not null
        || method.GetCustomAttribute<HttpDeleteAttribute>() is not null;

    /// <summary>Public actions declared on the controller itself (not inherited helpers).</summary>
    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    /// <summary>True when the action requires the admin policy, whether set on the method or the class.</summary>
    private static bool RequiresAdmin(MethodInfo method) =>
        method.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(method.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>())
            .Any(a => a.Policy == AuthorizationPolicies.AdminOnly);

    [Fact]
    public void Every_mutating_catalog_action_is_admin_gated() // [AC-7.2] the rule, not the instance
    {
        var unguarded = CatalogControllers
            .SelectMany(Actions)
            .Where(IsMutating)
            .Where(m => !IsExemptFromWriteGate(m))
            .Where(m => !RequiresAdmin(m))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            unguarded.Count == 0,
            "These catalog write endpoints are not admin-gated. Reference-catalog writes set prices and codes "
            + "the whole clinic bills against, so each needs [Authorize(Policy = AuthorizationPolicies.AdminOnly)] "
            + "— or a deliberate decision recorded here about why it does not:\n  "
            + string.Join("\n  ", unguarded));
    }

    [Theory]
    [MemberData(nameof(GatedActions))]
    public void Clinic_wide_configuration_endpoints_are_admin_gated(Type controller, string actionName) // [AC-7.1][AC-7.3]
    {
        var action = Actions(controller).Single(m => m.Name == actionName);

        Assert.True(
            RequiresAdmin(action),
            $"{controller.Name}.{actionName} changes clinic-wide configuration and must stay admin-gated.");
    }

    [Fact]
    public void Catalog_read_endpoints_are_not_admin_gated() // reads stay open to all staff
    {
        // The other half of AC-7.2. Over-gating would be its own defect: a secretary booking an appointment
        // needs to read the procedure catalog, and locking reads would break day-to-day work.
        var overGated = CatalogControllers
            .SelectMany(Actions)
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is not null)
            .Where(RequiresAdmin)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(
            overGated.Count == 0,
            "These catalog READ endpoints are admin-gated, which blocks ordinary clinical work:\n  "
            + string.Join("\n  ", overGated));
    }

    [Fact]
    public void The_pinned_action_names_still_exist() // a rename must not silently empty this guard
    {
        foreach (var row in GatedActions())
        {
            var controller = (Type)row[0];
            var actionName = (string)row[1];

            Assert.Single(Actions(controller).Where(m => m.Name == actionName));
        }
    }

    [Fact]
    public void Every_write_gate_exemption_still_names_a_real_action() // an exemption must not outlive its action
    {
        // Same reasoning as the test above, pointed at the other list. A renamed or deleted action would leave a
        // dead exemption behind — harmless today, and a pre-approved hole the moment the name is reused.
        foreach (var (controller, actionName) in NonMutatingExemptions)
        {
            Assert.Single(Actions(controller).Where(m => m.Name == actionName));
        }
    }
}
