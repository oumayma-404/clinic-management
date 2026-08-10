using System.Reflection;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Deployment;

/// <summary>
/// The deployment profile and its capability matrix (multi-tenant-cloud, US-1 / Part A).
///
/// <para><b>The load-bearing test is <see cref="Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table"/></b>
/// (R-2). ~30 branches moved from one boolean to twelve named capabilities, and the only thing that makes that
/// safe is that the two profiles which already shipped answer every question exactly as they did. It asserts
/// against <see cref="LocalAuthConfig.IsLocalMode"/> <i>itself</i> rather than against a retyped table of
/// expected values, so it cannot drift from the boolean it replaced.</para>
///
/// <para><b>Why the explicit matrix as well.</b> <c>HostedMultiTenant</c> has no old-boolean counterpart — every
/// one of its twelve answers is a new decision, and a decision with no assertion behind it is a comment.
/// <see cref="Every_capability_is_covered_by_the_matrix"/> reflects over the type so a capability added later
/// cannot quietly escape the table, which is the same derived-guard discipline
/// <c>RealtimeResourceResolverTests</c> and <c>verify-schema</c> follow.</para>
/// </summary>
public class DeploymentProfileTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    /// <summary>
    /// The matrix, as data. Each entry is the capability's expected value per kind, in the order
    /// <c>SelfHostedLan</c>, <c>HostedMultiTenant</c>, <c>CloudBrowser</c>.
    /// </summary>
    private static readonly Dictionary<string, (bool SelfHostedLan, bool HostedMultiTenant, bool CloudBrowser)>
        ExpectedMatrix = new()
        {
            [nameof(DeploymentProfile.UsesLocalAccounts)] = (true, true, false),
            [nameof(DeploymentProfile.FailClosedAuthz)] = (true, true, false),
            [nameof(DeploymentProfile.EnforcesTokenState)] = (true, true, false),
            [nameof(DeploymentProfile.UsesDiskStorage)] = (true, false, false),
            [nameof(DeploymentProfile.SelfHostsFrontDoor)] = (true, false, false),
            [nameof(DeploymentProfile.SelfSignsCertificate)] = (true, false, false),
            [nameof(DeploymentProfile.RunsAsWindowsService)] = (true, false, false),
            [nameof(DeploymentProfile.DefersMigrations)] = (true, false, false),
            [nameof(DeploymentProfile.RunsStartupBackfills)] = (false, true, true),
            [nameof(DeploymentProfile.ExposesTrustEndpoints)] = (true, false, false),
            [nameof(DeploymentProfile.HasLocalDbTooling)] = (true, false, false),
            [nameof(DeploymentProfile.ExposesMetaOnboarding)] = (false, true, true),
            // US-3. ⚠️ The one capability where HostedMultiTenant parts company with SelfHostedLan while sharing
            // its login provider — so it is also the one the old `UsesLocalAccounts` guard on `register` got
            // wrong. R-2 still holds: the two shipped kinds answer exactly as IsLocalMode did.
            [nameof(DeploymentProfile.AllowsSelfRegistration)] = (true, false, false),
            // US-4, added by the multi-tenant-cloud author in parallel with Part 6. The row is here because
            // `DeploymentProfile.cs` could not be staged without their capability (their addition and Part 6's
            // `PermitsOsPush` land in one diff hunk), and a capability with no row fails the drift guard below.
            // The three values are read off `For(kind)` itself, not chosen here.
            // clinic-self-signup. The first capability true of HostedMultiTenant ALONE, which is what forced the
            // `hostedOnlyCapabilities` set in the R-2 test below — see the comment there.
            [nameof(DeploymentProfile.AllowsPublicClinicSignup)] = (false, true, false),
            // platform-console. The SECOND capability true of HostedMultiTenant alone, so it joins the
            // `hostedOnlyCapabilities` set below for the same reason AllowsPublicClinicSignup did.
            [nameof(DeploymentProfile.ServesPlatformConsole)] = (false, true, false)
        };

    private static IEnumerable<PropertyInfo> Capabilities() =>
        typeof(DeploymentProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool));

    private static bool Read(DeploymentProfile profile, string capability) =>
        (bool)typeof(DeploymentProfile).GetProperty(capability)!.GetValue(profile)!;

    // ---- R-2: the two shipped profiles are byte-identical ------------------------------------------

    /// <summary>
    /// [R-2] Every capability of the two pre-existing kinds equals what <c>IsLocalMode</c> answered — directly
    /// for the ten questions that were true in Local, negated for the two that were true in Cloud. Asserting
    /// against the live boolean is the point: a table of expected values here would be a second copy of the
    /// answer and could agree with a mistake.
    /// </summary>
    [Theory]
    [InlineData("Local")]
    [InlineData("Cloud")]
    public void Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table(string authMode)
    {
        var configuration = Configuration(("Auth:Mode", authMode));
        var wasLocal = LocalAuthConfig.IsLocalMode(configuration);
        var profile = DeploymentProfile.Resolve(configuration);

        // The two capabilities that answered the other way round: a hosted deployment owes the startup
        // backfills and can reach Meta's onboarding, and neither is true of a clinic's own PC.
        var invertedCapabilities = new[]
        {
            nameof(DeploymentProfile.RunsStartupBackfills),
            nameof(DeploymentProfile.ExposesMetaOnboarding)
        };

        // ⚠️ Capabilities true of `HostedMultiTenant` and of **neither** shipped kind. They are `false` here
        // whatever `wasLocal` says, so they are neither `wasLocal` nor its negation and the loop below cannot
        // express them.
        //
        // This is *more* faithful to R-2 rather than a dodge of it. R-2 is « both shipped profiles behave exactly
        // as before », and a capability the third kind alone holds is precisely a capability that changes neither
        // of them — asserting `false` for both is the strongest statement available, and it is exactly what the
        // contract asks. The alternative (giving one of the shipped kinds a value so the old shape still fits)
        // would be changing a profile to satisfy a test.
        var hostedOnlyCapabilities = new[]
        {
            nameof(DeploymentProfile.AllowsPublicClinicSignup),
            nameof(DeploymentProfile.ServesPlatformConsole)
        };

        foreach (var capability in Capabilities())
        {
            if (hostedOnlyCapabilities.Contains(capability.Name))
            {
                Assert.False(Read(profile, capability.Name));
                continue;
            }

            var expected = invertedCapabilities.Contains(capability.Name) ? !wasLocal : wasLocal;

            Assert.Equal(expected, Read(profile, capability.Name));
        }
    }

    [Theory]
    [InlineData("Local", DeploymentKind.SelfHostedLan)]
    [InlineData("local", DeploymentKind.SelfHostedLan)]
    [InlineData("Cloud", DeploymentKind.CloudBrowser)]
    [InlineData("", DeploymentKind.CloudBrowser)]
    [InlineData(null, DeploymentKind.CloudBrowser)]
    public void An_absent_profile_key_derives_the_kind_from_Auth_Mode(string? authMode, DeploymentKind expected)
    {
        var profile = DeploymentProfile.Resolve(Configuration(("Auth:Mode", authMode)));

        Assert.Equal(expected, profile.Kind);
    }

    // ---- The matrix, including the kind that is new ------------------------------------------------

    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public void Each_kind_answers_every_capability_as_the_matrix_says(DeploymentKind kind)
    {
        var profile = DeploymentProfile.For(kind);

        Assert.Equal(kind, profile.Kind);

        foreach (var (capability, expected) in ExpectedMatrix)
        {
            var wanted = kind switch
            {
                DeploymentKind.SelfHostedLan => expected.SelfHostedLan,
                DeploymentKind.HostedMultiTenant => expected.HostedMultiTenant,
                DeploymentKind.CloudBrowser => expected.CloudBrowser,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

            Assert.Equal(wanted, Read(profile, capability));
        }
    }

    /// <summary>
    /// A drift guard on the matrix above: a capability added to <see cref="DeploymentProfile"/> without a row
    /// here would otherwise be shipped with nothing asserting its three answers.
    /// </summary>
    [Fact]
    public void Every_capability_is_covered_by_the_matrix()
    {
        var declared = Capabilities().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            ExpectedMatrix.Keys.OrderBy(n => n, StringComparer.Ordinal),
            declared);
    }

    // ---- Resolving the explicit key ---------------------------------------------------------------

    [Theory]
    [InlineData("SelfHostedLan", DeploymentKind.SelfHostedLan)]
    [InlineData("HostedMultiTenant", DeploymentKind.HostedMultiTenant)]
    [InlineData("hostedmultitenant", DeploymentKind.HostedMultiTenant)]
    [InlineData("  HostedMultiTenant  ", DeploymentKind.HostedMultiTenant)]
    [InlineData("CloudBrowser", DeploymentKind.CloudBrowser)]
    public void An_explicit_profile_key_wins(string configured, DeploymentKind expected)
    {
        // Auth:Mode says the opposite on purpose: the explicit key is the authority once it is present.
        var profile = DeploymentProfile.Resolve(Configuration(
            (DeploymentProfile.ProfileKey, configured),
            ("Auth:Mode", "Cloud")));

        Assert.Equal(expected, profile.Kind);
    }

    [Fact]
    public void The_hosted_profile_keeps_local_accounts_without_any_of_the_local_hosting_machinery()
    {
        var profile = DeploymentProfile.Resolve(Configuration(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant))));

        // « the second deployment's infrastructure with the first's authentication » — the whole plan in one line.
        Assert.True(profile.UsesLocalAccounts);
        Assert.True(profile.FailClosedAuthz);
        Assert.True(profile.EnforcesTokenState);
        Assert.False(profile.SelfHostsFrontDoor);
        Assert.False(profile.SelfSignsCertificate);
        Assert.False(profile.RunsAsWindowsService);
        Assert.False(profile.UsesDiskStorage);
    }

    // ---- Per-platform OS push (mobile-native-shells Part 6) ---------------------------------------

    /// <summary>
    /// [FR-10] The Kind half of the push capability, per platform and per kind.
    ///
    /// <para>It is a <b>method</b> rather than a <c>bool</c> property, so it is deliberately outside
    /// <see cref="ExpectedMatrix"/> and outside the R-2 truth-table test above — and it has to be: its answer is
    /// <c>false</c> for <c>SelfHostedLan</c>, where <c>IsLocalMode</c> was <c>true</c>, so a property would have
    /// broken that assertion on arrival. This theory is its matrix.</para>
    /// </summary>
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan, DevicePlatform.Android, false)]
    [InlineData(DeploymentKind.SelfHostedLan, DevicePlatform.Ios, false)]
    [InlineData(DeploymentKind.HostedMultiTenant, DevicePlatform.Android, true)]
    [InlineData(DeploymentKind.HostedMultiTenant, DevicePlatform.Ios, true)]
    [InlineData(DeploymentKind.CloudBrowser, DevicePlatform.Android, true)]
    [InlineData(DeploymentKind.CloudBrowser, DevicePlatform.Ios, true)]
    public void Each_kind_answers_whether_it_permits_os_push_per_platform(
        DeploymentKind kind, DevicePlatform platform, bool expected)
    {
        Assert.Equal(expected, DeploymentProfile.For(kind).PermitsOsPush(platform));
    }

    /// <summary>
    /// [R-4] The boundary the whole split exists to protect: <c>SelfHostedLan</c> stays ✗ <b>whatever</b> an
    /// operator configures.
    ///
    /// <para>This is <c>LEARNINGS :45</c>'s <c>httpsConfigured</c> trap — a value derived from configuration that
    /// merely <i>correlated</i> with the mode, and so silently changed the other mode's behaviour once it was set.
    /// Push credentials are the first thing this product configures that a capability could have been tempted to
    /// read, so the credentials half lives in <c>IOsPushAvailability</c> and this method never sees it. Asserted by
    /// resolving a profile with the keys <b>present</b> and checking the answer has not moved.</para>
    /// </summary>
    [Fact]
    public void A_self_hosted_lan_install_permits_no_push_however_it_is_configured()
    {
        var profile = DeploymentProfile.Resolve(Configuration(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.SelfHostedLan)),
            ("Push:Fcm:ProjectId", "clinic-push"),
            ("Push:Fcm:ServiceAccountKey", "a-real-looking-key"),
            ("Push:Apns:BundleId", "tn.cabinet.clinic"),
            ("Push:Apns:PrivateKey", "a-real-looking-key")));

        Assert.False(profile.PermitsOsPush(DevicePlatform.Android));
        Assert.False(profile.PermitsOsPush(DevicePlatform.Ios));
    }

    /// <summary>
    /// An unrecognised value throws instead of falling back. Falling back would hand a hosted deployment Auth0
    /// login and no local accounts — silently, on a typo — which is the failure this key exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("Hosted")]
    [InlineData("SelfHosted")]
    [InlineData("hosted-multi-tenant")]
    public void An_unrecognised_profile_key_fails_loud(string configured)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeploymentProfile.Resolve(Configuration((DeploymentProfile.ProfileKey, configured))));

        Assert.Contains(DeploymentProfile.ProfileKey, exception.Message);
        // The message must name the valid values — an operator reading it should not need this source file.
        Assert.Contains(nameof(DeploymentKind.HostedMultiTenant), exception.Message);
    }
}
