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
        // Taking the cabinet's own data out is not recording new work, and the unattended path is the one with
        // nobody present to be told why it stopped (AC-8 / AC-4.2). The token it mints is scoped to the archive
        // download alone, so the exemption cannot be spent on anything else.
        "Backup.ExchangeArchiveGrant",
        // The unattended shell stamping « le coffre est copié ». Reporting that the practice holds its own copy
        // of its own files is not recording new work, and a lapsed cabinet must not be the one that stops being
        // told its coffre is unprotected. It is grant-gated independently of the subscription.
        "Backup.ReportVaultCopy",
        // Signing out is not recording clinic work, and it must keep working on an expired cabinet — a practice
        // that cannot sign out of a shared reception PC is a worse outcome than one that cannot bill.
        "Auth.Logout",
        // The same argument one step further out. « Mes appareils » is where a lost laptop's 30-day session is
        // revoked, and a security control has to work on the day the cabinet's cover lapses — that is precisely
        // when a practice is distracted and least likely to notice it cannot. It records no clinical work and is
        // scoped to the caller's own account, so an expired cabinet gains nothing by reaching it.
        "Auth.EndMySession",
        "Auth.Refresh",
        "Auth.Setup",
        "Auth.Register",
        "Auth.ChangePassword",
        // hosted-security-hardening FR-1.10. The second factor's own two anonymous doors, and they belong to the
        // group above for the same reason: neither records clinical work, and an expired cabinet keeps every
        // read, export and PDF (AC-4.1/4.2) — which it cannot reach at all if it cannot sign in. Enrolment
        // matters most here: an administrator refused `totp_enrolment_required` has NO other way in, so gating
        // it on the entitlement would turn a lapsed subscription into a total lockout from records the cabinet
        // is still entitled to read.
        "Auth.EnrolTotp",
        "Auth.RedeemRecoveryCode",
        // The self-service password reset's own two anonymous doors, in the group above for its exact reason:
        // neither records clinical work, and an expired cabinet keeps every read, export and PDF (AC-4.1/4.2) —
        // which it reaches through nobody who cannot sign in. It bites hardest on precisely the cabinet this gate
        // is about: the one whose cover lapsed *because* the person who pays forgot their password. Gating the way
        // back on the entitlement would make that lockout self-sustaining.
        "Auth.RequestPasswordReset",
        "Auth.CompletePasswordReset",
        // FR-1.10, and the same reason as the group above: an expired cabinet keeps every read, export and PDF
        // by right (AC-4.1/4.2), and it reaches none of them if its people cannot get in. « Sécurité » is where
        // a lost authenticator is re-secured and where a step-up is proved; refusing those on the entitlement
        // would turn a lapsed subscription into a lockout from records the cabinet is still entitled to read.
        "Auth.RegenerateRecoveryCodes",
        "Auth.DisableTotp",
        "Auth.StepUp",

        // --- Compute-only POSTs (AC-4.9): a POST for a read, each persisting nothing.
        "DentalActs.GetReimbursementEstimates",   // an estimate per act row; a GET could not carry the list
        "Patients.PreviewPatientImport",                 // the dry run — a Query by design. The commit is refused.
        // ⚠️ Takes the document in the BODY and persists nothing — it loads no stored document and checks no
        // ownership, so an expired cabinet can render a brand-new one. AC-4.9 exempts it in those terms anyway.
        "MedicalDocuments.GeneratePdfForDownload",

        // --- Experienced as reading, but issuing a write to do it (AC-4.11).
        "Notifications.MarkRead",                        // else AC-3.4's own expiry notice can never be dismissed
        "Notifications.MarkAllRead",
        "PushDevices.Register",                          // fired at every mobile sign-in (AC-4.7)
        "PushDevices.Deregister",
        "PatientFiles.InitializeDefaultFolders",         // fired on the first visit to the Files tab; a READ fails
        "Dashboard.UpdatePreferences",                   // personal interface state, not clinic work

        // --- Getting your data out, and getting a colleague out (FR-3).
        "Backup.BackupNow",                              // the AC-4.2 argument; the scheduled one already keeps going
        // ⚠️ A POST that puts back records the cabinet already HAD (`clinic-data-archive-and-restore` AC-8). It is a
        // write by verb and by mechanism, and exempt anyway because recovering rows that once existed is not
        // recording new work — an expired cabinet that has also lost data is exactly the one that must recover it.
        // Its sibling download is a GET the gate never inspects, which is why only this half appears here.
        "Backup.RestoreArchive",
        // clinic-recovery-points: putting back records the cabinet already had is not recording new work (AC-8), and
        // an expired cabinet that has ALSO lost data is exactly the one that must be able to recover it. Same reason
        // as RestoreArchive beside it — this is the same operation from a server-kept copy instead of an upload.
        "Backup.RestoreFromRecoveryPoint",
        "Users.SetStatus",                               // offboarding must not wait on an invoice; the handler
                                                         // refuses the RE-activation direction, which the reason
                                                         // on the attribute never covered
        "Users.ResetPassword",                           // regaining READ access must not depend on payment: a
                                                         // forgotten password otherwise costs an expired cabinet
                                                         // the reads, exports and PDFs AC-4.1/4.2 guarantee, and
                                                         // hosted has no other recovery

        // --- The vendor console's own write (`platform-console` AC-4.1). It is the endpoint whose PURPOSE is to end
        // a refusal, and the cabinets it is used on are precisely the ones that have lapsed. A console account is
        // not a cabinet, so it passes the gate on that ground today — the attribute states the intent where a reader
        // finds it rather than leaving it to how the tenant scope happens to resolve.
        "PlatformSubscriptions.RecordPeriod",

        // --- And correcting one recorded by mistake (`platform-console` AC-5.1). The vendor's own bookkeeping, on
        // the cabinets likeliest to be lapsed — including where the mis-keyed entry is what caused the lapse, which
        // is the one case refusing it would make uncorrectable from the console.
        "PlatformSubscriptions.CancelPeriod",

        // --- Stopping a cabinet for abuse, and undoing that (`platform-console` AC-6.1/6.4). Suspension is not a
        // payment state (AC-6.3), so making it wait on the cabinet's own entitlement would be incoherent in both
        // directions: a fraudulent practice that has stopped paying is exactly the one still to be stopped, and a
        // mistaken suspension has to stay liftable on the same cabinet.
        "PlatformSubscriptions.Suspend",
        "PlatformSubscriptions.LiftSuspension",

        // --- Clearing a clinic account's second factor at its owner's request (`hosted-security-hardening`
        // FR-1.4). ⚠️ **The exemption is the point of the endpoint, not a convenience.** The person who cannot sign
        // in is very often the SOLE administrator of a cabinet whose cover lapsed *because* nobody could sign in to
        // pay for it. Gating this on the entitlement would make that lockout self-sustaining: no sign-in, therefore
        // no payment, therefore no cover, therefore no reset, therefore no sign-in. It also touches no ledger entry
        // and consumes no paid day — it is a support action on a person, not a transaction with a practice.
        "PlatformClinicSecurity.ResetSecondFactor",

        // --- And replacing a clinic account's forgotten password from the console. The row above's argument,
        // verbatim, applied to the credential somebody is far more likely to lose: the account that cannot sign in
        // is frequently the sole administrator of a cabinet whose cover lapsed *because* nobody could sign in to
        // pay for it, so gating this would make the lockout permanent by construction. It touches no ledger entry
        // and consumes no paid day either.
        "PlatformClinicSecurity.ResetPassword",

        // --- Putting a cabinet back that no longer exists (`clinic-data-archive-and-restore` AC-8). There is no
        // entitlement to read for a cabinet that is gone, and this is the action that gives it one — so a gate
        // consulting the cabinet's own cover would refuse exactly the request meant to create it.
        "PlatformClinicRestore.RestoreClinic",

        // --- The vendor console's WhatsApp-forfait writes (`vendor-whatsapp-messaging-quota` US-6/US-7). The same
        // argument as the two payment routes above, and it bites harder here: a cabinet whose *subscription* has lapsed
        // is precisely one whose patients may still need warning about visits it already has booked, so gating a
        // reminder top-up on that cabinet's own cover would let one lapse silence another. The correction half must
        // stay reachable for the mirror reason — a mis-keyed forfait is the vendor's own bookkeeping.
        "PlatformMessaging.RecordAllowance",
        "PlatformMessaging.CancelAllowance",
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
    [InlineData("DentalActs.GetReimbursementEstimates")]
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
