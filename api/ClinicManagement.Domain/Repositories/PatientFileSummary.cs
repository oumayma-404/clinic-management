namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One patient reduced to « whose files are these, and how many are there » — the row behind the « Fichiers »
/// directory, which is the one screen whose subject is a patient's <i>drawer</i> rather than the patient.
///
/// <para>A projection and not a <see cref="Entities.Patient"/> for the reason every other projection in this
/// file is one: the directory needs three identity fields and three aggregates per row, and materialising whole
/// aggregates — with their flags and both history collections — to render a name and a count is the full-scan
/// this codebase has already paid for twice.</para>
///
/// <para>⚠️ <b>The three aggregates are computed by the database, per patient, in the same query.</b> The
/// tempting alternative is to page the patients and then ask a second read for « the counts of these 25 ids »,
/// on <c>AppointmentInvoiceLinks</c>' precedent. It is one round trip cheaper and it cannot sort or filter:
/// « les patients qui ont des fichiers » and « le plus de fichiers d'abord » are decisions taken <b>before</b> a
/// page is cut, and a count annotated onto an already-cut page can only ever narrow the 25 rows in hand —
/// which is the console's AC-2.4a lesson and this repo's own list-pagination trap (b).</para>
///
/// <para><paramref name="TotalBytes"/> is 0 for a patient with no files, never null: « no files » and « files
/// with no bytes » are not distinguishable facts here, and <paramref name="FileCount"/> already answers the
/// question. <paramref name="LastUploadedAtUtc"/> <i>is</i> nullable, because « rien n'a jamais été déposé »
/// is a fact the card states in words rather than as a date it would have to invent.</para>
/// </summary>
public sealed record PatientFileSummary(
    Guid PatientId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    int FileCount,
    long TotalBytes,
    DateTime? LastUploadedAtUtc);

/// <summary>
/// How the « Fichiers » directory is ordered. An enum rather than a free-text column name so an unknown value is
/// a compile error here and a clamp at the edge, never a string interpolated into an <c>ORDER BY</c>.
/// </summary>
public enum PatientFileSummarySort
{
    /// <summary>Alphabetical by surname — the default, because a directory is something you look a name up in.</summary>
    Name = 0,

    /// <summary>Fullest drawer first. « Qui a le plus d'imagerie ? », and the fastest way to the heavy records.</summary>
    MostFiles = 1,

    /// <summary>
    /// Most recently added file first. Patients with no file at all sort <b>last</b> rather than first: a
    /// descending sort over a nullable column puts NULLs at the top in PostgreSQL, which would head a
    /// « derniers ajouts » list with the patients who have never had one.
    /// </summary>
    RecentUpload = 2,
}
