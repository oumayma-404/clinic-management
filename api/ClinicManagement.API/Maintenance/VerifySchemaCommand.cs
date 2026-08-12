using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utility that asserts the database actually has the schema the EF model describes, and
/// that every data migration in this feature finished its job.
///
///   ClinicManagement.API.exe verify-schema
///
/// Runs on the clinic SERVER PC without starting the web server, so it can be used on a stopped app — which is
/// the point: run it BEFORE a migration batch, keep the output, run it AFTER, and diff. It is strictly read-only.
///
/// <para><b>Why the verb exists.</b> Nothing in the test project touches a database, so a migration is the one
/// class of change unit tests structurally cannot verify: an index can be missing, an exclusion constraint can
/// be non-partial, a backfill can cover zero rows, and the whole suite still passes. This is the gate for that
/// class of change.</para>
///
/// Exit codes mirror <see cref="ReconcileMoneyCommand"/>, because "could not run" and "ran and found a problem"
/// must not look the same to an operator or a script:
///   0 = every check passed
///   1 = the utility could not run (wrong mode, bad config, unreachable database)
///   2 = the utility ran and found at least one mismatch
/// </summary>
public static class VerifySchemaCommand
{
    public const string CommandName = "verify-schema";

    /// <summary>Exit code when the report ran but at least one check found drift.</summary>
    public const int DriftFoundExitCode = 2;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve appsettings from the install directory (R-6), not the CWD, so the packaged
            // `ClinicManagement.API.exe verify-schema` works from any working directory.
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            // Gated on having a database, not on the deployment profile (M3): this is the only gate a schema
            // change has anywhere in the product, so it must run wherever the schema lives — including against
            // the hosted database, over `docker exec`. See MaintenanceDatabase.
            if (!MaintenanceDatabase.HasConnectionString(configuration, "This schema-verification utility"))
            {
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            // AddInfrastructure ONLY — never AddApplication. It registers the tenant scope and a floor
            // ICurrentClinicProvider, which is what lets the declaration below actually mean something.
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // US-2: the backfill row counts are counted over every clinic. Under an unset scope the filtered ones
            // would come back 0 — indistinguishable from « the backfill covered nothing », which is exactly the
            // finding this verb exists to surface.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} verifies the schema and counts backfilled rows across every clinic");

            // Configuration is passed for the internal-certificate line alone (FR-2.6) — it names the root the
            // database and object-store hops verify against, so the report reads the deployment's real setting
            // rather than a third key of its own.
            // The Data Protection provider is passed for FR-3.1's coverage figure: which key-ring generation each
            // stored secret is encrypted under, read from a live Protect rather than from configuration.
            // The chain key is passed for FR-4.1's walk. It is the same instance the appender writes with — a
            // report resolving its own would verify a chain nothing wrote.
            var reader = new SchemaVerificationReader(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<IVendorMessagingAvailability>(),
                configuration,
                scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
                scope.ServiceProvider.GetRequiredService<IAuditChainKeyProvider>());
            var service = new SchemaVerificationService(reader);

            var report = await service.RunAsync(cancellationToken);
            var rendered = Render(report);

            Console.Write(rendered);

            var savedTo = TrySaveReport(configuration, rendered);
            if (savedTo is not null)
            {
                Console.WriteLine($"Saved to: {savedTo}");
                Console.WriteLine();
            }

            return report.HasDrift ? DriftFoundExitCode : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Schema verification failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Renders the report as plain text. Same text goes to stdout and to the saved file.</summary>
    private static string Render(SchemaVerificationReport report)
    {
        var output = new StringBuilder();
        output.AppendLine();
        output.AppendLine("=== Schema verification ===");
        output.AppendLine($"Generated : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine();

        foreach (var scope in report.Findings.GroupBy(f => f.Scope))
        {
            output.AppendLine(scope.Key);
            foreach (var finding in scope)
            {
                var marker = finding.Severity == SchemaVerificationSeverity.Drift ? "DRIFT" : "  ok ";
                output.AppendLine($"  [{marker}] {finding.Check}: {finding.Detail}");
            }

            output.AppendLine();
        }

        output.AppendLine(report.DriftCount == 0
            ? "Result: schema matches the model."
            : $"Result: {report.DriftCount} check(s) found drift. See the DRIFT lines above.");
        output.AppendLine();

        return output.ToString();
    }

    /// <summary>
    /// Best-effort save beside the backup destination so the before/after pair can be diffed. A failure here
    /// never changes the exit code — the report already went to stdout, which is the primary channel.
    /// </summary>
    private static string? TrySaveReport(IConfiguration configuration, string rendered)
    {
        try
        {
            var configured = configuration["Backup:DefaultDestination"];
            var directory = string.IsNullOrWhiteSpace(configured)
                ? LocalInstallPaths.Resolve("reports")
                : configured;

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"schema-verification-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(Could not save the report to disk: {ex.Message})");
            return null;
        }
    }
}
