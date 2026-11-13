using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>A camera shot this cabinet has already filed, and where — the « déjà importées » line of an import.</summary>
public sealed record ImportedCaptureRow(string CaptureKey, Guid FileId, Guid PatientId, string PatientName);

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
