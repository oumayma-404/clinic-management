namespace ClinicManagement.Domain.Enums;

/// <summary>
/// A PC de secours problem the <b>vendor</b> is e-mailed about (<c>clinic-pc-copy</c> AC-9.2, AC-9.4) — a narrower
/// set than the admins' <see cref="RelayAlert"/>: a disk filling up or a clock that drifts is the cabinet's to fix.
///
/// <para>⚠️ Persisted as an int — append, never insert.</para>
/// </summary>
public enum RelayIncidentKind
{
    /// <summary>No heartbeat for 24 h.</summary>
    Unseen = 1,

    /// <summary>Behind for 15 min during opening hours.</summary>
    Late = 2,

    /// <summary>The copy differs from the cloud after its own repair.</summary>
    Mismatch = 3,

    /// <summary>The cloud went back in time and the copy stopped to lose nothing (AC-9.4).</summary>
    Stopped = 4,

    /// <summary>The return has been stuck for 15 min (AC-5.9, AC-9.2 « bloqué au retour »).</summary>
    ReturnStuck = 5,
}
