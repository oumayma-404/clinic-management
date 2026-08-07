using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Projects the scope's <see cref="ITenantScope"/> into the two synchronous values the EF Core global query
/// filter reads. Nothing more — the decision lives in the scope, and this exists only because a query filter
/// lambda cannot call an async resolver.
///
/// <para><b>It no longer reads the JWT claim, and that is the point (amendment C3′).</b> The claim was
/// tolerable while the filter was fail-open: a missing or stale <c>clinic_id</c> simply switched the backstop
/// off and the per-handler DB check returned the right rows anyway. With the filter refusing, the same
/// divergence becomes <b>zero rows and no error</b> — and in Cloud the claim is the namespaced
/// <c>https://clinic-management.com/clinic_id</c>, emitted only by an Auth0 tenant Action that does not live in
/// this repository. The scope is therefore set from the DB-resolved <c>User.ClinicId</c> instead, so the filter
/// and the handlers answer to the same source.</para>
/// </summary>
public class CurrentClinicProvider : ICurrentClinicProvider
{
    private readonly ITenantScope _scope;

    public CurrentClinicProvider(ITenantScope scope)
    {
        _scope = scope;
    }

    public bool IsSystemWide => _scope.Kind == TenantScopeKind.SystemWide;

    public Guid? ClinicId => _scope.ClinicId;
}
