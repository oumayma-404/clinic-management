namespace ClinicManagement.Application.DTOs;

public class MedicalDocumentDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientAge { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public string ClinicName { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string ClinicPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public Guid? FileId { get; set; }
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// The fiche de soins that emitted this document, when one did.
    ///
    /// <para>
    /// ⚠️ <b>Not the same fact as <see cref="AppointmentId"/>, and the difference is visible on screen.</b> The
    /// appointment is null on every fiche charted outside the agenda, so the Documents tab's « Séance » column
    /// printed « — » for exactly those rows even though the document belonged to a séance. It also decides
    /// which door « Modifier » offers: a document a fiche owns must be edited <b>in that fiche</b>, because the
    /// next fiche save recomposes its <c>ContentJson</c> from the section and would silently overwrite whatever
    /// the standalone editor had written.
    /// </para>
    /// </summary>
    public Guid? DentalRecordId { get; set; }

    /// <summary>Round-tripped by the editor so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

