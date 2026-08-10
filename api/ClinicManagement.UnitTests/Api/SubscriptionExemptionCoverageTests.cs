using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// FR-3's exempt set, derived from the compiled controllers rather than listed twice. Adding
/// <c>[AllowsWithoutSubscription]</c> to a write anywhere fails this test until it is reviewed and named below, and
/// removing one from an approved write fails it too — the second direction being the one that matters, since a
/// silently un-exempted <c>change-password</c> locks an expired cabinet out of its own account.
///
/// <para><b>⚠️ It classifies NON-GET actions only, and that is a stated limitation rather than an oversight.</b> The
/// gate never inspects a read, so a GET is exempt whether or not it says so and this test <i>cannot</i> go red when
/// the attribute is removed from one. Several rows of FR-3's table are GET-only for exactly that reason
/// (<c>MetaController</c>'s two, and every action of the coming <c>SubscriptionController</c>): they carry the
/// attribute as documentation, so a reader does not have to re-derive « is a GET refused? » to know the set. A green
/// suite is therefore <b>not</b> evidence that those rows are load-bearing. <c>/health</c> is not even a controller
/// action and sits outside <c>/api</c>, so nothing can be applied to it at all.</para>
///
/// <para>Its red-proof is executable (<see cref="The_Guard_Detects_A_Newly_Exempted_Write"/>) rather than a note
/// asking a reviewer to delete an attribute by hand, and it pins the GET blindness above in the same way.</para>
/// </summary>
public class SubscriptionExemptionCoverageTests
{
    /// <summary>
    /// Every <b>write</b> that keeps working on a cabinet that may no longer record new work (FR-3). One line per
    /// endpoint with the reason it is here — the attribute in the source carries the same reason, at the place
    /// somebody reading the endpoint will find it.
    /// </summary>
    private static readonly HashSet<string> ExpectedExemptWrites = new()
    {
        // --- Signing in, and getting unblocked (AC-4.7, EC-2). Class-level on AuthController: the reason is one
        // reason for all seven. Six are [AllowAnonymous] and so arrive with an Unset tenant scope, which passes the
        // gate anyway; ChangePassword is the one that is authenticated, clinic-scoped and non-GET — i.e. the only
        // row of this group that genuinely needs the attribute, and the one EC-2 is about.
        "Auth.SignUp",
        "Auth.VerifySignUp",
        "Auth.Login",
        "Auth.Refresh",
        "Auth.Setup",
        "Auth.Register",
        "Auth.ChangePassword",

        // --- Compute-only POSTs (AC-4.9): a POST for a read, each persisting nothing.
        "CnamNomenclature.GetReimbursementEstimates",   // an estimate per act row; a GET could not carry the list
        "Patients.PreviewPatientImport",                 // the dry run — a Query by design. The commit is refused.
        "MedicalDocuments.GeneratePdfForDownload",       // renders a document the cabinet already holds (AC-4.3)

        // --- Experienced as reading, but issuing a write to do it (AC-4.11).
        "Notifications.MarkRead",                        // else AC-3.4's own expiry notice can never be dismissed
        "Notifications.MarkAllRead",
        "PushDevices.Register",                          // fired at every mobile sign-in (AC-4.7)
        "PushDevices.Deregister",
        "PatientFiles.InitializeDefaultFolders",         // fired on the first visit to the Files tab; a READ fails
        "Dashboard.UpdatePreferences",                   // personal interface state, not clinic work

        // --- Getting your data out, and getting a colleague out (FR-3).
        "Backup.BackupNow",                              // the AC-4.2 argument; the scheduled one already keeps going
        "Users.SetStatus",                               // offboarding must not wait on an invoice
    };

