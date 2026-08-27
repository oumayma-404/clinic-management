using ClinicManagement.API.Startup;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The residency check — « where does this deployment's data end up? », the legal counterpart of
/// <see cref="TransportAssuranceTests"/>' « is this hop encrypted? ».
///
/// <para><b>The highest-value case is <see cref="A_Foreign_Pitr_Endpoint_Refuses_Startup"/></b>, because it is
/// the defect that actually shipped: <c>deploy/.env.hosted.example</c> carried
/// <c>WALG_S3_ENDPOINT=https://s3.us-west-002.backblazeb2.com</c> as a working default, so an operator who
/// changed nothing else continuously shipped every write to every patient record to Oregon — and every layer of
/// the product reported a healthy deployment while it happened.</para>
///
/// <para>⚠️ <b>Half of this class asserts what must NOT refuse</b>, and that balance is deliberate. This guard
/// stands in front of the whole hosted deployment: a wrong « refuse » verdict does not degrade a feature, it
/// takes a working practice off the air. An undeclared allow-list, an internal container name and a
/// <c>SelfHostedLan</c> install must each start cleanly.</para>
/// </summary>
public class DataResidencyAssuranceTests
{
    private const string TunisianHost = "s3.eodatacenter.tn";

    // ── What must NOT refuse ──────────────────────────────────────────────────────────────────────────

    // A clinic's own PC has no residency question: the data is already on the premises and its backup is a
    // folder on that machine. A check applying there would refuse to start every offline install in the field.
    [Fact]
    public void It_Does_Not_Apply_Where_The_Front_Door_Is_Self_Hosted()
    {
        var result = Inspect(
            DeploymentKind.SelfHostedLan,
            new Dictionary<string, string?>
            {
                [$"{DataResidencyAssurance.PitrEndpointKey}"] = "https://s3.us-west-002.backblazeb2.com",
            });

        Assert.False(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    // ⚠️ Undeclared is not forbidden. Refusing every deployment that had not yet declared a list would have
    // taught operators to leave the key empty for ever — the shape `Security:EnforceCsp` was left in for a whole
    // release. `Program.cs` warns on every boot instead; that half is asserted by the ordering test below.
    [Fact]
    public void An_Undeclared_Allow_List_Does_Not_Refuse()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            new Dictionary<string, string?>
            {
                [DataResidencyAssurance.PitrEndpointKey] = "https://s3.us-west-002.backblazeb2.com",
            });

        Assert.False(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    // ⚠️ A dotless host is a container on the compose network — traffic that never leaves the machine. Forcing
    // an operator to allow-list `minio` would train them to add whatever the refusal names, which is precisely
    // how an allow-list stops being a decision and becomes a transcription exercise.
    [Theory]
    [InlineData("minio:9000")]
    [InlineData("postgres")]
    public void An_Internal_Container_Name_Is_Not_Egress(string endpoint)
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (DataResidencyAssurance.MinioEndpointKey, endpoint)));

