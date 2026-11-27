using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Authorization.Requirements;
using ClinicManagement.Application.Features.Users;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// « Gestionnaire » (<c>manager</c>, owner's call 2026-11-27): everything an admin has except the dashboard, the
/// three money screens (Factures, Caisse, Chèques), the journal d'activité, « mode discret », « Premiers pas » —
/// and never an admin's account. <see cref="Endpoints_A_Manager_Does_Not_Reach"/> is where that list is stated;
/// it is derived from the compiled attributes, so an endpoint added later under either closed policy fails here
/// until somebody decides whether the gestionnaire gets it.
/// </summary>
public class ManagerRoleAccessTests
{
    /// <summary>The owner's list, as endpoints. Everything else the API serves to a clinic role, he reaches.</summary>
    private static readonly string[] ClosedToManager =
    {
        "AuditController.GetAuditEntries",
        "BillingController.ExportCaisseLedger",
        "BillingController.ExportChequesDue",
        "BillingController.GetCaisseLedger",
        "BillingController.GetCaisseSummary",
        "BillingController.GetChequesDue",
        "ClinicsController.HideMoney",
        "ClinicsController.ShowMoney",
        "DashboardController.GetAppointmentsByStatus",
        "DashboardController.GetDashboard",
        "DashboardController.GetGettingStarted",
        "DashboardController.GetPreferences",
        "DashboardController.TickStep",
        "DashboardController.UntickStep",
        "DashboardController.UpdatePreferences",
        "InvoicesController.ExportInvoices",
        "InvoicesController.GetRevenue",
        "InvoicesController.SetPaymentBanked",
        "TreatmentPlansController.SetInstallmentPaymentBanked",
    };

    private static AuthorizationOptions Options()
    {
        var options = new AuthorizationOptions();
        AuthorizationPolicies.ConfigurePolicies(options, isLocalMode: true);
        return options;
    }

    private static IReadOnlyList<string>? RolesOf(AuthorizationOptions options, string? policy) =>
        policy is null ? null : options.GetPolicy(policy)?.Requirements.OfType<RoleRequirement>().SingleOrDefault()?.AllowedRoles;

    [Theory]
    [InlineData(AuthorizationPolicies.AnyClinicRole, new[] { "admin", "doctor", "secretary", "manager" })]
    [InlineData(AuthorizationPolicies.AdminDoctorOrManager, new[] { "admin", "doctor", "manager" })]
    [InlineData(AuthorizationPolicies.AdminOrManager, new[] { "admin", "manager" })]
    [InlineData(AuthorizationPolicies.AdminOrDoctor, new[] { "admin", "doctor" })]
    [InlineData(AuthorizationPolicies.AdminOnly, new[] { "admin" })]
    public void Each_Role_Policy_Admits_Exactly_Its_Roles(string policy, string[] roles)
    {
        var allowed = RolesOf(Options(), policy);

        Assert.NotNull(allowed);
        Assert.Equal(roles.OrderBy(r => r), allowed!.OrderBy(r => r));
    }

    [Fact]
    public void Endpoints_A_Manager_Does_Not_Reach()
    {
        var options = Options();
        var closed = new List<string>();
        foreach (var controller in typeof(global::ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var classPolicies = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Policy);
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null) continue;
                // Every attribute applies (ASP.NET combines them), so one role policy without « manager » closes it.
                var roleSets = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Policy)
                    .Concat(classPolicies)
                    .Select(p => RolesOf(options, p))
                    .Where(r => r is not null)
                    .ToList();
                if (roleSets.Count > 0 && roleSets.Any(r => !r!.Contains(User.RoleManager)))
                    closed.Add($"{controller.Name}.{action.Name}");
            }
        }

        Assert.Equal(ClosedToManager.OrderBy(n => n, StringComparer.Ordinal),
            closed.Distinct().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(User.RoleAdmin, User.RoleAdmin, true)]
    [InlineData(User.RoleAdmin, User.RoleManager, true)]
    [InlineData(User.RoleManager, User.RoleAdmin, false)]
    [InlineData(User.RoleManager, User.RoleManager, true)]
    [InlineData(User.RoleManager, User.RoleDoctor, true)]
    [InlineData(User.RoleManager, User.RoleSecretary, true)]
    public void A_Manager_Never_Acts_On_An_Admin_Account(string callerRole, string targetRole, bool allowed)
    {
        var clinic = Guid.NewGuid();
        var caller = User.CreateLocalUser(clinic, callerRole, "caller@clinic.com", "HASH", "Caller");
        var target = User.CreateLocalUser(clinic, targetRole, "target@clinic.com", "HASH", "Target");

        Assert.Equal(allowed, StaffAccountAuthority.MayManage(caller, target));
    }

    [Theory]
    [InlineData(User.RoleAdmin, User.RoleAdmin, true)]
    [InlineData(User.RoleManager, User.RoleAdmin, false)]
    [InlineData(User.RoleManager, User.RoleManager, true)]
    [InlineData(User.RoleManager, User.RoleDoctor, true)]
    [InlineData(User.RoleManager, User.RoleSecretary, true)]
    public void A_Manager_Never_Hands_Out_The_Admin_Role(string callerRole, string role, bool allowed)
    {
        var caller = User.CreateLocalUser(Guid.NewGuid(), callerRole, "caller@clinic.com", "HASH", "Caller");

        Assert.Equal(allowed, StaffAccountAuthority.MayGrant(caller, role));
    }

    [Theory]
    [InlineData(User.RoleAdmin, true)]
    [InlineData(User.RoleManager, true)]
    [InlineData(User.RoleDoctor, false)]
    [InlineData(User.RoleSecretary, false)]
    public void RunsTheClinic_Is_Admin_Or_Manager(string role, bool expected)
    {
        var user = User.CreateLocalUser(Guid.NewGuid(), role, "u@clinic.com", "HASH", "U");

        Assert.Equal(expected, user.RunsTheClinic());
        Assert.Equal(User.RoleManager, User.NormalizeRole("Manager"));
    }
}
