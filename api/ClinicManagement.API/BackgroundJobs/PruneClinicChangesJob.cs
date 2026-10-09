using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The cloud's change log is trimmed (<c>clinic-pc-copy</c> D27): rows the cabinet's PC de secours already holds, older
/// than <see cref="ClinicRelay.ChangeLogKeptFor"/>, go — the row at the PC's own position stays, since the feed checks
/// its fingerprint (D12). Unpruned, the hosted table grows with every save of every cabinet that has a PC.
///
/// <para>Nothing is pruned while <see cref="ClinicRelay.ChangeLogPrunableBelow"/> says no: a cut (the return reads the
/// log after the PC's position), a stopped copy, a restore gap, an unseeded PC. A cabinet with no PC writes no log at
/// all (retirement drops it). The PC's own log is dropped after each return (D18) and is not this job's.</para>
/// </summary>
public class PruneClinicChangesJob
{
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRelayRowStore _rows;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<PruneClinicChangesJob> _logger;

    public PruneClinicChangesJob(
        IClinicRelayRepository relays,
        IClinicRelayRowStore rows,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<PruneClinicChangesJob> logger)
    {
        _relays = relays;
        _rows = rows;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public Task PruneClinicChanges() => PruneClinicChanges(DateTime.UtcNow);

    /// <summary>The pass at a given instant — the entry point the tests drive.</summary>
    public async Task PruneClinicChanges(DateTime nowUtc)
    {
        _auditActor.RunAs(nameof(PruneClinicChangesJob));
        _tenantScope.UseSystemWide("PruneClinicChangesJob trims every cabinet's change log below its PC de secours's position");

        var before = nowUtc - ClinicRelay.ChangeLogKeptFor;
        foreach (var relay in RelayWatchJob.CurrentPerClinic(await _relays.GetLiveAsync(), nowUtc))
        {
            try
            {
                var highWater = await _rows.HighWaterAsync(relay.ClinicId, CancellationToken.None);
                if (relay.ChangeLogPrunableBelow(highWater) is not { } below)
                {
                    continue;
                }

                var deleted = await _rows.PruneChangesAsync(relay.ClinicId, below, before, CancellationToken.None);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "PC de secours: {Deleted} change rows below seq {Seq} pruned for clinic {ClinicId}",
                        deleted, below, relay.ClinicId);
                }
            }
            catch (Exception ex)
            {
                // One cabinet's failure must not keep every other cabinet's log growing.
                _logger.LogError(ex, "PC de secours: change-log pruning failed for clinic {ClinicId}", relay.ClinicId);
            }
        }
    }
}
