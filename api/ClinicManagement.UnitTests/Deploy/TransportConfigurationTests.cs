using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClinicManagement.API.Startup;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.UnitTests.Deploy;

/// <summary>
/// The shipped deployment configuration actually satisfies the check that guards it
/// (hosted-security-hardening Part 2, AC-5, FR-2.5's R-6: <b>the check and the configuration ship in the same
/// commit</b>).
///
/// <para><b>This is the guard that would have caught <c>Security:EnforceCsp</c> being unset for the life of the
/// deployment</b> — the spec's own example of the failure mode AC-5 exists to prevent: a guarantee that lives in
/// a configuration key somebody has to remember, with nothing in the build able to notice it was forgotten.</para>
///
/// <para><b>How it earns being derived rather than a list of expected strings.</b> The two load-bearing cases do
/// not compare text at all: <see cref="The_Shipped_Configuration_Satisfies_The_Startup_Check"/> feeds each compose
/// file's <i>own</i> values through the <b>real</b> <see cref="TransportAssurance"/> — including the real Npgsql
/// connection-string parser, so a keyword spelling the driver rejects cannot pass — and
/// <see cref="Every_Service_That_Mounts_The_Certificates_Waits_For_Them"/> derives the service set from the
/// mounts it finds rather than naming today's five.</para>
///
/// <para>⚠️ It parses the compose files via <see cref="CallerFilePathAttribute"/>, on
/// <c>RealtimeResourceResolverTests</c>' precedent, and <b>throws</b> when a file is absent: a contract test that
/// skips when it cannot find one side reports green while the contract goes unchecked. Every case asserts a
/// non-zero parsed count for the same reason — a renamed key or a moved file must not leave this passing
/// vacuously.</para>
/// </summary>
public class TransportConfigurationTests
{
    private const string HostedFile = "deploy/docker-compose.hosted.yml";
    private const string ProdFile = "deploy/docker-compose.prod.yml";
    private const string HbaFile = "deploy/postgres/pg_hba.conf";

    private const string ConnectionStringVariable = "ConnectionStrings__DefaultConnection";
    private const string MinioUseSslVariable = "MinIO__UseSSL";
    private const string MinioRootVariable = "MinIO__RootCertificate";
    private const string CertsVolume = "internal_certs";
    private const string CertsService = "certs";

    /// <summary>
    /// ⚠️ <b>The load-bearing case.</b> The compose file's own settings, through the production check — so
    /// « verified TLS is configured » is asserted by the same code that refuses to start without it, not by a
    /// substring somebody kept in step by hand. The certificate <i>file</i> is stubbed as usable: what a compose
    /// file controls is the settings, and whether <c>/certs/ca.crt</c> exists is the running deployment's business.
    /// </summary>
    [Theory]
    [InlineData(HostedFile, DeploymentKind.HostedMultiTenant)]
    [InlineData(ProdFile, DeploymentKind.CloudBrowser)]
    public void The_Shipped_Configuration_Satisfies_The_Startup_Check(string composeFile, DeploymentKind kind)
    {
        var api = Services(composeFile)["api"];
        Assert.NotEmpty(api.Environment);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(api.Environment.ToDictionary(
                e => e.Key.Replace("__", ":", StringComparison.Ordinal),
                e => (string?)e.Value))
            .Build();

        var result = TransportAssurance.Inspect(
            configuration, DeploymentProfile.For(kind), DateTime.UtcNow, UsableCertificate());

        Assert.True(result.Applies, $"{composeFile}: TransportAssurance must apply to {kind}.");
        Assert.True(
            result.IsSatisfied,
            $"{composeFile} does not satisfy the transit check it ships with: "
            + string.Join(" | ", result.Problems));
    }

