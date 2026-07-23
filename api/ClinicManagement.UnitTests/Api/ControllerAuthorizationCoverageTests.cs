using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Attribute-coverage guard for the Phase 4 release gate (FR-E3). In Local mode the fail-closed
/// <c>FallbackPolicy</c> authenticates every endpoint that is NOT explicitly <c>[AllowAnonymous]</c>
/// (including anonymous-by-omission ones such as the Google AJAX endpoints — they now fail closed).
/// The only remaining anonymous surface is the set of <c>[AllowAnonymous]</c> endpoints, which must
/// exactly match this approved allow-list: adding a new <c>[AllowAnonymous]</c> anywhere — the way a new
/// hole would appear — fails this test until it is reviewed and listed here.
/// </summary>
public class ControllerAuthorizationCoverageTests
{
    private static readonly HashSet<string> ExpectedAnonymous = new()
    {
        "Auth.GetMode",              // reports the auth mode so the frontend can render the right login UI
        "Auth.Login",                // bootstrap: email+password login (issues the session token)
        "Auth.Setup",                // bootstrap: first-run clinic+admin (also localhost-gated, AC-1.2a)
        "Auth.Register",             // bootstrap: clinic-code self-registration
        "Connectivity.Get",          // non-sensitive online/offline poll (Local-only; 404s in Cloud)
        "GoogleCalendar.Callback",   // OAuth browser redirect back from Google — cannot carry a bearer
    };

    private static IReadOnlyCollection<string> AnonymousEndpoints()
    {
        var assembly = typeof(ClinicManagement.API.Controllers.AuthController).Assembly;

        var result = new List<string>();
        foreach (var controller in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var controllerAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var shortName = controller.Name.Replace("Controller", string.Empty);

            foreach (var action in controller.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var isAnonymous = controllerAnonymous
                                  || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                if (isAnonymous)
                {
                    result.Add($"{shortName}.{action.Name}");
                }
            }
        }

        return result;
    }

    [Fact]
    public void No_unexpected_anonymous_endpoints_exist() // [FR-E3]
    {
        var unexpected = AnonymousEndpoints().Except(ExpectedAnonymous).OrderBy(x => x).ToList();

        Assert.True(unexpected.Count == 0,
            "Unexpected [AllowAnonymous] endpoint(s) not on the approved allow-list: "
            + string.Join(", ", unexpected)
            + ". Add [Authorize]/remove [AllowAnonymous], or add to the reviewed allow-list.");
    }

    [Fact]
    public void All_approved_anonymous_endpoints_still_exist() // guards against silent renames/removals
    {
        var missing = ExpectedAnonymous.Except(AnonymousEndpoints()).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "Approved anonymous endpoint(s) missing or renamed: " + string.Join(", ", missing));
    }
}
