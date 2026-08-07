namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Lightweight, synchronous view of the scope's <see cref="ITenantScope"/> for the EF Core global query
/// filter. Two members, because the filter must distinguish three states from what a query can read
/// synchronously: <see cref="IsSystemWide"/> switches it off, and <see cref="ClinicId"/> is the clinic to
/// match — <c>null</c> meaning « nobody said », which the filter treats as <b>no rows</b>.
///
/// <para>It is intentionally distinct from <see cref="ICurrentClinicResolver"/>: the resolver does an async DB
/// lookup and cannot be invoked from inside a synchronous query-filter lambda. The scope is set once per
/// request from that resolver's source (async middleware), which is what lets the filter stay sync.</para>
///
/// <para>The per-handler DB-resolved <see cref="ICurrentClinicResolver"/> check remains the authoritative
/// tenant guard — and the only one for the seven clinical tables that carry no <c>ClinicId</c> at all. This is
/// the second layer, not a replacement for the first.</para>
/// </summary>
public interface ICurrentClinicProvider
{
    /// <summary>True when the scope declared itself cross-clinic; the filter then returns every row.</summary>
    bool IsSystemWide { get; }

    /// <summary>The clinic to filter to, or <c>null</c> when no scope was set.</summary>
    Guid? ClinicId { get; }
}