    // Stated separately from the check above so a failure names WHICH half is missing. `Root Certificate` matters
    // as much as the mode: verify-full with no root cannot verify anything and refuses every connection.
    [Theory]
    [InlineData(HostedFile)]
    [InlineData(ProdFile)]
    public void The_Database_Connection_Is_Verified_Tls(string composeFile)
    {
        var connectionString = Services(composeFile)["api"].Environment[ConnectionStringVariable];

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(TransportAssurance.RequiredSslMode, builder.SslMode);
        Assert.False(
            string.IsNullOrWhiteSpace(builder.RootCertificate),
            $"{composeFile}: SSL Mode=VerifyFull with no « Root Certificate » verifies nothing and refuses "
            + "every connection.");
        Assert.Equal("postgres", builder.Host);
    }

    [Theory]
    [InlineData(HostedFile)]
    [InlineData(ProdFile)]
    public void The_Object_Store_Connection_Is_Tls_Against_The_Internal_Root(string composeFile)
    {
        var environment = Services(composeFile)["api"].Environment;

        Assert.Equal("true", environment[MinioUseSslVariable]);
        Assert.False(string.IsNullOrWhiteSpace(environment[MinioRootVariable]));
    }

    /// <summary>
    /// ⚠️ <b>The <c>extends</c> trap, derived.</b> Compose deliberately does not carry <c>depends_on</c> across
    /// <c>extends</c>, so the hosted file has to restate every dependency — and a dropped one starts postgres
    /// before its certificate exists, which fails as a missing *file* two containers away from the cause. The
    /// service set comes from the mounts found in either file, so a sixth consumer added later is covered without
    /// touching this test.
    /// </summary>
    [Fact]
    public void Every_Service_That_Mounts_The_Certificates_Waits_For_Them()
    {
        var hosted = Services(HostedFile);
        var prod = Services(ProdFile);

        var needsCertificates = prod.Concat(hosted)
            .Where(s => s.Value.Volumes.Any(v => v.StartsWith(CertsVolume + ":", StringComparison.Ordinal)))
            .Select(s => s.Key)
            // The authority itself mounts the volume in order to WRITE it, and cannot wait for itself.
            .Where(name => name != CertsService)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            needsCertificates.Count >= 4,
            "Found almost no service mounting the internal certificates — the volume was renamed, or this "
            + $"parser stopped seeing volumes. Found: {string.Join(", ", needsCertificates)}");

