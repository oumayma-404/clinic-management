using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.API.Startup;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Runs the vendor console's activity counter pass once, now, instead of waiting for its 03:00 UTC schedule.
///
/// <code>
///   ClinicManagement.API.exe count-activity
/// </code>
///
/// <para><b>Why a verb exists for a job that already runs daily.</b> The counters are what make the portfolio
/// readable at all — before the first pass every cabinet honestly reads « jamais mesuré » (EC-15), which is correct
/// and useless. Three situations need it on demand and had no answer: a **freshly deployed** console, where waiting
/// until the next night is the whole first impression; a deployment whose pass has been **failing** for some
/// cabinets, where the fix needs verifying immediately rather than tomorrow; and **local development**, where the
/// job would otherwise never have run against real data at all.</para>
///
/// <para>⚠️ <b>It resolves and calls the job, and re-implements nothing.</b> Every counter rule — the exclusion of
/// <c>job|</c> and <c>console|</c> actors, the clinic-local day bucketing, the whole-30-day rewrite, the money read
/// going through la caisse's own predicates — lives in <see cref="ClinicActivityCounterJob"/>. A verb with its own
/// copy of the pass is this repository's dominant defect shape, and the figures would drift silently: the console
/// would show one set of numbers after a nightly run and another after a manual one.</para>
///
/// <para>⚠️ <b>The tenant scope is declared by the job, not here</b>, and deliberately: <c>ITenantScope</c> is
/// single-assignment, so declaring it in both would throw. The job's own <c>UseSystemWide</c> is the first thing it
/// does, which is also what makes it correct when Hangfire invokes it.</para>
///
/// <para>Gated on a configured connection string like its read-only siblings (amendment M3) — it runs no PostgreSQL
/// binary — and not on a deployment capability: the pass is registered unconditionally in every profile, because the
/// counters are history and a deployment that switches the console on later must not open it to a blank portfolio
/// with nothing to backfill from.</para>
/// </summary>
public static class CountActivityCommand
{
    public const string CommandName = "count-activity";

    /// <summary>Returns a process exit code: 0 on success, 1 on any failure.</summary>
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Counting cabinet activity"))
            {
                return SubscriptionVerbs.Failed;
            }

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            var job = ActivatorUtilities.CreateInstance<ClinicActivityCounterJob>(scope.ServiceProvider);
            await job.CountClinicActivity();

            // Read the result back rather than reporting « done »: a pass that measured nothing looks identical to a
            // successful one from the outside, and that is exactly EC-15's failure — which is what this verb is for.
            var activity = scope.ServiceProvider.GetRequiredService<IClinicActivityRepository>();
            var snapshots = await activity.GetAllSnapshotsAsync(cancellationToken);

            Console.WriteLine($"Activity counted. {snapshots.Count} cabinet(s) now have a snapshot.");
            if (snapshots.Count == 0)
            {
                Console.WriteLine(
                    "No cabinet was measured. Either the deployment has no clinics, or every cabinet failed — "
                    + "check the log for « Activity counting failed for clinic ».");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Counting cabinet activity failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }
}
