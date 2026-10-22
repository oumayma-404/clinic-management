using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The doors a fenced cloud keeps open while a cabinet's PC de secours may hold its saves (<c>clinic-pc-copy</c> FR-11,
/// D15), derived from the compiled controllers and asserted equal to the reviewed set in both directions —
/// <see cref="RelayStandbyExemptionCoverageTests"/>' shape. Exemptions are WHOLE controllers by design: everything an
/// administrator does to an account, everything about the PC de secours itself, the PC's own channel, and signing in.
/// A cabinet's clinical or money write that slipped in would be a row the two copies then disagree about.
/// </summary>
public class RelayFenceExemptionCoverageTests
{
    private static readonly HashSet<string> ExpectedExemptControllers = new()
    {
        "Auth",       // signing in, and a person's own credentials (FR-11)
        "Users",      // an administrator's account changes (FR-11)
        "Relay",      // managing the PC de secours — retiring it is how an admin frees a cabinet whose PC fell silent
        "RelayPeer",  // the PC's own channel: how a silent PC comes back, confirms its acks and stands down
        "RelayDevice", // the cabinet's devices saying they reach the cloud but not a silent PC — how its lock ends (AC-6.2)
    };

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IReadOnlyCollection<string> ExemptControllers(IEnumerable<Type> controllers) =>
        controllers
            .Where(c => c.GetCustomAttribute<AllowedWhileCloudFencedAttribute>(inherit: true) is not null)
            .Select(c => c.Name.Replace("Controller", string.Empty))
            .ToList();

    private static IReadOnlyCollection<string> ExemptActions(IEnumerable<Type> controllers) =>
        controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowedWhileCloudFencedAttribute>(inherit: true) is not null)
                .Select(m => $"{c.Name.Replace("Controller", string.Empty)}.{m.Name}"))
            .ToList();

    [Fact]
    public void The_Exempt_Controllers_Are_Exactly_The_Reviewed_Ones()
    {
        var actual = ExemptControllers(ProductionControllers()).OrderBy(x => x).ToList();

        Assert.Equal(ExpectedExemptControllers.OrderBy(x => x), actual);
    }

    // One-off exemptions would make the set impossible to read at a glance: a door that must stay open belongs to a
    // controller whose whole job is FR-11's.
    [Fact]
    public void No_Single_Action_Is_Exempted_On_Its_Own()
    {
        Assert.Empty(ExemptActions(ProductionControllers()));
    }

    [Fact]
    public void Every_Exemption_Gives_A_Reason()
    {
        var thin = ProductionControllers()
            .Select(c => c.GetCustomAttribute<AllowedWhileCloudFencedAttribute>(inherit: true))
            .Where(a => a is not null && a.Reason.Trim().Length < 20)
            .Select(a => a!.Reason)
            .ToList();

        Assert.True(thin.Count == 0, "Exemption reason(s) too thin to review: " + string.Join(" | ", thin));
    }

    [Fact]
    public void The_Guard_Detects_A_Newly_Exempted_Controller_And_Action()
    {
        Assert.Equal(new[] { "ExemptProbe" }, ExemptControllers(new[] { typeof(ExemptProbeController) }));
        Assert.Equal(new[] { "ActionProbe.Write" }, ExemptActions(new[] { typeof(ActionProbeController) }));
    }

    [AllowedWhileCloudFenced("A probe reason long enough to be a sentence.")]
    private class ExemptProbeController : ControllerBase
    {
    }

    private class ActionProbeController : ControllerBase
    {
        [HttpPost]
        [AllowedWhileCloudFenced("A probe reason long enough to be a sentence.")]
        public IActionResult Write() => Ok();
    }
}
