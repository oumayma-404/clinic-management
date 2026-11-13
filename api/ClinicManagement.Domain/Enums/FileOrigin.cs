namespace ClinicManagement.Domain.Enums;

/// <summary>
/// How a patient file reached the record.
///
/// <para>⚠️ <b>Persisted as an int — append only.</b> Slotting a member in where it reads best would repoint every
/// stored row. The X-ray watcher of <c>features/sensor-auto-import</c> takes <c>2</c>.</para>
/// </summary>
public enum FileOrigin
{
    /// <summary>Picked or dropped by somebody — every file stored before camera import existed.</summary>
    Upload = 0,

    /// <summary>
    /// Filed from a camera's photos through « Importer les photos » (SD card, USB cable, phone gallery). Such a file
    /// carries a <see cref="Entities.PatientFile.CaptureKey"/>, and may have a full-size original kept on a clinic PC.
    /// </summary>
    Camera = 1,
}
