namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// One named figure the console states before a deletion — « 12 patients », « 34 rendez-vous ».
///
/// <para>The label is French and server-side, like every other word this surface renders: the keys are CLR entity
/// names, and a client translating them would keep a second copy of a map that grows with the model.</para>
/// </summary>
public sealed record PlatformClinicDeletionTallyDto(string Label, long Rows);

/// <summary>
/// What deleting one cabinet would remove — the sentence the vendor reads before the confirmation unlocks
/// (<c>clinic-account-removal</c>).
///
/// <para>⚠️ <b><see cref="RowsTotal"/> is derived from the whole plan while <see cref="Tallies"/> is a curated
/// selection of it</b>, and that asymmetry is the point: the named figures are what a human recognises, and the
/// total is what stays true if one of those names ever stops matching the model. A preview that could only show
/// the curated rows would silently understate a deletion the day an entity was renamed.</para>
///
/// <para><c>FreedEmails</c> is every password-backed account of the cabinet: those addresses become available
/// again, which is the whole reason this feature exists.</para>
/// </summary>
public sealed record PlatformClinicDeletionPreviewDto(
    Guid ClinicId,
    string ClinicName,
    IReadOnlyList<string> FreedEmails,
    IReadOnlyList<PlatformClinicDeletionTallyDto> Tallies,
    long RowsTotal,
    int FileCount,
    long FileBytes);

/// <summary>
/// What a completed deletion removed. Same shape as the preview, stated in the past.
///
/// <para><c>AddressRowsCleared</c> is the pending-signup and password-reset rows that named the freed addresses:
/// what the vendor is being told is « nothing is still holding these », and which table held a row is not their
/// question.</para>
/// </summary>
public sealed record PlatformClinicDeletedDto(
    Guid ClinicId,
    string ClinicName,
    IReadOnlyList<string> FreedEmails,
    IReadOnlyList<PlatformClinicDeletionTallyDto> Tallies,
    long RowsDeleted,
    int FilesDeleted,
    int AddressRowsCleared);
