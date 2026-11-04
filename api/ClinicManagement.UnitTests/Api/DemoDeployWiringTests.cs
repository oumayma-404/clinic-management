using System.Text.RegularExpressions;
using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// demo-cabinet story 7: the demo beside production on one server. Nothing here runs Docker; it reads the shipped
/// files, because every one of these mistakes is invisible until the server meets it.
/// </summary>
public class DemoDeployWiringTests
{
    private static string Repo(params string[] parts)
    {
        var path = Path.Combine(new[] { SolutionSources.Root().Parent!.FullName }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : throw new FileNotFoundException("missing", path);
    }

    private static string Workflow => Repo(".github", "workflows", "deploy-hosted.yml");

    private static string Overlay => Repo("deploy", "docker-compose.demo.yml");

    // A compose command that forgets the overlay runs `--remove-orphans` over the demo and deletes it.
    [Fact]
    public void Every_Compose_Command_Of_The_Deploy_Carries_The_Demo_Overlay_When_It_Is_Configured()
    {
        var lines = Workflow.Replace("\r\n", "\n").Split('\n');
        var assignments = lines.Select((line, i) => (line, i))
            .Where(x => Regex.IsMatch(x.line, @"^\s*COMPOSE=""docker compose "))
            .ToList();

        Assert.True(assignments.Count >= 3, $"expected the deploy's compose commands, found {assignments.Count}");
        Assert.All(assignments, a =>
            Assert.Contains(lines.Skip(a.i + 1).Take(3), l => l.Contains("-f docker-compose.demo.yml", StringComparison.Ordinal)
                && l.Contains("DEMO_DOMAIN", StringComparison.Ordinal)));
    }

    [Fact] // [AC-7.1] nothing can leave the demo: no mail, WhatsApp, Google, push or reminder settings at all
    public void The_Demo_Carries_No_Outbound_Integration()
    {
        foreach (var prefix in new[] { "Notification__", "Meta__", "GoogleCalendar__", "Push__", "Reminders__", "Messaging__", "Smtp" })
        {
            Assert.DoesNotContain(prefix, Overlay, StringComparison.Ordinal);
        }
    }

    [Fact] // [AC-7.2][AC-7.3] its own database, role, signing key, issuer, key ring and bucket
    public void The_Demo_Has_Its_Own_Identity_Data_And_Files()
    {
        var overlay = Overlay;
        Assert.Contains("Deployment__Profile: PublicDemo", overlay, StringComparison.Ordinal);
        Assert.Contains("Database=${DEMO_POSTGRES_DB", overlay, StringComparison.Ordinal);
        Assert.Contains("Username=${DEMO_POSTGRES_USER", overlay, StringComparison.Ordinal);
        Assert.Contains("/run/secrets/demo_auth_local_signing_key", overlay, StringComparison.Ordinal);
        Assert.Contains("Auth__Local__Issuer: apexa-demo", overlay, StringComparison.Ordinal);
        Assert.Contains("demo_dataprotection_keys:/keys", overlay, StringComparison.Ordinal);
        Assert.Contains("MinIO__AccessKey: ${DEMO_MINIO_ACCESS_KEY}", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("${POSTGRES_PASSWORD}", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("${MINIO_ROOT_USER}", overlay, StringComparison.Ordinal);
        foreach (var productionSecret in new[] { "auth_local_signing_key", "keyring_certificate", "audit_chain_key" })
        {
            Assert.DoesNotMatch($@"(?<![a-z_]){productionSecret}", overlay.Replace("demo_" + productionSecret, ""));
        }
    }

    [Fact] // [EC-3] the demo shares production's database server: its pool and its role are both capped
    public void The_Demo_Cannot_Take_Productions_Database_Connections()
    {
        Assert.Matches(@"ConnectionStrings__DefaultConnection: ""[^""]*;Maximum Pool Size=\$\{DEMO_DB_POOL:-\d+\}""", Overlay);
        var runbook = Repo("deploy", "demo", "README.md");
        Assert.Matches(@"CREATE ROLE clinic_demo [^;]*CONNECTION LIMIT \d+;", runbook);
        Assert.Contains("ALTER ROLE clinic_demo SET statement_timeout", runbook, StringComparison.Ordinal);
    }

    [Fact] // [AC-7.4] every production deploy runs the demo on production's own api and web images
    public void The_Demo_Runs_Production_Images()
    {
        var registry = Repo("deploy", "docker-compose.registry.yml");
        foreach (var service in new[] { "api", "web" })
        {
            var image = Regex.Match(registry, $@"image: (\S+-{service}:\S+)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(image), $"no {service} image in the registry overlay");
            Assert.Contains($"image: {image}", Overlay, StringComparison.Ordinal);
        }
    }

    [Fact] // a demo that never turns healthy cannot fail a production deploy: `--wait` covers production's services only
    public void The_Deploy_Never_Waits_On_The_Demo()
    {
        var lines = Workflow.Split('\n');
        var waits = lines.Where(l => l.Contains("up -d", StringComparison.Ordinal) && l.Contains("--wait", StringComparison.Ordinal)).ToList();

        Assert.Contains(waits, l => l.TrimEnd().EndsWith("$prod_services", StringComparison.Ordinal));
        Assert.Contains("config --services", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(waits, l => l.Contains("demo", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("up -d --no-build --no-deps api-demo web-demo", StringComparison.Ordinal));
    }

    [Fact] // [EC-3] a crowd of visitors cannot take production's memory or CPU
    public void Both_Demo_Services_Are_Capped()
    {
        Assert.Equal(2, Regex.Matches(Overlay, @"^\s+mem_limit:", RegexOptions.Multiline).Count);
        Assert.Equal(2, Regex.Matches(Overlay, @"^\s+cpus:", RegexOptions.Multiline).Count);
    }

    [Fact] // the demo site exists only where the overlay mounts it; production's Caddyfile imports the folder
    public void The_Demo_Site_Is_Mounted_Where_The_Caddyfile_Imports()
    {
        Assert.Contains("import /etc/caddy/sites/*.caddy", Repo("deploy", "Caddyfile"), StringComparison.Ordinal);
        Assert.Contains("./demo/Caddyfile.demo:/etc/caddy/sites/demo.caddy:ro", Overlay, StringComparison.Ordinal);
        Assert.Contains("demo/Caddyfile.demo:/etc/caddy/sites/demo.caddy:ro", Workflow, StringComparison.Ordinal);
    }

    [Fact] // [AC-7.7][FR-4] not indexed, and no install offered
    public void The_Demo_Site_Is_Not_Indexed_And_Offers_No_Install()
    {
        var site = Repo("deploy", "demo", "Caddyfile.demo");
        Assert.Contains("X-Robots-Tag \"noindex, nofollow\"", site, StringComparison.Ordinal);
        Assert.Contains("Disallow: /", site, StringComparison.Ordinal);
        Assert.Matches(@"handle /manifest\.webmanifest \{\s*respond 404", site);
    }
}
