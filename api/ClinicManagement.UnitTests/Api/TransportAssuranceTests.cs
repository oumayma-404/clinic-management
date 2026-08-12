using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClinicManagement.API.Startup;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The fail-loud transit check (hosted-security-hardening Part 2, FR-2.5).
///
/// <para><b>Most of this class asserts what must REFUSE</b>, which is the opposite balance from most guards here
/// and is deliberate: this check stands in front of the whole hosted deployment, so a wrong "satisfied" verdict
/// does not degrade a feature — it lets patient data move in the clear on every hop, silently, for the life of
/// the deployment. The three certificate verdicts are separated because each is a case a presence check calls
/// healthy.</para>
///
/// <para>⚠️ <b>And one case asserts the opposite direction just as hard</b>:
/// <see cref="It_Does_Not_Apply_Where_The_Front_Door_Is_Self_Hosted"/>. A clinic's own Windows PC has no internal
/// CA and reaches PostgreSQL on the same machine, so a check that applied there would refuse to start every
/// offline install in the field — strictly worse than the exposure it prevents (FR-2.7).</para>
/// </summary>
public class TransportAssuranceTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    private const string VerifiedConnection =
        "Host=postgres;Database=clinic;Username=u;Password=p;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";

    [Fact]
    public void It_Does_Not_Apply_Where_The_Front_Door_Is_Self_Hosted()
    {
        var result = Inspect(
            DeploymentProfile.For(DeploymentKind.SelfHostedLan),
            new Dictionary<string, string?>());

        Assert.False(result.Applies);
        Assert.True(result.IsSatisfied);
        Assert.Empty(result.Problems);
    }

    // ⚠️ BOTH hosted kinds, not HostedMultiTenant alone: docker-compose.hosted.yml `extends` the prod file's
    // infrastructure and deploy/postgres/Dockerfile is shared, so ssl=on and the hostssl-only pg_hba land on
    // CloudBrowser too. A check one kind narrower than its own configuration lets transit fail open there.
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public void It_Applies_To_Every_Hosted_Kind(DeploymentKind kind)
    {
        var result = Inspect(DeploymentProfile.For(kind), new Dictionary<string, string?>());

        Assert.True(result.Applies);
        Assert.False(result.IsSatisfied);
    }

    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public void A_Fully_Configured_Deployment_Starts(DeploymentKind kind)
    {
        var result = Inspect(DeploymentProfile.For(kind), Configured());

        Assert.True(result.Applies);
        Assert.True(result.IsSatisfied);
    }

    // `Require` and `Prefer` encrypt and accept ANY certificate, so they stop a packet capture and not an
    // impostor on the container network. Only verify-full checks identity, which is what FR-2.1 asks for.
    [Theory]
    [InlineData("Require")]
    [InlineData("Prefer")]
    [InlineData("Disable")]
    [InlineData("VerifyCA")]
    public void Anything_Short_Of_VerifyFull_Refuses_And_Names_The_Setting(string sslMode)
    {
        var settings = Configured();
        settings[TransportAssurance.ConnectionStringKey] =
            $"Host=postgres;Database=clinic;Username=u;Password=p;SSL Mode={sslMode};Root Certificate=/certs/ca.crt";

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(
            result.Problems,
            p => p.Contains(TransportAssurance.ConnectionStringKey, StringComparison.Ordinal)
                 && p.Contains("VerifyFull", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Absent_Connection_String_Refuses()
    {
        var settings = Configured();
        settings[TransportAssurance.ConnectionStringKey] = null;

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains(TransportAssurance.ConnectionStringKey, StringComparison.Ordinal));
    }

    // The connection string is PARSED, not pattern-matched — and this is the case that proves the difference is
    // real: `sslmode=verify-full` is libpq's spelling, which Npgsql rejects outright. A substring check for
    // "verify-full" would call this deployment configured while the driver refuses to open the connection.
    [Fact]
    public void Libpqs_Own_Spelling_Is_Refused_Rather_Than_Silently_Accepted()
    {
        var settings = Configured();
        settings[TransportAssurance.ConnectionStringKey] =
            "Host=postgres;Database=clinic;Username=u;Password=p;sslmode=verify-full;Root Certificate=/certs/ca.crt";

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains(TransportAssurance.ConnectionStringKey, StringComparison.Ordinal));
    }

    [Fact]
    public void A_Connection_String_With_No_Root_Certificate_Refuses_And_Names_The_Volume()
    {
        var settings = Configured();
        settings[TransportAssurance.ConnectionStringKey] =
            "Host=postgres;Database=clinic;Username=u;Password=p;SSL Mode=VerifyFull";

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains("internal_certs", StringComparison.Ordinal));
    }

    // The three certificate verdicts FR-2.5 requires be told apart. Each names the file; « pas de fichier »,
    // « illisible » and « pas encore valide » have three different fixes, and the last is the one a presence
    // check reports as healthy.
    [Fact]
    public void An_Absent_Certificate_Refuses_And_Names_The_File()
    {
        var result = Inspect(Hosted, Configured(), new InternalCertificate.Store(_ => false, _ => Array.Empty<byte>()));

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains("/certs/ca.crt", StringComparison.Ordinal)
                                              && p.Contains("introuvable", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Unreadable_Certificate_Refuses_And_Says_So()
    {
        var store = new InternalCertificate.Store(_ => true, _ => Encoding.UTF8.GetBytes("not a certificate"));

        var result = Inspect(Hosted, Configured(), store);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains("lisible", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Not_Yet_Valid_Certificate_Refuses_Rather_Than_Reading_As_Present()
    {
        using var future = SelfSigned(Now.AddDays(7), Now.AddDays(3650));

        var result = Inspect(Hosted, Configured(), StoreReturning(future));

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains("pas encore valide", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Expired_Certificate_Refuses()
    {
        using var expired = SelfSigned(Now.AddDays(-100), Now.AddDays(-1));

        var result = Inspect(Hosted, Configured(), StoreReturning(expired));

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains("expiré", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("treu")]
    public void Object_Store_TLS_Off_Or_Mistyped_Refuses_And_Names_The_Key(string? useSsl)
    {
        var settings = Configured();
        settings[TransportAssurance.MinioUseSslKey] = useSsl;

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Problems, p => p.Contains(TransportAssurance.MinioUseSslKey, StringComparison.Ordinal));
    }

    [Fact]
    public void An_Object_Store_With_No_Root_Certificate_Refuses()
    {
        var settings = Configured();
        settings[InternalCertificate.MinioRootCertificateKey] = null;

        var result = Inspect(Hosted, settings);

        Assert.False(result.IsSatisfied);
        Assert.Contains(
            result.Problems,
            p => p.Contains(InternalCertificate.MinioRootCertificateKey, StringComparison.Ordinal));
    }

    // Every problem, not the first: these settings are set together and are usually wrong together, so reporting
    // one per container restart is a loop an operator pays for in minutes. Four independent faults, four lines.
    [Fact]
    public void Independent_Faults_Are_All_Reported_At_Once()
    {
        var result = Inspect(Hosted, new Dictionary<string, string?>
        {
            [TransportAssurance.ConnectionStringKey] = "Host=postgres;Database=clinic;Username=u;Password=p",
            [TransportAssurance.MinioUseSslKey] = "false",
            [InternalCertificate.MinioRootCertificateKey] = null,
            // The store has to EXIST for its transit to be a fault — see Configured()'s note.
            ["MinIO:Endpoint"] = "minio:9000",
            ["MinIO:AccessKey"] = "clinic-access-key",
            ["MinIO:SecretKey"] = "clinic-secret-key",
        });

        Assert.False(result.IsSatisfied);
        Assert.Equal(4, result.Problems.Count);
        Assert.Contains(result.Problems, p => p.Contains("VerifyFull", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Contains("base de données", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Contains(TransportAssurance.MinioUseSslKey, StringComparison.Ordinal));
        Assert.Contains(
            result.Problems,
            p => p.Contains(InternalCertificate.MinioRootCertificateKey, StringComparison.Ordinal));
    }

    // A missing connection string reports ONE problem, not two: naming a « Root Certificate » inside a string
    // that does not exist is noise about a setting the operator has nowhere to put yet.
    [Fact]
    public void An_Absent_Connection_String_Does_Not_Also_Complain_About_Its_Root_Certificate()
    {
        var result = Inspect(Hosted, new Dictionary<string, string?>
        {
            [TransportAssurance.MinioUseSslKey] = "true",
            [InternalCertificate.MinioRootCertificateKey] = "/certs/ca.crt",
        });

        Assert.Single(result.Problems);
        Assert.Contains(TransportAssurance.ConnectionStringKey, result.Problems[0], StringComparison.Ordinal);
    }

    // The refusal an operator actually reads: every problem, plus what to do, in ONE message — a container's
    // last output has to hold all of it, since there is nobody at a console when this fires.
    [Fact]
    public void The_Refusal_Message_Carries_Every_Problem_And_Points_At_The_Runbook()
    {
        var result = Inspect(Hosted, new Dictionary<string, string?>());

        var message = TransportAssurance.RefusalMessage(result);

        foreach (var problem in result.Problems)
        {
            Assert.Contains(problem, message, StringComparison.Ordinal);
        }

        Assert.Contains("deploy/README.md", message, StringComparison.Ordinal);
    }

    private static DeploymentProfile Hosted => DeploymentProfile.For(DeploymentKind.HostedMultiTenant);

    /// <summary>
    /// A deployment with everything set. ⚠️ The three MinIO credentials are part of « configured » now: the
    /// object-store check is skipped where no store exists at all, so a fixture that omits them would exercise
    /// the skip and every assertion about object-store transit would pass vacuously.
    /// </summary>
    private static Dictionary<string, string?> Configured() => new()
    {
        [TransportAssurance.ConnectionStringKey] = VerifiedConnection,
        [TransportAssurance.MinioUseSslKey] = "true",
        [InternalCertificate.MinioRootCertificateKey] = "/certs/ca.crt",
        ["MinIO:Endpoint"] = "minio:9000",
        ["MinIO:AccessKey"] = "clinic-access-key",
        ["MinIO:SecretKey"] = "clinic-secret-key",
    };

    private static TransportAssurance.Result Inspect(
        DeploymentProfile profile,
        Dictionary<string, string?> settings,
        InternalCertificate.Store? store = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return TransportAssurance.Inspect(configuration, profile, Now, store ?? UsableCertificate());
    }

    /// <summary>A store answering with a root that is inside its window, so only the case under test fails.</summary>
    private static InternalCertificate.Store UsableCertificate()
    {
        using var certificate = SelfSigned(Now.AddDays(-1), Now.AddDays(3650));
        return StoreReturning(certificate);
    }

    private static InternalCertificate.Store StoreReturning(X509Certificate2 certificate)
    {
        var pem = Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());
        return new InternalCertificate.Store(_ => true, _ => pem);
    }

    private static X509Certificate2 SelfSigned(DateTime notBefore, DateTime notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=internal test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
