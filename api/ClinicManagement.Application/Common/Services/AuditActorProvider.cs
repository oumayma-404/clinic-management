using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Resolves the audit actor once per scope: the JWT's user if there is one, otherwise whatever process declared
/// itself through <see cref="RunAs"/>, otherwise <see cref="AuditActor.Unknown"/>.
///
/// <para>The resolution is <b>cached for the scope</b> on first read, and that is load-bearing rather than an
/// optimisation. The interceptor reads the actor while the save is being prepared, and a request that changes the
/// caller's own account mid-flight (a role change bumping <c>TokenVersion</c>, a password change) must not have
/// its later rows attributed differently from its earlier ones — one operation, one actor.</para>
///
/// <para>It lives in Application, next to <c>ClinicContext</c> and <c>CurrentClinicProvider</c>, because it needs
/// only <see cref="IClinicContext"/>. The interceptor that consumes it is in Infrastructure (it is an EF type),
/// which is the usual direction: the seam is declared and implemented here, the EF plumbing depends on it.</para>
/// </summary>
public class AuditActorProvider : IAuditActorProvider
{
    private readonly IClinicContext _clinicContext;
    private string? _processName;
    private AuditActor? _resolved;

    public AuditActorProvider(IClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
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

    private AuditActor Resolve()
    {
        // The token first, always: a signed-in user's identity outranks any process name, so a helper that calls
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
