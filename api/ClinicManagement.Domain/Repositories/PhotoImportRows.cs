using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// A camera shot this cabinet has already filed, and where — the « déjà importées » line of an import. Also which PC
/// keeps its full-size original (null when none reported one), so the Windows app can keep the original of a photo
/// whose filing answer it never received.
/// </summary>
public sealed record ImportedCaptureRow(
    string CaptureKey, Guid FileId, Guid PatientId, string PatientName, string? OriginalHeldBy);

/// <summary>
/// One booked visit, as much of it as matching a burst of photos to a patient needs. Times are UTC; the reader that
/// serves the browser turns them into clinic wall-clock.
/// </summary>
public sealed record PhotoImportVisitRow(
    Guid AppointmentId,
    Guid PatientId,
    string PatientName,
    Guid? DoctorId,
    DateTime StartUtc,
    TimeSpan Duration,
    int? LateArrivalMinutes,
    AppointmentStatus Status);

/// <summary>
/// When a fiche de soins was first saved — the one timestamp in the product that a human produced during or right
/// after a visit, which is why it anchors the matching on a day running late.
/// </summary>
public sealed record FicheSaveRow(
    Guid DentalRecordId,
    Guid PatientId,
    string PatientName,
    Guid? AppointmentId,
    Guid? DoctorId,
    DateTime SavedAtUtc);

/// <summary>How many camera originals one PC says it holds, and how many bytes — what its copy is checked against.</summary>
public sealed record CameraOriginalTotals(string HeldBy, int Files, long Bytes);

/// <summary>The originals one PC kept after its last complete copy: how many, and since when.</summary>
public sealed record UncoveredOriginals(int Files, DateTime? OldestKeptAtUtc);

/// <summary>A PC whose originals have waited too long for a copy — one line of the bell notification.</summary>
public sealed record StalePhotoOriginals(string HeldBy, int Files, DateTime OldestKeptAtUtc);
