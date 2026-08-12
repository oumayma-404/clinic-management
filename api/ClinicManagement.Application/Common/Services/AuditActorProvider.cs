using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Resolves the audit actor once per scope: the acting <b>console</b> account if there is one, else the JWT's
/// clinic user, else whatever process declared itself through <see cref="RunAs"/>, else
/// <see cref="AuditActor.Unknown"/>.
///
/// <para>The resolution is <b>cached for the scope</b> on first read, and that is load-bearing rather than an
/// optimisation. The interceptor reads the actor while the save is being prepared, and a request that changes the
/// caller's own account mid-flight (a role change bumping <c>TokenVersion</c>, a password change) must not have
/// its later rows attributed differently from its earlier ones — one operation, one actor.</para>
///
/// <para>⚠️ <b>The console is asked <i>before</i> the clinic context, and the order is the whole point</b>
/// (<c>platform-console</c> Part 1). <see cref="IClinicContext.GetUserId"/> returns the token's <c>sub</c>
/// whatever issued it, so with the two the other way round a console write would be recorded as a bare GUID —
/// indistinguishable from a clinic user in that cabinet's journal (AC-4.7 false) and invisible to the counter
/// pass's <c>console|</c> exclusion, which would then match nothing (AC-2.2/EC-10 false). Both failures are
/// silent, which is why the seam lands with the principal rather than with the first console write.
/// <see cref="IPlatformSessionContext"/> answers null for every clinic request, so the clinic path below is
/// untouched.</para>
///
/// <para>It lives in Application, next to <c>ClinicContext</c> and <c>CurrentClinicProvider</c>, because it needs
/// only those two context seams. The interceptor that consumes it is in Infrastructure (it is an EF type), which
/// is the usual direction: the seam is declared and implemented here, the EF plumbing depends on it.</para>
/// </summary>
public class AuditActorProvider : IAuditActorProvider
{
    private readonly IClinicContext _clinicContext;
    private readonly IPlatformSessionContext _platformSession;
    private string? _processName;
    private bool _restoring;
    private AuditActor? _resolved;

    public AuditActorProvider(IClinicContext clinicContext, IPlatformSessionContext platformSession)
    {
        _clinicContext = clinicContext;
        _platformSession = platformSession;
    }

    public AuditActor Current => _resolved ??= Resolve();

    public void RunAs(string processName)
    {
        // A declaration after the actor has been read would silently disagree with the rows already written, so
        // the first read wins and this becomes a no-op. In practice a job declares itself before doing anything.
        if (_resolved is not null)
        {
            return;
        }

        _processName = processName;
    }

    public void RestoringAnArchive()
    {
        // Unlike RunAs this decorates rather than replaces, so it is safe after the actor has been read — and it
        // has to be, since the restore declares itself once and then writes in batches.
        _restoring = true;
        _resolved = _resolved?.AsRestore();
    }

    private AuditActor Resolve()
    {
        var actor = ResolveIdentity();

        return _restoring ? actor.AsRestore() : actor;
    }

    private AuditActor ResolveIdentity()
    {
        // The console first: its principal also carries a `sub`, so asking the clinic context first would claim a
        // console write as a clinic user's. See the class remarks.
        var consoleAccountId = _platformSession.GetAccountId();
        if (consoleAccountId is not null)
        {
            return AuditActor.Console(consoleAccountId.Value, _platformSession.GetEmail());
        }

        // Then the token, always: a signed-in user's identity outranks any process name, so a helper that calls
        // RunAs while running inside somebody's request cannot claim their work.
        var userId = _clinicContext.GetUserId();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return new AuditActor(userId, _clinicContext.GetUserEmail());
        }

        return string.IsNullOrWhiteSpace(_processName)
            ? AuditActor.Unknown
            : AuditActor.Process(_processName);
    }
}
