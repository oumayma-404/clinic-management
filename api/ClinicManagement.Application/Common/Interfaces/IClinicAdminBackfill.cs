namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Repairs existing Cloud clinics that were created before the "creator becomes admin" fix and therefore
/// have no admin (leaving every admin-gated feature unreachable). Idempotent — promotes the earliest user
/// of any clinic that currently has no active admin. Cloud-only; a no-op in Local (every Local clinic
/// already mints an admin at first-run).
/// </summary>
public interface IClinicAdminBackfill
{
    Task BackfillAsync(CancellationToken cancellationToken = default);
}
