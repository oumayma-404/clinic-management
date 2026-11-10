using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// `pointage-obligatoire` AC-3: the writes a locked secretary may still make, derived from the compiled controllers and
/// compared with the reviewed list in BOTH directions. Adding <c>[AllowsBeforeClockIn]</c> anywhere fails this until it
/// is reviewed here; removing one fails it too — and that is the direction that matters, since a silently un-exempted
/// « Mon profil » schéma locks her out of the one action that lets the borne lift the lock.
/// It classifies non-GET actions only: the gate never inspects a read.
/// </summary>
public class AttendanceLockExemptionCoverageTests
{
    private static readonly HashSet<string> ExpectedOpenBeforeClockIn = new()
    {
        // Signing in and out, and keeping a session alive (class-level on AuthController).
        "Auth.SignUp", "Auth.VerifySignUp", "Auth.Login", "Auth.RelayPairingCode", "Auth.RelaySession", "Auth.DemoSession",
        "Auth.Logout", "Auth.EndMySession", "Auth.Refresh", "Auth.Setup", "Auth.Register", "Auth.ChangePassword",
        "Auth.EnrolTotp", "Auth.RedeemRecoveryCode", "Auth.RequestPasswordReset", "Auth.CompletePasswordReset",
        "Auth.PreviewInvitation", "Auth.AcceptInvitation", "Auth.RegenerateRecoveryCodes", "Auth.DisableTotp", "Auth.StepUp",
        // The borne itself — the punch is how the lock lifts (class-level on PointageController).
        "Pointage.Punch",
        // The device an admin chose as a borne picks it up — a locked secretary's own PC may be that device.
        "Pointage.ClaimBorne",
        // « Mon profil »: her schéma, which the borne needs.
        "Team.SetMySchema",
        // « Congés » stays open.
        "Team.Submit",
        "Team.Cancel",
        // Her own bell.
        "Notifications.MarkRead", "Notifications.MarkAllRead", "Notifications.Dismiss", "Notifications.DismissAll",
        // Fired at sign-in and sign-out on a phone.
        "PushDevices.Register", "PushDevices.Deregister", "PushDevices.Release",
        // A device reporting whether it reaches the PC de secours.
        "RelayDevice.Report",
    };

    private static IReadOnlyCollection<string> OpenWrites(IEnumerable<Type> controllers)
    {
        var result = new List<string>();
        foreach (var controller in controllers)
        {
            var classLevel = controller.GetCustomAttribute<AllowsBeforeClockInAttribute>(inherit: true) is not null;
            var shortName = controller.Name.Replace("Controller", string.Empty);
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var open = classLevel || action.GetCustomAttribute<AllowsBeforeClockInAttribute>(inherit: true) is not null;
                if (open && IsWrite(action))
                    result.Add($"{shortName}.{action.Name}");
            }
        }

        return result;
    }

    /// <summary>An action declaring no HTTP method answers every verb, so it counts as a write.</summary>
    private static bool IsWrite(MethodInfo action)
    {
        var declared = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();
        return declared.Count == 0 || declared.Any(m => m is not ("GET" or "HEAD" or "OPTIONS"));
    }

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void No_Unreviewed_Write_Stays_Open_Before_Her_Arrival()
    {
        var unexpected = OpenWrites(ProductionControllers()).Except(ExpectedOpenBeforeClockIn).OrderBy(x => x).ToList();

        Assert.True(unexpected.Count == 0,
            "Write endpoint(s) open before the arrival but not reviewed here: " + string.Join(", ", unexpected)
            + ". Either remove [AllowsBeforeClockIn] or add the endpoint here with its reason.");
    }

    [Fact]
    public void Every_Reviewed_Open_Write_Still_Carries_The_Attribute()
    {
        var missing = ExpectedOpenBeforeClockIn.Except(OpenWrites(ProductionControllers())).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "Reviewed open write(s) no longer open (attribute removed, action renamed, or verb changed): " + string.Join(", ", missing));
    }

    [Fact]
    public void Clinic_Work_Is_Never_Open_Before_Her_Arrival()
    {
        var open = OpenWrites(ProductionControllers());

        Assert.DoesNotContain(open, a => a.StartsWith("Patients.") || a.StartsWith("Appointments.") || a.StartsWith("Invoices."));
        Assert.DoesNotContain("Team.RecordAttendance", open);
    }

    [Fact]
    public void Every_Exemption_Gives_A_Reason()
    {
        var thin = ProductionControllers()
            .SelectMany(c => c.GetCustomAttributes<AllowsBeforeClockInAttribute>(true)
                .Concat(c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<AllowsBeforeClockInAttribute>(true)))
                .Where(a => a.Reason.Trim().Length < 20)
                .Select(a => $"{c.Name}: \"{a.Reason}\""))
            .ToList();

        Assert.True(thin.Count == 0, "Exemption reason(s) too thin to review: " + string.Join(" | ", thin));
    }

    [Fact]
    public void A_Blank_Reason_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => new AllowsBeforeClockInAttribute(" "));
    }

    [Fact]
    public void The_Guard_Detects_A_Newly_Opened_Write()
    {
        Assert.Equal(new[] { "OpenWriteProbe.Write" }, OpenWrites(new[] { typeof(OpenWriteProbeController) }));
        Assert.Empty(OpenWrites(new[] { typeof(GuardedWriteProbeController) }));
    }

    [Fact]
    public void The_Guard_Is_Deliberately_Blind_To_An_Exempted_Read()
    {
        Assert.Empty(OpenWrites(new[] { typeof(OpenReadProbeController) }));
    }

    private class OpenWriteProbeController : ControllerBase
    {
        [HttpPost]
        [AllowsBeforeClockIn("A probe reason long enough to be a sentence.")]
        public IActionResult Write() => Ok();
    }

    private class GuardedWriteProbeController : ControllerBase
    {
        [HttpPost]
        public IActionResult Write() => Ok();
    }

    private class OpenReadProbeController : ControllerBase
    {
        [HttpGet]
        [AllowsBeforeClockIn("A probe reason long enough to be a sentence.")]
        public IActionResult Read() => Ok();
    }
}
