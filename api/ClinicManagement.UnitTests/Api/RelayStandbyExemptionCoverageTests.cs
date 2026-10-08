using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The writes a standby PC de secours still accepts (<c>clinic-pc-copy</c> Part 1), derived from the compiled controllers
/// and asserted equal to the reviewed set in both directions — <see cref="SubscriptionExemptionCoverageTests"/>' shape.
/// A write added here without review is one the cloud's next change overwrites, or that the cloud never sees.
/// </summary>
public class RelayStandbyExemptionCoverageTests
{
    /// <summary>The sign-in doors, and nothing that writes a row the cloud copies.</summary>
    private static readonly HashSet<string> ExpectedAllowedWrites = new()
    {
        "Auth.Login",
        "Auth.Refresh",
        "Auth.Logout",
        "Auth.EndMySession",
        "Auth.StepUp",
        // AC-8.2: on a RETIRED PC, the admin erases this PC's copy — it removes the copy, it writes nothing the cloud owns.
        "RelayLocal.Erase",
    };

    private static IReadOnlyCollection<string> AllowedWrites(IEnumerable<Type> controllers)
    {
        var result = new List<string>();

        foreach (var controller in controllers)
        {
            var atClassLevel = controller.GetCustomAttribute<AllowedOnStandbyRelayAttribute>(inherit: true) is not null;
            var shortName = controller.Name.Replace("Controller", string.Empty);

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var allowed = atClassLevel
                              || action.GetCustomAttribute<AllowedOnStandbyRelayAttribute>(inherit: true) is not null;
                if (allowed && IsWrite(action))
                {
                    result.Add($"{shortName}.{action.Name}");
                }
            }
        }

        return result;
    }

    /// <summary>⚠️ An action declaring no HTTP method answers every verb, so it counts as a write.</summary>
    private static bool IsWrite(MethodInfo action)
    {
        var declared = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();
        return declared.Count == 0
               || declared.Any(m => !string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(m, "HEAD", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(m, "OPTIONS", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void No_Unreviewed_Write_Is_Allowed_On_A_Standby_Relay()
    {
        var unexpected = AllowedWrites(ProductionControllers()).Except(ExpectedAllowedWrites).OrderBy(x => x).ToList();

        Assert.True(unexpected.Count == 0,
            "Write endpoint(s) allowed on a standby PC de secours but not reviewed: " + string.Join(", ", unexpected));
    }

    // A sign-in door that silently loses the attribute locks everybody out of the copy.
    [Fact]
    public void Every_Reviewed_Write_Still_Carries_The_Attribute()
    {
        var missing = ExpectedAllowedWrites.Except(AllowedWrites(ProductionControllers())).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "Reviewed standby write(s) no longer allowed (attribute removed, action renamed, or verb changed): "
            + string.Join(", ", missing));
    }

    // Recording on a copy is the case the gate exists for: the change password, TOTP enrolment and recovery code doors
    // write rows the cloud owns, so they are refused on the PC and done on the cloud.
    [Theory]
    [InlineData("Auth.ChangePassword")]
    [InlineData("Auth.EnrolTotp")]
    [InlineData("Auth.RedeemRecoveryCode")]
    [InlineData("Auth.Setup")]
    public void A_Write_The_Cloud_Owns_Is_Not_Allowed(string endpoint)
    {
        Assert.DoesNotContain(endpoint, AllowedWrites(ProductionControllers()));
    }

    [Fact]
    public void Every_Allowance_Gives_A_Reason()
    {
        var thin = ProductionControllers()
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<AllowedOnStandbyRelayAttribute>(true))
            .Where(a => a.Reason.Trim().Length < 20)
            .Select(a => a.Reason)
            .ToList();

        Assert.True(thin.Count == 0, "Allowance reason(s) too thin to review: " + string.Join(" | ", thin));
    }

    [Fact]
    public void The_Guard_Detects_A_Newly_Allowed_Write()
    {
        Assert.Equal(new[] { "AllowedWriteProbe.Write" }, AllowedWrites(new[] { typeof(AllowedWriteProbeController) }));
        Assert.Empty(AllowedWrites(new[] { typeof(GuardedWriteProbeController) }));
    }

    private class AllowedWriteProbeController : ControllerBase
    {
        [HttpPost]
        [AllowedOnStandbyRelay("A probe reason long enough to be a sentence.")]
        public IActionResult Write() => Ok();
    }

    private class GuardedWriteProbeController : ControllerBase
    {
        [HttpPost]
        public IActionResult Write() => Ok();
    }
}
