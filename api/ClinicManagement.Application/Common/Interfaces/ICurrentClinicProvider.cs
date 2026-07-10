namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Lightweight, synchronous, per-request source of the caller's clinic id for the EF Core global
/// query filter (a defense-in-depth backstop). Reads the JWT <c>clinic_id</c> claim via
/// <see cref="IClinicContext"/>; returns <c>null</c> when no clinic is in scope (background jobs,
/// the <c>reset-admin-password</c> CLI, anonymous auth/setup, non-request contexts) so the filter
/// stays <b>inactive</b> (returns all rows) rather than filtering everything to empty.
///
/// This is only a backstop — the authoritative tenant check remains the per-handler DB-resolved
/// <c>User.ClinicId</c> (see <see cref="ICurrentClinicResolver"/>). It is intentionally distinct
/// from <see cref="ICurrentClinicResolver"/>: the resolver does an async DB lookup and cannot be
/// invoked from inside a synchronous EF query-filter lambda.
/// </summary>
public interface ICurrentClinicProvider
{
    Guid? ClinicId { get; }
}