        Assert.True(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void A_Declared_Host_Is_Satisfied()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (DataResidencyAssurance.PitrEndpointKey, $"https://{TunisianHost}")));

        Assert.True(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    // An absent destination is not a foreign one. A deployment that has not wired PITR up yet must start.
    [Fact]
    public void An_Absent_Destination_Is_Not_A_Problem()
    {
        var result = Inspect(DeploymentKind.HostedMultiTenant, Declared());

        Assert.True(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    // ── What must refuse ──────────────────────────────────────────────────────────────────────────────

    // THE case this class exists for: the default that shipped.
    [Fact]
    public void A_Foreign_Pitr_Endpoint_Refuses_Startup()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (
                DataResidencyAssurance.PitrEndpointKey,
                "https://s3.us-west-002.backblazeb2.com")));

        Assert.True(result.Applies);
        Assert.False(result.IsSatisfied);

        var problem = Assert.Single(result.Problems);
        // The host is named, so an operator reading `docker logs` knows which value to change...
        Assert.Contains("s3.us-west-002.backblazeb2.com", problem);
        // ...and the compose variable is named, so they know WHERE to change it.
        Assert.Contains("WALG_S3_ENDPOINT", problem);
    }

    [Fact]
    public void A_Foreign_Object_Store_Refuses_Startup()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (DataResidencyAssurance.MinioEndpointKey, "s3.eu-west-3.amazonaws.com")));

        Assert.False(result.IsSatisfied);
        Assert.Contains("MinIO__Endpoint", Assert.Single(result.Problems));
    }

    // ⚠️ Unreadable is refused, not skipped: a destination nobody can parse is not a destination anybody has
    // checked, and skipping it would make a typo the quiet way past this guard.
    [Fact]
    public void An_Unreadable_Destination_Refuses_Startup()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (DataResidencyAssurance.PitrEndpointKey, "::: not a host :::")));

        Assert.False(result.IsSatisfied);
    }

    // Every problem is reported, not the first: the two destinations are configured together and are usually
    // wrong together, and an operator restarting a container once per mistake is a loop measured in minutes.
    [Fact]
    public void Every_Foreign_Destination_Is_Reported_Not_Just_The_First()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(
                with: (DataResidencyAssurance.PitrEndpointKey, "https://s3.us-west-002.backblazeb2.com"),
                and: (DataResidencyAssurance.MinioEndpointKey, "s3.eu-west-3.amazonaws.com")));

        Assert.Equal(2, result.Problems.Count);
    }

    // ── What it cannot check, and says so ─────────────────────────────────────────────────────────────

    // ⚠️ The load-bearing distinction in this class. `offsite:clinic-backups` is an rclone REMOTE; the host
    // behind it lives in deploy/rclone/rclone.conf, which this process never reads and which belongs to another
    // container. Reporting it as satisfied would convert « unknown » into « checked » on a nightly full dump of
    // every clinic's records — so it lands in `Unverified`, which does NOT block the boot but IS logged.
    [Fact]
    public void An_Rclone_Remote_Is_Reported_As_Unverified_Rather_Than_Satisfied()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (DataResidencyAssurance.BackupRemoteKey, "offsite:clinic-backups")));

        // It does not refuse — nothing here knows the answer, and blocking on it would be a guess.
        Assert.True(result.IsSatisfied);
        Assert.Empty(result.Problems);

        // But it is never silent.
        var note = Assert.Single(result.Unverified);
        Assert.Contains("offsite:clinic-backups", note);
        Assert.Contains("rclone.conf", note);
    }

    [Fact]
    public void No_Backup_Remote_Produces_No_Note()
    {
        var result = Inspect(DeploymentKind.HostedMultiTenant, Declared());

        Assert.Empty(result.Unverified);
    }

    // ── The refusal an operator actually reads ────────────────────────────────────────────────────────

    // The message has to carry the legal reason, not just the mechanical one: the person reading it is deciding
    // whether to change the value or to add the host to the allow-list, and only one of those is lawful.
    [Fact]
    public void The_Refusal_Names_The_Law_And_The_Runbook()
    {
        var result = Inspect(
            DeploymentKind.HostedMultiTenant,
            Declared(with: (
                DataResidencyAssurance.PitrEndpointKey,
                "https://s3.us-west-002.backblazeb2.com")));

        var message = DataResidencyAssurance.RefusalMessage(result);

        Assert.Contains("2004-63", message);
        Assert.Contains("INPDP", message);
        Assert.Contains("deploy/README.md", message);
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string?> Declared(
        (string Key, string Value)? with = null,
        (string Key, string Value)? and = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{DataResidencyAssurance.AllowedEgressHostsKey}:0"] = TunisianHost,
        };

        if (with is not null)
        {
            values[with.Value.Key] = with.Value.Value;
        }

        if (and is not null)
        {
            values[and.Value.Key] = and.Value.Value;
        }

        return values;
    }

    private static DataResidencyAssurance.Result Inspect(
        DeploymentKind kind,
        Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return DataResidencyAssurance.Inspect(configuration, DeploymentProfile.For(kind));
    }
}
