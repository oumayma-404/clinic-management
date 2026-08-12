using System.Globalization;
using System.Text;
using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Read-only report of where every cabinet stands on its WhatsApp reminder forfait
/// (<c>vendor-whatsapp-messaging-quota</c> AC-8.6, AC-9.4), and — for one cabinet — the allocations behind that figure.
///
/// <code>
///   ClinicManagement.API.exe messaging-report
///   ClinicManagement.API.exe messaging-report --month 2026-07          # a CLOSED month
///   ClinicManagement.API.exe messaging-report --clinic &lt;id|email&gt; [--month 2026-07]
/// </code>
///
/// <para>Exit codes are <c>reconcile-money</c>'s and <c>verify-schema</c>'s, so it can be scheduled beside them and read
/// by the same script: <b>0</b> nothing to act on · <b>1</b> could not run · <b>2</b> ran and found something. AC-9.4's
/// three finding kinds are kept apart because the vendor's action differs for each — « épuisé » is « recharge-le »,
/// « aucun forfait » and « non mesuré » are « notre comptabilité est cassée », and a template no longer <c>UTILITY</c> is
/// « notre coût par message a bougé ».</para>
///
/// <para><b>⚠️ <c>--month</c> is what makes this answer « qu'avons-nous facturé en juillet ? » after July has ended</b>,
/// which is when the vendor reconciles against Meta's bill. It is free because the fold takes the month as a parameter
/// and reads no clock (FR-2) — a current-month-only report would have made the one question this verb exists for
/// unanswerable.</para>
///
/// <para>It never mutates, and it is the recovery path for reading a cabinet's allocation ids when the vendor console is
/// unreachable — <c>messaging-cancel</c> needs one and only this prints them.</para>
/// </summary>
public static class MessagingReportCommand
{
    public const string CommandName = "messaging-report";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "This WhatsApp reminder report"))
            {
                return SubscriptionVerbs.Failed;
            }

            if (!MessagingVerbs.TryReadMonth(args, out var monthKey))
            {
                return SubscriptionVerbs.Failed;
            }

            var monthLabel = ClinicClock.MonthLabelFr(monthKey);

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            // US-2: the report reads every cabinet's forfait — including in its single-cabinet mode, which filters the
            // same deployment-wide projection. Both messaging tables carry a clinic query filter that refuses an unset
            // scope, so an undeclared run would report an empty deployment as « rien à signaler » — the worst possible
            // answer from a verb whose only job is to find cabinets needing attention.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} reports every cabinet's WhatsApp reminder allowance");

            var service = new MessagingReportService(
                scope.ServiceProvider.GetRequiredService<IMessagingAllowanceRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicReminderSettingsRepository>());

            var cabinetSelected =
                !string.IsNullOrWhiteSpace(SubscriptionVerbs.ReadOption(args, "--clinic"))
                || !string.IsNullOrWhiteSpace(SubscriptionVerbs.ReadOption(args, "--email"));

            if (cabinetSelected)
            {
                var clinicId = await SubscriptionVerbs.ResolveCabinetAsync(
                    args, scope.ServiceProvider, cancellationToken);

                if (clinicId is null)
                {
                    return SubscriptionVerbs.Failed;
                }

                var cabinet = await service.RunForCabinetAsync(
                    clinicId.Value, monthKey, monthLabel, cancellationToken);

                if (cabinet is null)
                {
                    Console.Error.WriteLine($"Aucun cabinet {clinicId} dans ce déploiement.");
                    return SubscriptionVerbs.Failed;
                }

                Console.Write(RenderCabinet(cabinet));

                // The verdict comes from the service, which buckets this cabinet with the same rule the
                // deployment-wide run uses. A second implementation here agreed only by coincidence and would be
                // outside the test project's reach — and an exit code that quietly stops alarming reads as a clean run.
                return cabinet.NeedsAttention ? MessagingVerbs.FindingsExitCode : SubscriptionVerbs.Success;
            }

            var report = await service.RunAsync(monthKey, monthLabel, cancellationToken);

            Console.Write(Render(report));
            return report.NeedsAttention ? MessagingVerbs.FindingsExitCode : SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Messaging report failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }

    private static string Render(MessagingReport report)
    {
        var output = new StringBuilder();
        output.AppendLine();
        output.AppendLine("=== WhatsApp reminder allowances ===");
        output.AppendLine($"Generated : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine($"Month     : {report.MonthKey}  ({report.MonthLabel}, Tunisia UTC+1)");
        output.AppendLine($"Cabinets  : {report.TotalCabinets}");
        output.AppendLine();

        // Ordered by what the vendor does about it, not by size: the first group is money the practice is losing right
        // now, the middle two are our own bookkeeping, the last is our cost per message.
        AppendGroup(output, "Exhausted — reminders are being held", report.Exhausted);
        AppendGroup(output, "No allowance record — our bookkeeping is wrong", report.NoAllowance);
        AppendGroup(output, "Not measured — the daily pass has not written this month's row", report.Unmeasured);
        AppendGroup(output, "Template no longer UTILITY — our cost per message has moved", report.TemplateNotUtility);

        output.AppendLine($"Nothing to act on : {report.Healthy.Count} cabinet(s)");
        output.AppendLine();

        if (!report.NeedsAttention)
        {
            output.AppendLine("No findings.");
            output.AppendLine();
            return output.ToString();
        }

        output.AppendLine("Next steps:");
        output.AppendLine("  · Exhausted        → messaging-grant --clinic <id> --top-up <n> --month "
            + report.MonthKey);
        output.AppendLine("  · No allowance     → messaging-grant --clinic <id> --per-month <n>");
        output.AppendLine("  · Not measured     → check the daily MessagingAllowanceJob ran; verify-schema's");
        output.AppendLine("                       messaging-month-covers-every-clinic reports the same gap");
        output.AppendLine("  · Template moved   → resubmit it as UTILITY with Meta; the practice is not told and its");
        output.AppendLine("                       reminders keep going out (FR-7b)");
        output.AppendLine();
        return output.ToString();
    }

    private static void AppendGroup(
        StringBuilder output, string title, IReadOnlyList<MessagingReportLine> lines)
    {
        output.AppendLine($"-- {title}: {lines.Count}");

        foreach (var line in lines)
        {
            output.AppendLine(
                $"   {line.ClinicId}  {Truncate(line.ClinicName, 28),-28}  "
                + $"forfait {MessagingVerbs.Allowance(line.Allowance),13}  "
                + $"envoyés {MessagingVerbs.Count(line.Consumed),8}  "
                + $"reste {MessagingVerbs.Count(line.Remaining),8}  {line.SenderStateLabel}");

            // ⚠️ Printed only when the two disagree, and it is the single most useful line this verb can emit: the
            // snapshot is what the outbox gate enforces on and the fold is what the ledger says, so a drift means a
            // practice is being held to a figure nobody granted it. verify-schema catches it too; this is what a vendor
            // sees while the cabinet is on the telephone.
            if (line.StoredAllowance is { } stored && line.Allowance is { } folded && stored != folded)
            {
                output.AppendLine(
                    $"       ⚠️  stored snapshot {stored} disagrees with the ledger's {folded} — run verify-schema");
            }
        }

        output.AppendLine();
    }

    private static string RenderCabinet(MessagingCabinetReport report)
    {
        var cabinet = report.Cabinet;
        var output = new StringBuilder();

        output.AppendLine();
        output.AppendLine("=== WhatsApp reminder allowance — one cabinet ===");
        output.AppendLine($"Generated : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine($"Month     : {report.MonthKey}  ({report.MonthLabel}, Tunisia UTC+1)");
        output.AppendLine();
        output.AppendLine($"  Cabinet         : {cabinet.ClinicName}");
        output.AppendLine($"  Clinic id       : {cabinet.ClinicId}");
        output.AppendLine($"  Standing forfait: {MessagingVerbs.Allowance(cabinet.StandingAllowance)} / month");
        output.AppendLine($"  This month      : {MessagingVerbs.Allowance(cabinet.Allowance)} allowed, "
            + $"{MessagingVerbs.Count(cabinet.Consumed)} sent, {MessagingVerbs.Count(cabinet.Remaining)} left");
        output.AppendLine($"  Sender          : {cabinet.SenderStateLabel}");
        output.AppendLine($"  Verdict         : {cabinet.Bucket}");
        output.AppendLine();

        output.AppendLine($"-- Allocations: {report.Ledger.Count}  (oldest first)");

        if (report.Ledger.Count == 0)
        {
            // AC-4.3's state said out loud rather than shown as an empty table: every cabinet is provisioned with an
            // opening allocation (FR-3), so an empty ledger is a fault and not a young practice.
            output.AppendLine("   (none — this cabinet has no allocation on record at all, which should not happen:");
            output.AppendLine("    every cabinet is provisioned with one. Record a standing forfait to restore it.)");
        }

        foreach (var entry in report.Ledger)
        {
            var struck = entry.IsCancelled ? "  [ANNULÉE]" : string.Empty;
            output.AppendLine(
                $"   {entry.EntryId}  {entry.EffectiveMonth}  {entry.KindLabel,-20}  "
                + $"{entry.Messages,8} rappels  "
                + $"{(entry.Amount is { } a ? a.ToString("0.000", CultureInfo.InvariantCulture) + " DT" : "offert"),12}"
                + struck);

            if (entry.IsCancelled && !string.IsNullOrWhiteSpace(entry.CancelReason))
            {
                output.AppendLine($"       motif : {entry.CancelReason}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Reference))
            {
                output.AppendLine($"       réf.  : {entry.Reference}");
            }
        }

        output.AppendLine();
        output.AppendLine("Cancel a mistaken allocation with:");
        output.AppendLine($"  messaging-cancel --clinic {cabinet.ClinicId} --entry <id> --reason \"<motif>\"");
        output.AppendLine();
        return output.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
