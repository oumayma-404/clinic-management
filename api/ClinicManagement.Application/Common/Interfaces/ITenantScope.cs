namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What the EF Core global query filter is allowed to return in this scope.
/// </summary>
public enum TenantScopeKind
{
    /// <summary>
    /// Nobody said. The filter <b>refuses</b> — every clinic-owned read comes back empty.
    /// </summary>
    Unset,

    /// <summary>One clinic's rows.</summary>
    Clinic,

    /// <summary>
    /// Every clinic's rows, because the caller declared it. The widest thing in the design.
    /// </summary>
    SystemWide
}

/// <summary>
/// The per-scope answer to « whose rows may this read see? ». Set once, by whoever knows: request middleware
/// from the DB-resolved <c>User.ClinicId</c>, a job or console verb by declaring itself cross-clinic.
///
/// <para><b>Only <see cref="TenantScopeKind.Unset"/> refuses.</b> That is what makes the query filter an
/// isolation layer rather than the fail-open backstop it was: before this, no clinic in scope meant no filter,
/// so a path that forgot to establish one read every clinic and nothing said so.</para>
///
/// <para><b>The scope is single-assignment in both directions.</b> <see cref="UseClinic"/> then
/// <see cref="UseSystemWide"/> throws rather than silently widening — a widening call is how a single-clinic
/// path quietly becomes a cross-clinic one — and narrowing is refused for the same reason: one scope, one
/// answer. A job that needs per-clinic narrowing during enumeration opens a child scope.</para>
/// </summary>
public interface ITenantScope
{
    TenantScopeKind Kind { get; }

    /// <summary>Non-null iff <see cref="Kind"/> is <see cref="TenantScopeKind.Clinic"/>.</summary>
    Guid? ClinicId { get; }

    /// <summary>Why this scope reads across clinics; null unless <see cref="Kind"/> is SystemWide.</summary>
    string? SystemWideReason { get; }

    /// <summary>
    /// Scope every clinic-owned read to <paramref name="clinicId"/>. Repeating the same id is a no-op, so a
    /// handler may restate what the middleware already established; a different one throws.
    /// </summary>
    void UseClinic(Guid clinicId);

    /// <summary>
    /// Read across every clinic, on the record. <paramref name="reason"/> is logged and required — it is the
    /// answer to « who read across clinics, and why ».
    /// </summary>
    void UseSystemWide(string reason);
}
