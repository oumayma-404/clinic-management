using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Drops session families whose credential lifetime has run out.
///
/// <para><b>Why this exists now and not before.</b> <c>ISessionFamilyRepository.PurgeExpiredAsync</c> shipped
/// with the families themselves and had <b>no caller anywhere</b> — a written, tested, unreachable method, which
/// is this repository's « present and inert » shape. It was harmless while every row expired within 12 hours of
/// its last use: the table stayed roughly as large as the practice's working day. « Rester connecté sur cet
/// appareil » makes a row live for <b>30 days</b> after its last rotation, so the same table now accumulates a
/// month of every device that has ever signed in, and the read behind « Mes appareils » walks it.</para>
///
/// <para>⚠️ <b>Expiry only, never age.</b> The repository's predicate is <c>ExpiresAtUtc &lt; now</c> and this
/// job supplies nothing but the instant — a live family is live precisely because a device is still using it,
/// and pruning by age would sign working users out on a schedule, which is the one thing a housekeeping job must
/// never do. An <i>ended</i> row is kept until its own credential lifetime runs out too, so a recorded replay
/// stays visible for as long as the credential that caused it could still be presented.</para>
///
/// <para>⚠️ <b>Deleting a row is not a revocation and cannot be used as one.</b> A purged family reads back as
/// « no chain to check » rather than as a replay, so removing a *live* row would quietly turn replay detection
/// off for that device instead of ending its session. Ending one is <c>SessionFamily.End</c>'s job, and it has
/// three callers: sign-out, « Mes appareils », and replay detection itself.</para>
///
/// <para>Not connectivity-gated, and registered on every deployment kind: it touches one local table and its
/// absence is a table that grows without bound on an offline LAN install exactly as on a hosted one.</para>
/// </summary>
public class SessionFamilyPurgeJob
{
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SessionFamilyPurgeJob> _logger;

    public SessionFamilyPurgeJob(
        ISessionFamilyRepository sessionFamilies,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<SessionFamilyPurgeJob> logger)
    {
        _sessionFamilies = sessionFamilies;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task PurgeExpiredSessions()
    {
        // A job carries no token, so without naming itself every row it wrote would read « Tâche automatique »
        // with no clue which one. Declared before anything is saved.
        _auditActor.RunAs(nameof(SessionFamilyPurgeJob));

        // ⚠️ `SessionFamily` carries no `ClinicId` and therefore no query filter, so this scope declaration
        // changes nothing about what the delete matches. It is here because an **undeclared** scope is what
        // `ITenantScope` refuses, and because the next person to add a clinic-filtered read to this job must
        // find the declaration already made rather than discover a clean pass over zero rows.
        _tenantScope.UseSystemWide("SessionFamilyPurgeJob drops expired session families across the install");

        var removed = await _sessionFamilies.PurgeExpiredAsync(DateTime.UtcNow);

        // Logged at Information with the count, because the useful signal is the shape over time: a number that
        // never moves means the job is running and finding nothing, which is a different fault from silence.
        _logger.LogInformation(
            "Purge des sessions expirées : {Removed} ligne(s) supprimée(s).", removed);
    }
}
