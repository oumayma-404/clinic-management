using System.Reflection;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Attribute-coverage guard for the Phase 4 release gate (FR-E3) <b>and</b> for the role matrix
/// (<c>adoption-qa-i-access-control-and-audit</c>, I2). In Local mode the fail-closed
/// <c>FallbackPolicy</c> authenticates every endpoint that is NOT explicitly <c>[AllowAnonymous]</c>
/// (including anonymous-by-omission ones such as the Google AJAX endpoints — they now fail closed).
/// The only remaining anonymous surface is the set of <c>[AllowAnonymous]</c> endpoints, which must
/// exactly match this approved allow-list: adding a new <c>[AllowAnonymous]</c> anywhere — the way a new
/// hole would appear — fails this test until it is reviewed and listed here.
///
/// <para><b>I2 — why the additions below are derived, not listed.</b> Authentication was never the gap. Before
/// I1 the product had <b>33 bare <c>[Authorize]</c> attributes</b> (any authenticated user, any role) and
/// <b>20 controllers with no policy at all</b> — la caisse, les créances, the dashboard, patient
/// delete/archive, the odontogram, every clinical note — while three of the five defined policies
/// (<c>DoctorOnly</c>, <c>SecretaryOnly</c>, <c>DoctorOrSecretary</c>) had <b>zero usages for the entire life
/// of the product</b>. Every test stayed green throughout, because the only policy assertion anywhere merely
/// checked that a policy <i>existed</i>. A policy that exists and is applied nowhere is indistinguishable from
/// one that is enforced everywhere, if that is all you ask.</para>
///
/// <para>So the two new guards ask the questions that could have failed: does <b>every action</b> resolve to a
/// named policy, and is the set of <b>defined</b> policies equal to the set of <b>applied</b> ones — in both
/// directions. The second is what makes an unused policy a build failure, and it is only satisfiable with
/// <b>no exemption list</b> because I1 applied a class-level policy to all 32 controllers and deleted the three
/// dead ones. Same pattern as <c>RealtimeResourceResolverTests</c> (backend keys vs. the frontend file) and
/// <c>verify-schema</c> (the EF model vs. the catalogue): derive both sides, compare, refuse to carry a list.</para>
///
/// <para>⚠️ Everything here reads the <b>compiled</b> attribute set with <c>inherit: true</c>, per the spec's
/// edge case — a policy applied on a base class is part of the effective surface and source text would miss it.</para>
/// </summary>
public class ControllerAuthorizationCoverageTests
{
    private static readonly HashSet<string> ExpectedAnonymous = new()
    {
        "Auth.GetMode",              // reports the auth mode so the frontend can render the right login UI
        "Auth.Login",                // bootstrap: email+password login (issues the session token)
        "Auth.Setup",                // bootstrap: first-run clinic+admin (also localhost-gated, AC-1.2a)
        "Auth.Register",             // bootstrap: clinic-code self-registration
        "Auth.SignUp",               // bootstrap: public clinic self-signup (clinic-self-signup). Anonymous by
                                     // necessity — the caller has no clinic and therefore no account to hold a
                                     // token. It creates nothing: one pending row and an emailed token, and the
                                     // whole endpoint 404s where DeploymentProfile.AllowsPublicClinicSignup is
                                     // false. Rate-limited per submitted account like its neighbours.
        "Auth.VerifySignUp",         // the other half: consumes that token and provisions the clinic. Anonymous
                                     // for the same reason and gated by the same capability; the 32-byte
                                     // single-use token IS the credential, and it issues no session in exchange.
        "Auth.Refresh",              // exchanges the HttpOnly session cookie for a short-lived access token
                                     // (security-hardening US-5). Anonymous by necessity — the caller has no
                                     // access token yet, that being the point. Not unauthenticated in effect:
                                     // the refresh token IS the credential, and it is signed, audience-bound,
                                     // lifetime-bound, and re-checked against live account state on every use.
                                     // Rate-limited like the other anonymous auth endpoints.
        "Auth.EnrolTotp",            // enrols a second factor from the login screen (hosted-security-hardening
                                     // FR-1.3). Anonymous by necessity: an account refused with
                                     // `totp_enrolment_required` has no session, which is the whole point of
                                     // that refusal. It verifies the PASSWORD before minting anything — so it
                                     // is not an unauthenticated write — and issues no session in exchange.
        "Auth.RedeemRecoveryCode",   // signs in with a single-use recovery code (FR-1.4), the one way back the
                                     // user can take alone. Anonymous for the reason Login is; the password is
                                     // verified FIRST so a wrong one burns no code, and both lockout tiers and
                                     // the per-account rate limit apply exactly as they do to Login.
        "Connectivity.Get",          // non-sensitive online/offline poll (Local-only; 404s in Cloud)
        "Meta.ClientRequirements",   // the client-version floor + both store URLs (mobile-native-shells AC-28).
                                     // Anonymous by necessity: a shell below the floor must be able to ask
                                     // BEFORE signing in, and the answer's whole purpose is to reach a client
                                     // every other route is refusing with 426. Nothing served is a secret — a
                                     // version number and two public store links. ClientVersionMiddleware
                                     // exempts this one route from the floor for the same reason (AC-29).
        "GoogleCalendar.Callback",   // OAuth browser redirect back from Google — cannot carry a bearer

        // --- The vendor console's sign-in surface (platform-console AC-1.2, AC-1.3a, AC-1.3b). ---
        // Anonymous by necessity, not by concession: all three are performed by a caller who has no console
        // session, that being the point of each. What bounds them is not authentication but (a) the listener —
        // ConsolePortGate 404s /api/platform/* on the public port and 404s it everywhere when the console is off,
        // so these are unreachable from the internet at all — and (b) the anonymous-auth rate limits, per
        // submitted account and per address, which RateLimiting.IsAnonymousAuthPath was widened to cover.
        // ⚠️ PlatformAuth.ChangePassword is deliberately NOT here: it requires a console session (AC-8.6), and it
        // is the one route a bootstrapped account may reach before changing its one-time password.
        "PlatformAuth.Login",        // e-mail + password + a one-time code
        "PlatformAuth.EnrolTotp",    // binds the secret the bootstrap verb issued; returns the recovery codes once
        "PlatformAuth.Recovery",     // single-use recovery code when the authenticator is gone

        // --- LAN device trust (P8, AC-44). Local-only; all four 404 in Cloud. ---
        // Anonymous by necessity, not by concession: a device cannot obtain a token until it trusts the
        // server's certificate, and it cannot trust that certificate until it has fetched these — requiring
        // auth here is a deadlock. Nothing served is a secret: a CA's PUBLIC certificate, the same bytes
        // wrapped as an Apple profile, install instructions, and a QR of an address already broadcast on the
        // LAN. The cleartext LAN exposure they gain is bounded to this prefix by TrustPortGate, which refuses
        // every other path on the trust port — without it, binding that port would publish the whole
        // cleartext API including Auth.Login.
        "Trust.Page",                // the French instructions page (the only HTML this API serves)
        "Trust.CaCertificate",       // .local/ca.crt — public CA cert, what Android imports
        "Trust.AppleProfile",        // the same CA as an iOS .mobileconfig
        "Trust.QrCode",              // PNG QR of this page's own LAN address
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

    // ---------------------------------------------------------------- I2: the role matrix

    /// <summary>Every controller in the API assembly. One source for all the scans below.</summary>
    private static IEnumerable<Type> Controllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
            .OrderBy(m => m.Name);

    /// <summary>
    /// The policy an action actually runs under: its own <c>[Authorize(Policy = …)]</c> if it has one, otherwise
    /// the controller's. Read through <c>inherit: true</c> so a policy declared on a base controller counts —
    /// the spec's own edge case, and the reason this is reflection over compiled attributes rather than a grep.
    /// </summary>
    private static string? EffectivePolicy(Type controller, MethodInfo action)
    {
        var onAction = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (!string.IsNullOrEmpty(onAction))
        {
            return onAction;
        }

        return controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));
    }

    /// <summary>
    /// The policy vocabulary, read off <see cref="AuthorizationPolicies"/>'s own public constants — so deleting
    /// or adding one changes this set automatically and the comparison below stays honest.
    /// </summary>
    private static SortedSet<string> DefinedPolicies() =>
        new(typeof(AuthorizationPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!),
            StringComparer.Ordinal);

    /// <summary>Every policy name actually reached by a request, across all controllers and actions.</summary>
    private static SortedSet<string> AppliedPolicies()
    {
        var applied = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var controller in Controllers())
        {
            foreach (var action in Actions(controller))
            {
                var policy = EffectivePolicy(controller, action);
                if (!string.IsNullOrEmpty(policy))
                {
                    applied.Add(policy);
                }
            }
        }

        return applied;
    }

    /// <summary>
    /// [I2] Every action either resolves to a named policy or is on the reviewed anonymous allow-list. A new
    /// action with no policy fails the build.
    ///
    /// <para>This is the assertion that would have caught the original defect: 33 endpoints carried a bare
    /// <c>[Authorize]</c> and 20 controllers carried nothing, and no test in the suite could see it. Note it is
    /// deliberately stricter than "is it authenticated" — Local mode's fail-closed fallback already guarantees
    /// authentication, so a bare <c>[Authorize]</c> looks safe while granting a secretary the clinic's revenue.</para>
    /// </summary>
    [Fact]
    public void Every_Action_Resolves_To_A_Named_Policy_Or_Is_Approved_Anonymous()
    {
        var anonymous = AnonymousEndpoints().ToHashSet(StringComparer.Ordinal);
        var unpoliced = new List<string>();

        foreach (var controller in Controllers())
        {
            var shortName = controller.Name.Replace("Controller", string.Empty);

            foreach (var action in Actions(controller))
            {
                var endpoint = $"{shortName}.{action.Name}";
                if (anonymous.Contains(endpoint))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(EffectivePolicy(controller, action)))
                {
                    unpoliced.Add(endpoint);
                }
            }
        }

        Assert.True(unpoliced.Count == 0,
            "Action(s) with no named authorization policy — a bare [Authorize] is any authenticated user, ANY "
            + "role, which is how a secretary reached the clinic's revenue: "
            + string.Join(", ", unpoliced.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// [I2] Every policy an action names must be one this codebase defines — a typo'd policy string is not a
    /// tighter gate, it is a policy ASP.NET cannot resolve.
    /// </summary>
    [Fact]
    public void Every_Applied_Policy_Is_One_Of_The_Defined_Ones()
    {
        var unknown = AppliedPolicies().Except(DefinedPolicies(), StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0,
            "Policy name(s) applied on a controller/action but not defined in AuthorizationPolicies: "
            + string.Join(", ", unknown));
    }

    /// <summary>
    /// [I2] …and every policy this codebase defines must be applied somewhere. **This is the direction that was
    /// missing**, and the one that let `DoctorOnly`, `SecretaryOnly` and `DoctorOrSecretary` sit unused for the
    /// product's whole life while the guard test asserted only that they existed.
    ///
    /// <para>There is deliberately <b>no exemption list</b>. A policy nobody applies is not a capability held in
    /// reserve — it is a comment that compiles, and its presence makes the authorization surface read as richer
    /// than it is. If a policy stops being needed, delete it; if it is needed, apply it.</para>
    /// </summary>
    [Fact]
    public void Every_Defined_Policy_Is_Applied_Somewhere()
    {
        var unused = DefinedPolicies().Except(AppliedPolicies(), StringComparer.Ordinal).ToList();

        Assert.True(unused.Count == 0,
            "Policy/policies defined in AuthorizationPolicies but applied nowhere: "
            + string.Join(", ", unused)
            + ". Apply it, or delete it — an unapplied policy makes the authorization surface look richer "
            + "than it is (that is exactly how DoctorOnly/SecretaryOnly/DoctorOrSecretary went unnoticed).");
    }

    /// <summary>
    /// [I2] The two sets are equal, stated as one assertion so the failure message shows both sides at once.
    /// Kept alongside the two directional tests because when this one fails you want the whole picture.
    /// </summary>
    [Fact]
    public void Defined_And_Applied_Policy_Sets_Are_Equal_In_Both_Directions()
    {
        var defined = DefinedPolicies();
        var applied = AppliedPolicies();

        Assert.True(defined.SetEquals(applied),
            $"Defined [{string.Join(", ", defined)}] != applied [{string.Join(", ", applied)}]");
    }

    /// <summary>
    /// [I2] Every defined policy is actually registered by <c>ConfigurePolicies</c>, in both modes. Closes the
    /// loop: a constant applied on a controller but never registered is a 500 at request time, not a gate.
    /// Derived from the constants rather than the hand-list this replaces.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothModes))]
    public void Every_Defined_Policy_Is_Registered(bool isLocalMode)
    {
        var options = new AuthorizationOptions();
        AuthorizationPolicies.ConfigurePolicies(options, isLocalMode);

        var unregistered = DefinedPolicies().Where(p => options.GetPolicy(p) is null).ToList();

        Assert.True(unregistered.Count == 0,
            "Policy/policies defined but not registered by ConfigurePolicies: " + string.Join(", ", unregistered));
    }

    /// <summary>
    /// [I2] No controller carries a **bare** class-level <c>[Authorize]</c> any more. Narrower than the
    /// per-action guard above and worth its own line: the class-level attribute is what 20 controllers were
    /// missing entirely and what 12 others had in its policy-less form, so this is the shape of the original
    /// defect stated directly.
    ///
    /// <para>An <c>[AllowAnonymous]</c>-only controller would legitimately have none — there is none today, and
    /// if one appears its actions are already covered by the anonymous allow-list above.</para>
    /// </summary>
    [Fact]
    public void No_Controller_Carries_A_Bare_Class_Level_Authorize()
    {
        var bare = Controllers()
            .Where(c => c.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                        && c.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                            .All(a => string.IsNullOrEmpty(a.Policy)))
            .Select(c => c.Name)
            .ToList();

        Assert.True(bare.Count == 0,
            "Controller(s) with a policy-less class-level [Authorize] — authenticated but ANY role: "
            + string.Join(", ", bare));
    }

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { true }, new object[] { false } };
}
