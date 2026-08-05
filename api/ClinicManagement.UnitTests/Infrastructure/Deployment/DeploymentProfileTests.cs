using System.Reflection;
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
            [nameof(DeploymentProfile.ExposesMetaOnboarding)] = (false, true, true)
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

        foreach (var capability in Capabilities())
        {
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