        foreach (var (file, services) in new[] { (HostedFile, hosted), (ProdFile, prod) })
        {
            foreach (var name in needsCertificates.Where(services.ContainsKey))
            {
                Assert.Contains(CertsService, services[name].DependsOn);
            }
        }
    }

    /// <summary>
    /// The authority exists on both hosted kinds — and mounts the volume <b>writably</b>, which is the half worth
    /// asserting: every consumer takes it <c>:ro</c>, and copying that onto the one service whose job is to write
    /// the certificates makes issuance fail on a fresh volume. The hosted file reaches this definition through
    /// <c>extends</c>, so it declares the service and inherits the mount.
    /// </summary>
    [Fact]
    public void The_One_Shot_Certificate_Authority_Is_Declared_And_Can_Write()
    {
        Assert.Contains(CertsService, Services(HostedFile).Keys);

        var authority = Services(ProdFile)[CertsService];
        var mount = Assert.Single(
            authority.Volumes.Where(v => v.StartsWith(CertsVolume + ":", StringComparison.Ordinal)));

        Assert.EndsWith("/certs", mount, StringComparison.Ordinal);
        Assert.DoesNotContain(":ro", mount, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every consumer takes the certificates <b>read-only</b>. Derived from the mounts rather than listed: a new
    /// consumer added writably would be one container able to replace the root every other hop trusts.
    /// </summary>
    [Fact]
    public void Every_Consumer_Mounts_The_Certificates_Read_Only()
    {
        var consumers = Services(HostedFile).Concat(Services(ProdFile))
            .Where(s => s.Key != CertsService)
            .SelectMany(s => s.Value.Volumes
                .Where(v => v.StartsWith(CertsVolume + ":", StringComparison.Ordinal))
                .Select(v => (Service: s.Key, Mount: v)))
            .ToList();

        Assert.NotEmpty(consumers);

        foreach (var (service, mount) in consumers)
        {
            Assert.EndsWith(":ro", mount, StringComparison.Ordinal);
        }
    }

    // FR-2.3: the server's own refusal. `ssl=on` alone still accepts cleartext — it is the hostssl-only
    // pg_hba, reached through `hba_file`, that refuses it.
    [Fact]
    public void Postgres_Serves_Tls_And_Is_Pointed_At_The_Cleartext_Refusing_Hba()
    {
        var command = Services(ProdFile)["postgres"].Command;

        Assert.NotEmpty(command);
        Assert.Contains("ssl=on", command);
        Assert.Contains(command, c => c.StartsWith("ssl_cert_file=", StringComparison.Ordinal));
        Assert.Contains(command, c => c.StartsWith("ssl_key_file=", StringComparison.Ordinal));
        Assert.Contains(command, c => c.StartsWith("hba_file=", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole of FR-2.3 is an <b>absence</b>: <c>hostssl</c> matches only an already-encrypted connection, so
    /// a single <c>host</c> line would silently re-admit cleartext while every other check here still passed.
    /// </summary>
    [Fact]
    public void The_Hba_File_Offers_No_Cleartext_Host_Line()
    {
        var lines = File.ReadAllLines(Locate(HbaFile))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.StartsWith("hostssl", StringComparison.Ordinal));

        var cleartext = lines
            .Where(line => line.StartsWith("host", StringComparison.Ordinal)
                           && !line.StartsWith("hostssl", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            cleartext.Count == 0,
            "pg_hba.conf offers a cleartext `host` line, so the server no longer refuses unencrypted "
            + $"connections (FR-2.3): {string.Join(" / ", cleartext)}");
    }

    // FR-2.3's ⚠️: both sidecars connect with their own credentials, so they had to come across in the same
    // change. Left behind, the nightly dump fails at 02:00 and the symptom is a file nobody looks for.
    [Theory]
    [InlineData("backup")]
    [InlineData("pitr")]
    public void The_Backup_Sidecars_Connect_With_Verified_Tls(string service)
    {
        var environment = Services(ProdFile)[service].Environment;

        Assert.Equal("verify-full", environment["PGSSLMODE"]);
        Assert.False(string.IsNullOrWhiteSpace(environment["PGSSLROOTCERT"]));
    }

    /// <summary>
    /// <b>Reserved for Part D</b>, and asserted in the only way that is honest today: the enforcing-CSP key is
    /// either absent — Part D has not landed — or <c>true</c>. Never present-and-false, which is the state
    /// AC-5's own example describes, where the switch exists and nothing enforces anything.
    ///
    /// <para>When Part D adds it, this case starts asserting the full requirement with no edit.</para>
    /// </summary>
    [Theory]
    [InlineData(HostedFile)]
    [InlineData(ProdFile)]
    public void The_Enforcing_Csp_Key_Is_Never_Present_And_Off(string composeFile)
    {
        var environment = Services(composeFile)["api"].Environment;

        if (environment.TryGetValue("Security__EnforceCsp", out var enforceCsp))
        {
            Assert.Equal("true", enforceCsp);
        }
    }

    // ---- the reader ---------------------------------------------------------------------------------

    private sealed record ServiceBlock(
        Dictionary<string, string> Environment,
        List<string> Volumes,
        List<string> DependsOn,
        List<string> Command);

    /// <summary>
    /// A purpose-built reader for the two files in this repository, not a YAML implementation: it takes the
    /// four sub-blocks these assertions need. Deliberately no new package — the precedent here is
    /// <c>RealtimeResourceResolverTests</c> reading <c>clinic-hub.ts</c> and <c>CnamClosedSetContractTests</c>
    /// reading <c>cnam.ts</c>, both by hand — and every case above asserts a non-zero count, so a shape this
    /// reader stops understanding fails loudly instead of passing on an empty set.
    /// </summary>
    private static Dictionary<string, ServiceBlock> Services(string relativePath)
    {
        var services = new Dictionary<string, ServiceBlock>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(Locate(relativePath));

        var inServices = false;
        ServiceBlock? current = null;
        string? section = null;

        foreach (var raw in lines)
        {
            if (raw.TrimStart().StartsWith('#') || raw.Trim().Length == 0)
            {
                continue;
            }

            var indent = raw.Length - raw.TrimStart().Length;
            var line = raw.Trim();

            if (indent == 0)
            {
                inServices = line == "services:";
                current = null;
                section = null;
                continue;
            }

            if (!inServices)
            {
                continue;
            }

            if (indent == 2 && line.EndsWith(':'))
            {
                current = new ServiceBlock(new(StringComparer.Ordinal), new(), new(), new());
                services[line[..^1].Trim()] = current;
                section = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (indent == 4)
            {
                section = line.EndsWith(':') ? line[..^1].Trim() : null;
                continue;
            }

            switch (section)
            {
                case "environment" when indent >= 6 && line.Contains(':', StringComparison.Ordinal):
                    var separator = line.IndexOf(':', StringComparison.Ordinal);
                    current.Environment[line[..separator].Trim()] = CleanValue(line[(separator + 1)..].Trim());
                    break;

                case "volumes" when line.StartsWith("- ", StringComparison.Ordinal):
                    current.Volumes.Add(CleanValue(line[2..].Trim()));
                    break;

                case "command" when line.StartsWith("- ", StringComparison.Ordinal):
                    current.Command.Add(CleanValue(line[2..].Trim()));
                    break;

                // depends_on takes both forms: a bare list, and a map of names to a condition.
                case "depends_on" when line.StartsWith("- ", StringComparison.Ordinal):
                    current.DependsOn.Add(CleanValue(line[2..].Trim()));
                    break;

                case "depends_on" when indent == 6 && line.EndsWith(':'):
                    current.DependsOn.Add(line[..^1].Trim());
                    break;
            }
        }

        Assert.True(
            services.Count >= 5,
            $"Parsed only {services.Count} service(s) from {relativePath} — the file moved or this reader "
            + "stopped understanding its shape. A vacuous pass here would hide every assertion above.");

        return services;
    }

    /// <summary>
    /// Strips a trailing inline comment and surrounding quotes.
    ///
    /// <para>⚠️ The comment half is not cosmetic: several mounts in these files carry an explanation after the
    /// value (<c>internal_certs:/certs:ro    # the root pg_dump verifies against</c>), and without this the
    /// parsed value ends in prose — which showed up as <c>Every_Consumer_Mounts_The_Certificates_Read_Only</c>
    /// failing on a mount that was perfectly correct. A reader that quietly returns the wrong string makes every
    /// assertion above meaningless in the direction that passes.</para>
    ///
    /// <para>A quoted value is taken to its closing quote first, so a <c>#</c> inside one survives.</para>
    /// </summary>
    private static string CleanValue(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\''))
        {
            var closing = value.IndexOf(value[0], 1);
            if (closing > 0)
            {
                return value[1..closing];
            }
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }

    /// <summary>
    /// Locates a repository-relative file from this source file's own compile-time path — never
    /// <c>AppContext.BaseDirectory</c>, since the suite is routinely built to a scratch output directory outside
    /// the repository (the Smart App Control workaround). <b>Throws</b> rather than skipping.
    /// </summary>
    private static string Locate(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var native = relativePath.Replace('/', Path.DirectorySeparatorChar);

        for (var directory = new FileInfo(thisFile).Directory; directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, native);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from '{thisFile}'. The transit configuration cannot "
            + "be verified without it, and a skipped check reports green while the guarantee goes unchecked.");
    }

    private static InternalCertificate.Store UsableCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=internal test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        using var certificate = request.CreateSelfSigned(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(10));
        var pem = Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());

        return new InternalCertificate.Store(_ => true, _ => pem);
    }
}
