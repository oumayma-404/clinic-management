using System.Globalization;
using System.Text;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Read-only report of where every cabinet of the deployment stands (AC-5.9), and — for one cabinet — the ledger
/// behind its date.
///
/// <code>
///   ClinicManagement.API.exe subscription-report [--within 7]
///   ClinicManagement.API.exe subscription-report --clinic &lt;id|email&gt;
/// </code>
///
/// <para>Exit codes are <c>reconcile-money</c>'s and <c>verify-schema</c>'s, so it can be scheduled beside them and
/// read by the same script: <b>0</b> nothing to act on · <b>1</b> could not run · <b>2</b> ran and found cabinets
/// expiring, expired, or with no entitlement at all. That last group is FR-13's failure state and counts as a
/// finding; a <b>suspended</b> cabinet is listed but does not, because suspension is a decision the vendor already
/// made and a safety net that always alarms is one nobody reads.</para>
///
/// <para>It never mutates, and it is the recovery path for reading a cabinet's period ids when the vendor console is
/// unreachable — <c>subscription-cancel</c> needs one and only this prints them.</para>
/// </summary>
public static class SubscriptionReportCommand
{
    public const string CommandName = "subscription-report";

    /// <summary>Exit code when the report ran and found cabinets to act on.</summary>
    public const int FindingsExitCode = 2;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "This subscription report"))
            {
                return SubscriptionVerbs.Failed;
            }

            if (!SubscriptionVerbs.TryReadPositiveInt(args, "--within", out var within))
            {
                return SubscriptionVerbs.Failed;
            }

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            // US-2: the report reads every cabinet's entitlement — including in its single-cabinet mode, which
            // filters the same deployment-wide projection. The clinic query filters refuse an unset scope, so an
            // undeclared run would report an empty deployment as « rien à signaler », which is the worst possible
            // answer from a verb whose only job is to find cabinets needing attention.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} reports every cabinet's entitlement");

            var service = new SubscriptionReportService(
                scope.ServiceProvider.GetRequiredService<IClinicSubscriptionRepository>());

            var today = ClinicClock.ClinicToday();
            var cabinetSelected =
                !string.IsNullOrWhiteSpace(ProvisionClinicCommand.ReadOption(args, "--clinic"))
                || !string.IsNullOrWhiteSpace(ProvisionClinicCommand.ReadOption(args, "--email"));

            if (cabinetSelected)
            {
                var clinicId = await SubscriptionVerbs.ResolveCabinetAsync(
                    args, scope.ServiceProvider, cancellationToken);

                if (clinicId is null)
                {
                    return SubscriptionVerbs.Failed;
                }

                var cabinet = await service.RunForCabinetAsync(clinicId.Value, today, cancellationToken);
                if (cabinet is null)
                {
                    Console.Error.WriteLine($"Aucun cabinet {clinicId} dans ce déploiement.");
                    return SubscriptionVerbs.Failed;
                }

                Console.Write(RenderCabinet(cabinet, today));
                return NeedsAttention(cabinet.Cabinet, within ?? SubscriptionReportService.DefaultWithinDays)
                    ? FindingsExitCode
                    : SubscriptionVerbs.Success;
            }

            var report = await service.RunAsync(
                today, within ?? SubscriptionReportService.DefaultWithinDays, cancellationToken);

            Console.Write(Render(report));
            return report.NeedsAttention ? FindingsExitCode : SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Subscription report failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }

    /// <summary>The single-cabinet mirror of <see cref="SubscriptionReport.NeedsAttention"/> — same three groups.</summary>
    private static bool NeedsAttention(SubscriptionReportLine line, int withinDays) =>
        line.State is null
        || !line.AllowsWrites && line.State != Domain.Enums.SubscriptionState.Suspended
        || line.DaysRemaining is { } days && days <= withinDays;

    private static string Render(SubscriptionReport report)
    {
        var output = new StringBuilder();
        output.AppendLine();
        output.AppendLine("=== Cabinet subscriptions ===");
        output.AppendLine($"Generated   : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine($"Clinic today: {SubscriptionVerbs.Day(report.ClinicToday)}  (Tunisia, UTC+1)");
        output.AppendLine($"Cabinets    : {report.TotalCabinets}");
        output.AppendLine();

        AppendGroup(output, $"Expiring within {report.WithinDays} day(s)", report.Expiring);
        AppendGroup(output, "Expired", report.Expired);
        AppendGroup(output, "No entitlement at all (defect — see verify-schema)", report.WithoutEntitlement);
        AppendGroup(output, "Suspended (informational)", report.Suspended);
        AppendGroup(output, "Nothing to do", report.Healthy);

        output.AppendLine();
        output.AppendLine(report.NeedsAttention
            ? $"Result: {report.Expiring.Count} expiring, {report.Expired.Count} expired, "
              + $"{report.WithoutEntitlement.Count} without an entitlement."
            : "Result: no cabinet needs attention.");
        output.AppendLine();

        return output.ToString();
    }

    private static void AppendGroup(
        StringBuilder output, string title, IReadOnlyList<SubscriptionReportLine> lines)
    {
        output.AppendLine($"--- {title}: {lines.Count} ---");

        if (lines.Count == 0)
        {
            output.AppendLine("  (none)");
            output.AppendLine();
            return;
        }

        output.AppendLine($"  {"Cabinet",-32} {"État",-14} {"Fin",-12} {"Reste",6}  Forfait");
        foreach (var line in lines)
        {
            var remaining = line.DaysRemaining is { } days
                ? days.ToString(CultureInfo.InvariantCulture)
                : "—";

            output.AppendLine(
                $"  {Truncate(line.ClinicName, 32),-32} {line.StateLabel,-14} "
                + $"{SubscriptionVerbs.Day(line.EndsOn),-12} {remaining,6}  {line.PlanLabel ?? "—"}");

            if (!string.IsNullOrWhiteSpace(line.SuspensionReason))
            {
                output.AppendLine($"      motif : {line.SuspensionReason}");
            }
        }

        output.AppendLine();
    }

    private static string RenderCabinet(SubscriptionCabinetReport cabinet, DateTime today)
    {
        var line = cabinet.Cabinet;
        var output = new StringBuilder();

        output.AppendLine();
        output.AppendLine($"=== {line.ClinicName} ===");
        output.AppendLine($"Clinic id   : {line.ClinicId}");
        output.AppendLine($"Clinic today: {SubscriptionVerbs.Day(today)}  (Tunisia, UTC+1)");
        output.AppendLine($"État        : {line.StateLabel}");
        output.AppendLine($"Fin         : {SubscriptionVerbs.Day(line.EndsOn)}");
        output.AppendLine($"Reste       : {(line.DaysRemaining is { } d ? $"{d} jour(s)" : "—")}");
        output.AppendLine($"Forfait     : {line.PlanLabel ?? "—"}");
        output.AppendLine($"Écritures   : {(line.AllowsWrites ? "autorisées" : "refusées")}");

        if (!string.IsNullOrWhiteSpace(line.SuspensionReason))
        {
            output.AppendLine($"Motif       : {line.SuspensionReason}");
        }

        output.AppendLine();
        output.AppendLine("--- Journal (oldest first) ---");
        output.AppendLine("Period ids below are what subscription-cancel takes.");
        output.AppendLine();

        if (cabinet.Ledger.Count == 0)
        {
            output.AppendLine("  (no entry — this cabinet has an entitlement with an empty ledger)");
            output.AppendLine();
            return output.ToString();
        }

        foreach (var entry in cabinet.Ledger)
        {
            var covered = entry.IsCancelled
                ? "annulée"
                : $"{SubscriptionVerbs.Day(entry.FromDay)} → {SubscriptionVerbs.Day(entry.ThroughDay)}";

            output.AppendLine($"  {entry.EntryId}  {entry.KindLabel,-14} {covered}");

            if (entry.Amount is { } amount)
            {
                output.AppendLine(
                    $"      {amount.ToString("0.000", CultureInfo.InvariantCulture)} DT"
                    + $"{(entry.MethodLabel is null ? string.Empty : $" — {entry.MethodLabel}")}"
                    + $"{(entry.Reference is null ? string.Empty : $" — réf. {entry.Reference}")}");
            }

            if (entry.IsCancelled)
            {
                output.AppendLine($"      annulée : {entry.CancelReason}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Note))
            {
                output.AppendLine($"      {entry.Note}");
            }

            if (!string.IsNullOrWhiteSpace(entry.RecordedBy))
            {
                output.AppendLine($"      enregistrée par {entry.RecordedBy}");
            }
        }

        output.AppendLine();
        return output.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