    /// <summary>
    /// The exempt <b>writes</b> among <paramref name="controllers"/>, keyed <c>Controller.Action</c>. A parameter
    /// rather than a hard-wired assembly so the red-proofs below can feed it probe types.
    /// </summary>
    private static IReadOnlyCollection<string> ExemptWrites(IEnumerable<Type> controllers)
    {
        var result = new List<string>();

        foreach (var controller in controllers)
        {
            var exemptAtClassLevel =
                controller.GetCustomAttribute<AllowsWithoutSubscriptionAttribute>(inherit: true) is not null;
            var shortName = controller.Name.Replace("Controller", string.Empty);

            foreach (var action in controller.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var exempt = exemptAtClassLevel
                             || action.GetCustomAttribute<AllowsWithoutSubscriptionAttribute>(inherit: true)
                                 is not null;

                if (exempt && IsWrite(action))
                {
                    result.Add($"{shortName}.{action.Name}");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Anything the gate would inspect. ⚠️ An action declaring <b>no</b> HTTP method counts as a write: it answers
    /// every verb, so reading it as a GET would exempt a POST route nobody listed.
    /// </summary>
    private static bool IsWrite(MethodInfo action)
    {
        var declared = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
            .SelectMany(a => a.HttpMethods)
            .ToList();

        return declared.Count == 0
               || declared.Any(m => !string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(m, "HEAD", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(m, "OPTIONS", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    // ---- the two directions -----------------------------------------------------------------------------

    // [FR-3] Nothing exempts itself without review. This is the direction a hole appears in.
    [Fact]
    public void No_Unreviewed_Write_Is_Exempt_From_The_Subscription_Gate()
    {
        var unexpected = ExemptWrites(ProductionControllers())
            .Except(ExpectedExemptWrites).OrderBy(x => x).ToList();

        Assert.True(unexpected.Count == 0,
            "Write endpoint(s) exempted from the subscription gate but not on FR-3's reviewed set: "
            + string.Join(", ", unexpected)
            + ". Either remove [AllowsWithoutSubscription] or add the endpoint here with its reason.");
    }

    // [AC-4.7][EC-2] And nothing silently stops being exempt. A renamed or un-attributed change-password locks an
    // expired cabinet out of the one action that unblocks it — which looks like a login bug, not a gate bug.
    [Fact]
    public void Every_Reviewed_Exempt_Write_Still_Carries_The_Attribute()
    {
        var missing = ExpectedExemptWrites
            .Except(ExemptWrites(ProductionControllers())).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "Approved exempt write(s) no longer exempt (attribute removed, action renamed, or verb changed): "
            + string.Join(", ", missing));
    }

    // ---- the two named facts ----------------------------------------------------------------------------

    // [AC-4.9][FR-3] The AI assistant is deliberately NOT exempt: its action set books and cancels appointments, so
    // exempting « the chat » would hand an expired cabinet a second, unguarded door onto the agenda.
    [Fact]
    public void The_AI_Chat_Is_Not_Exempt()
    {
        Assert.DoesNotContain("AI.Chat", ExemptWrites(ProductionControllers()));
    }

    // [AC-4.9] The three compute-only POSTs are exempt — the ones that would otherwise read as writes because of
    // their verb alone. Stated separately from the set above so the AC has a case of its own to point at.
    [Theory]
    [InlineData("CnamNomenclature.GetReimbursementEstimates")]
    [InlineData("Patients.PreviewPatientImport")]
    [InlineData("MedicalDocuments.GeneratePdfForDownload")]
    public void The_Compute_Only_Posts_Are_Exempt(string endpoint)
    {
        Assert.Contains(endpoint, ExemptWrites(ProductionControllers()));
    }

    // [FR-3] The CSV import COMMIT is refused while its preview is not — the pair that proves the exemption is about
    // « writes nothing », not about « is on the import screen ».
    [Fact]
    public void The_Import_Commit_Is_Refused_While_Its_Dry_Run_Is_Not()
    {
        var exempt = ExemptWrites(ProductionControllers());

        Assert.Contains("Patients.PreviewPatientImport", exempt);
        Assert.DoesNotContain("Patients.ImportPatients", exempt);
    }

    // Every exemption states why. The attribute's constructor refuses a blank reason, so this asserts the reasons
    // are real sentences rather than placeholders — the thing a copy-pasted exemption would carry.
    [Fact]
    public void Every_Exemption_Gives_A_Reason()
    {
        var thin = new List<string>();

        foreach (var controller in ProductionControllers())
        {
            foreach (var attribute in controller
                         .GetCustomAttributes<AllowsWithoutSubscriptionAttribute>(inherit: true)
                         .Concat(controller
                             .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .SelectMany(m => m.GetCustomAttributes<AllowsWithoutSubscriptionAttribute>(true))))
            {
                if (attribute.Reason.Trim().Length < 20)
                {
                    thin.Add($"{controller.Name}: \"{attribute.Reason}\"");
                }
            }
        }

        Assert.True(thin.Count == 0, "Exemption reason(s) too thin to review: " + string.Join(" | ", thin));
    }

    // ---- red-proofs -------------------------------------------------------------------------------------

    // The guard genuinely sees a newly-exempted write, rather than passing because it finds nothing.
    [Fact]
    public void The_Guard_Detects_A_Newly_Exempted_Write()
    {
        Assert.Equal(new[] { "ExemptWriteProbe.Write" }, ExemptWrites(new[] { typeof(ExemptWriteProbeController) }));
        Assert.Empty(ExemptWrites(new[] { typeof(GuardedWriteProbeController) }));
    }

    // And it is blind to a GET, on purpose — the limitation this class's summary states, made executable so nobody
    // reads a green run as covering the GET-only rows of FR-3's table.
    [Fact]
    public void The_Guard_Is_Deliberately_Blind_To_An_Exempted_Read()
    {
        Assert.Empty(ExemptWrites(new[] { typeof(ExemptReadProbeController) }));
    }

    private class ExemptWriteProbeController : ControllerBase
    {
        [HttpPost]
        [AllowsWithoutSubscription("A probe reason long enough to be a sentence.")]
        public IActionResult Write() => Ok();
    }

    private class GuardedWriteProbeController : ControllerBase
    {
        [HttpPost]
        public IActionResult Write() => Ok();
    }

    private class ExemptReadProbeController : ControllerBase
    {
        [HttpGet]
        [AllowsWithoutSubscription("A probe reason long enough to be a sentence.")]
        public IActionResult Read() => Ok();
    }
}
