using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

/// <summary>
/// Body of <c>PUT /api/users/{id}/practitioner</c> — « Lier à un praticien » (cabinet-de-groupe US-3). Either an
/// existing practitioner of the roster to link, or the three fields of a new one.
/// </summary>
public class LinkUserPractitionerRequest
{
    public Guid? ExistingDoctorId { get; set; }
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}
