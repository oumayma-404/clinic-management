using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Server-side console utility that reconciles the money the app stores in two places, across every clinic,
/// and prints every figure a data migration must not move.
///
///   ClinicManagement.API.exe reconcile-money [months-of-history]
///
/// Runs on the clinic SERVER PC without starting the web server, so it can be used on a stopped app — which is
/// the point: run it BEFORE a migration, keep the output, run it AFTER, and diff. It is strictly read-only.
///
/// Exit codes differ from the other verbs deliberately, because "could not run" and "ran and found a problem"
/// must not look the same to an operator or a script:
///   0 = every check passed
///   1 = the utility could not run (wrong mode, bad config, unreachable database)
///   2 = the utility ran and found at least one mismatch
/// </summary>
public static class ReconcileMoneyCommand
{
    public const string CommandName = "reconcile-money";

    /// <summary>Exit code when the report ran but at least one check found drift.</summary>
    public const int DriftFoundExitCode = 2;

    private const int DefaultMonthsOfHistory = 24;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        // args[0] is the command name; args[1] (optional) overrides the months of history.
        var monthsOfHistory = DefaultMonthsOfHistory;
        if (args.Length > 1 && (!int.TryParse(args[1], out monthsOfHistory) || monthsOfHistory < 1))
        {
            Console.Error.WriteLine($"'{args[1]}' is not a valid number of months. Pass a positive whole number.");
            return 1;
        }

        try
        {
            // Resolve appsettings from the install directory (R-6), not the CWD, so the packaged
            // `ClinicManagement.API.exe reconcile-money` works from any working directory.
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            var profile = DeploymentProfile.Resolve(configuration);
            if (!profile.HasLocalDbTooling)
            {
                Console.Error.WriteLine(
                    "This reconciliation utility needs a direct database connection " +
                    $"(deployment profile: {profile.Kind}).");
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            // AddInfrastructure ONLY — never AddApplication. It registers the tenant scope and a floor
            // ICurrentClinicProvider, which is what lets the declaration below actually mean something.
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // US-2: a per-clinic reconciliation over every clinic's two payment ledgers. The clinic query filters
            // refuse an unset scope, so an undeclared run would reconcile an empty database and report it clean —
            // the worst possible answer from a verb whose only job is to find drift.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} reconciles every clinic's money ledgers");

            var reader = new MoneyReconciliationReader(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            var service = new MoneyReconciliationService(reader);

            var report = await service.RunAsync(monthsOfHistory, cancellationToken);
            var rendered = Render(report, monthsOfHistory);

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
            Console.Error.WriteLine($"Money reconciliation failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Renders the report as a plain-text table. Same text goes to stdout and to the saved file.</summary>
    private static string Render(MoneyReconciliationReport report, int monthsOfHistory)
    {
        var output = new StringBuilder();
        output.AppendLine();
        output.AppendLine("=== Money reconciliation ===");
        output.AppendLine($"Generated : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine($"History   : {monthsOfHistory} month(s)");
        output.AppendLine();

        output.AppendLine("--- Checks ---");
        foreach (var scope in report.Findings.GroupBy(f => f.Scope))
        {
            output.AppendLine();
            output.AppendLine(scope.Key);
            foreach (var finding in scope)
            {
                var marker = finding.Severity == MoneyReconciliationSeverity.Drift ? "DRIFT" : "  ok ";
                output.AppendLine($"  [{marker}] {finding.Check}: {finding.Detail}");
            }
        }

        output.AppendLine();
        output.AppendLine("--- Monthly « encaissé » baseline (must be identical after any money migration) ---");
        if (report.MonthlyBaseline.Count == 0)
        {
            output.AppendLine("  (no collected cash in the window)");
        }
        else
        {
            output.AppendLine($"  {"Clinic",-28} {"Month",-8} {"Invoices",14} {"Échéances",14} {"Total",14}");
            foreach (var line in report.MonthlyBaseline)
            {
                output.AppendLine(
                    $"  {Truncate(line.Clinic, 28),-28} {line.Year:0000}-{line.Month:00}  "
                    + $"{line.InvoiceCollected,13:0.000} {line.InstallmentCollected,13:0.000} {line.Total,13:0.000}");
            }
        }

        var driftCount = report.Findings.Count(f => f.Severity == MoneyReconciliationSeverity.Drift);
        output.AppendLine();
        output.AppendLine(driftCount == 0
            ? "Result: no drift detected."
            : $"Result: {driftCount} check(s) found drift. See the DRIFT lines above.");
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
            var path = Path.Combine(directory, $"money-reconciliation-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(Could not save the report to disk: {ex.Message})");
            return null;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
