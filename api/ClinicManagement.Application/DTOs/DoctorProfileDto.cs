namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The practitioner-facing view of a doctor's document identity (FR-2.5 / FR-3.1): their CNOMDT order
/// number and whether a cachet image is on file. The cachet image itself is streamed separately
/// (<c>GET /api/doctors/{id}/cachet</c>) — never inlined here.
/// </summary>
public class DoctorProfileDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string? OrdreNumberCnomdt { get; set; }
    public bool HasCachet { get; set; }
    public string? CachetContentType { get; set; }

    /// <summary>Round-tripped by « Mon profil » so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }
}

/// <summary>Streamed cachet image + its persisted content type (mirrors the patient-file download shape).</summary>
public class DoctorCachetDto
{
    public Stream FileStream { get; set; } = Stream.Null;
    public string ContentType { get; set; } = "application/octet-stream";
}
