namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One row of the console's access ledger, as « Journal » shows it (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>⚠️ <see cref="AccountEmail"/> and <see cref="ClinicName"/> come from the <b>row</b>, not from a join onto
/// a live account or a live cabinet: a ledger that stops naming its parties once one of them is deleted answers
/// nothing about precisely the accesses worth reviewing.</para>
/// </summary>
/// <param name="ActionLabel">The French wording of <paramref name="Action"/>, built server-side like the caisse
/// statement's labels — the enum member is a CLR name a client could only translate by keeping a second copy of
/// this map, and the copy is what would drift when Parts 4–6 add members.</param>
public record PlatformAccessEntryDto(
    Guid EntryId,
    Guid PlatformAccountId,
    string AccountEmail,
    Guid ClinicId,
    string ClinicName,
    string Action,
    string ActionLabel,
    DateTime OccurredAt);

/// <summary>
/// One page of the journal, newest first.
///
/// <para><see cref="Actors"/> is the « Compte » filter's options, derived from the rows themselves so an account
/// that has opened nothing is not offered and a deactivated one that did stays filterable.</para>
/// </summary>
public record PlatformAccessLogPageDto(
    IReadOnlyList<PlatformAccessEntryDto> Items,
    IReadOnlyList<PlatformAccessActorDto> Actors,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

/// <summary>A console account as the journal's filter offers it.</summary>
public record PlatformAccessActorDto(Guid PlatformAccountId, string AccountEmail);
