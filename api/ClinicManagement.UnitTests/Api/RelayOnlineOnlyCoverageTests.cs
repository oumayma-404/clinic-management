using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// FR-5's « online only » list (<c>clinic-pc-copy</c>), derived as the other face of FR-11: whatever the cloud keeps
/// writing during a cut, a PC de secours in charge must refuse — or the return would bring back two versions of one
/// account, one password, one authenticator. Every write of a controller the fenced cloud keeps open is therefore
/// <see cref="OnlineOnlyAttribute"/>, <see cref="AllowedOnStandbyRelayAttribute"/> (each side's own session state), or a
/// reviewed sign-in door below; a new action on one of those controllers fails here until somebody decides.
/// </summary>
public class RelayOnlineOnlyCoverageTests
{
    /// <summary>The PC's own channel: on the PC it serves nothing, and it is how a PC in charge tells the cloud so.</summary>
    private static readonly HashSet<string> ChannelControllers = new() { "RelayPeer" };

    /// <summary>
    /// Writes that stay open on a PC in charge although the cloud keeps them too — each a way to sign in, whose trace is
    /// merged at the return (FR-11: « a recovery code used on the PC cannot be used again »).
    /// </summary>
    private static readonly HashSet<string> SignInDoorsDuringACut = new()
    {
        "Auth.RedeemRecoveryCode", // a person whose phone is gone still signs in during the cut
    };

    /// <summary>FR-5's own list, named by the spec — held so a rename cannot quietly drop one.</summary>
    private static readonly string[] SpecNamed =
    {
        "Users.CreateUser", "Users.SetStatus", "Users.ResetPassword", "Users.ResetTotp", "Users.SetRole",
        "Auth.ChangePassword", "Auth.EnrolTotp", "Auth.DisableTotp", "Auth.RegenerateRecoveryCodes",
        "Clinics.UpdateReminderSettings", "Clinics.ConnectWhatsApp", "Clinics.DisconnectWhatsApp",
        "GoogleCalendar.Connect", "GoogleCalendar.Callback", "GoogleCalendar.SyncAppointmentToGoogle",
        "GoogleCalendar.Disconnect", "GoogleCalendar.RevertImport",
        "Backup.DownloadArchive", "Backup.RestoreArchive", "Backup.RestoreFromRecoveryPoint",
    };

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static string Name(Type controller) => controller.Name.Replace("Controller", string.Empty);

    private static IEnumerable<(string Key, MethodInfo Action)> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
            .Select(m => ($"{Name(controller)}.{m.Name}", m));

    /// <summary>An action answering anything but a read; one declaring no method answers every verb, so it counts.</summary>
    private static bool IsWrite(MethodInfo action)
    {
        var methods = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();
        return methods.Count == 0 || methods.Any(m => m is not ("GET" or "HEAD" or "OPTIONS"));
    }

    private static bool Has<T>(MethodInfo action) where T : Attribute =>
        action.GetCustomAttribute<T>(inherit: true) is not null;

    /// <summary>The writes the fenced cloud keeps open and a PC in charge would accept — each one a second writer.</summary>
    private static IReadOnlyList<string> Unpaired(IEnumerable<Type> controllers, out int candidates)
    {
        var writes = controllers
            .Where(c => c.GetCustomAttribute<AllowedWhileCloudFencedAttribute>(inherit: true) is not null
                        && !ChannelControllers.Contains(Name(c)))
            .SelectMany(Actions)
            .Where(a => IsWrite(a.Action))
            .ToList();
        candidates = writes.Count;
        return writes
            .Where(a => !Has<OnlineOnlyAttribute>(a.Action) && !Has<AllowedOnStandbyRelayAttribute>(a.Action)
                        && !SignInDoorsDuringACut.Contains(a.Key))
            .Select(a => a.Key)
            .ToList();
    }

    [Fact]
    public void Every_Write_The_Fenced_Cloud_Keeps_Open_Is_Refused_On_A_Pc_In_Charge()
    {
        var unpaired = Unpaired(ProductionControllers(), out var candidates);

        // A reflection guard fails open: one that found nothing would pass for ever.
        Assert.True(candidates >= 20, $"Only {candidates} candidate writes found — the scan no longer reaches the controllers.");
        Assert.True(unpaired.Count == 0,
            "Written by the cloud during a cut AND accepted by the PC in charge — mark [OnlineOnly] or review: "
            + string.Join(", ", unpaired));
    }

    // Both directions: a reviewed door that is now marked anyway, or gone, is an exception nobody needs any more.
    [Fact]
    public void Every_Reviewed_Sign_In_Door_Still_Exists_And_Is_Still_Open()
    {
        var actions = ProductionControllers().SelectMany(Actions).ToDictionary(a => a.Key, a => a.Action);

        foreach (var door in SignInDoorsDuringACut)
        {
            Assert.True(actions.TryGetValue(door, out var action), $"{door} no longer exists.");
            Assert.False(Has<OnlineOnlyAttribute>(action!) || Has<AllowedOnStandbyRelayAttribute>(action!),
                $"{door} is now marked; drop it from the reviewed list.");
        }
    }

    [Fact]
    public void The_Specs_Online_Only_List_Is_Marked()
    {
        var actions = ProductionControllers().SelectMany(Actions).ToDictionary(a => a.Key, a => a.Action);

        var missing = SpecNamed.Where(k => !actions.TryGetValue(k, out var a) || !Has<OnlineOnlyAttribute>(a)).ToList();

        Assert.True(missing.Count == 0, "FR-5 names these, and they are not [OnlineOnly]: " + string.Join(", ", missing));
    }

    // The two attributes say opposite things about the PC; one action cannot carry both.
    [Fact]
    public void No_Action_Is_Both_Online_Only_And_Open_On_Standby()
    {
        var both = ProductionControllers().SelectMany(Actions)
            .Where(a => Has<OnlineOnlyAttribute>(a.Action) && Has<AllowedOnStandbyRelayAttribute>(a.Action))
            .Select(a => a.Key)
            .ToList();

        Assert.Empty(both);
    }

    [Fact]
    public void Every_Online_Only_Action_Gives_A_Reason()
    {
        var thin = ProductionControllers().SelectMany(Actions)
            .Select(a => (a.Key, Attribute: a.Action.GetCustomAttribute<OnlineOnlyAttribute>(inherit: true)))
            .Where(a => a.Attribute is not null && a.Attribute.Reason.Trim().Length < 20)
            .Select(a => a.Key)
            .ToList();

        Assert.Empty(thin);
    }

    [Fact]
    public void The_Guard_Reports_An_Unmarked_Write_On_A_Controller_The_Cloud_Keeps_Open()
    {
        var unpaired = Unpaired(new[] { typeof(FencedProbeController) }, out var candidates);

        Assert.Equal(3, candidates);
        Assert.Equal(new[] { "FencedProbe.Unmarked" }, unpaired);
    }

    [AllowedWhileCloudFenced("A probe reason long enough to be a sentence.")]
    private class FencedProbeController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();

        [HttpPost("unmarked")]
        public IActionResult Unmarked() => Ok();

        [HttpPost("marked")]
        [OnlineOnly("A probe reason long enough to be a sentence.")]
        public IActionResult Marked() => Ok();

        [HttpPost("session")]
        [AllowedOnStandbyRelay("A probe reason long enough to be a sentence.")]
        public IActionResult Session() => Ok();
    }
}
