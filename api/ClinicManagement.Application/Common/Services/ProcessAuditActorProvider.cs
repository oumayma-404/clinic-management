using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// The audit actor for a container that has <b>no request and no claims</b> — a console verb.
///
/// <para><b>Why this exists at all.</b> The console verbs (<c>reset-admin-password</c>, <c>verify-schema</c>,
/// <c>reconcile-money</c>, …) build their service collection from <c>AddInfrastructure</c> <em>only</em>, never
/// <c>AddApplication</c> — deliberately, so no <c>ICurrentClinicProvider</c> is registered and the global clinic
/// query filters stay inactive while they read across every clinic. But the audit interceptor is wired into the
/// <c>DbContext</c> by <c>AddInfrastructure</c>, so without a floor implementation the first verb that writes
/// anything would fail to resolve its own database context. Infrastructure registers this with <c>TryAdd</c>, which
/// means the real, claims-reading <see cref="AuditActorProvider"/> wins in the API (registered earlier by
/// <c>AddApplication</c>) and this one only ever surfaces where there genuinely is nobody to name.</para>
///
/// <para>It still honours <see cref="RunAs"/>, which is the point: <c>reset-admin-password</c> declares itself and
/// the ledger records « Tâche automatique (reset-admin-password) » against the account it changed — an offline
/// password reset is precisely the kind of event an owner should be able to find afterwards.</para>
/// </summary>
public class ProcessAuditActorProvider : IAuditActorProvider
{
    private AuditActor _actor = AuditActor.Unknown;
    private bool _restoring;
    private bool _read;

    public AuditActor Current
    {
        get
        {
            _read = true;
            return _restoring ? _actor.AsRestore() : _actor;
        }
    }

    public void RunAs(string processName)
    {
        // Same first-read-wins rule as AuditActorProvider: rows already written must not be contradicted.
        if (_read)
        {
            return;
        }

        _actor = AuditActor.Process(processName);
    }

    /// <inheritdoc />
    /// <remarks>Unreachable in a console/job host — nothing signs in there — but implemented rather than thrown:
    /// the interface is one contract and a partial implementation is a landmine for the next caller.</remarks>
    public void AuthenticatedAs(string userId, string? email)
    {
        if (_read || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        _actor = new AuditActor(userId, email);
    }

    /// <summary>
    /// Decorates rather than replaces, so — unlike <see cref="RunAs"/> — it is honoured after the first read too.
    ///
    /// <para>⚠️ The flag is held <b>beside</b> the identity rather than folded into it, mirroring
    /// <see cref="AuditActorProvider"/>. Stored in <c>_actor</c>, a later <c>RunAs</c> — permitted whenever the
    /// actor has not been read — overwrote the decoration with a bare process name and the restore mark was lost
    /// silently. Two implementations of one interface disagreeing about whether a declared restore survives is
    /// the divergence shape this repository names as its dominant defect.</para>
    /// </summary>
    public void RestoringAnArchive() => _restoring = true;
}
