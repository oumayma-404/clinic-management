using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Reads the current clinic id from the JWT claim (via <see cref="IClinicContext"/>) for the EF Core
/// global query filter. See <see cref="ICurrentClinicProvider"/> for why this is a backstop only.
///
/// <para><b>Invariant (must hold for the backstop to be correct):</b> the JWT <c>clinic_id</c> claim
/// this reads must always equal the authoritative DB-resolved <c>User.ClinicId</c> that handlers use
/// via <see cref="ICurrentClinicResolver"/>. Auth issuance (Local <c>LoginCommand</c> / Cloud Auth0
/// <c>app_metadata</c>) is responsible for keeping the claim in sync with the DB; a token minted before
/// a user's clinic changes can diverge until it is refreshed.</para>
///
/// <para><b>Fail-open is deliberate (spec §1):</b> when no clinic is in scope the filter is inactive
/// (returns all rows) — required so background jobs, the <c>reset-admin-password</c> CLI, and anonymous
/// auth/setup keep working (AC-3). It is therefore <b>not</b> a substitute for the per-handler
/// DB-resolved <see cref="ICurrentClinicResolver"/> check, which is the authoritative tenant guard on
/// every request-scoped read/write. Do not lean on this filter for isolation.</para>
/// </summary>
public class CurrentClinicProvider : ICurrentClinicProvider
{
    private readonly IClinicContext _clinicContext;

    public CurrentClinicProvider(IClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
    }

    public Guid? ClinicId => _clinicContext.GetClinicId();
}
