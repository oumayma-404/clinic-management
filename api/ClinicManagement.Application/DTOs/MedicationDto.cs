namespace ClinicManagement.Application.DTOs;

// A single medication catalog entry. DB-backed and GLOBAL (not clinic-scoped): the doctor picks an entry to
// consistently fill the ordonnance medication line. Reads are open to any authenticated user;
// create/update/deactivate/confirm are admin-only. Dcis holds the active ingredient molecules (one or more).
public class MedicationDto
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();
    public bool IsActive { get; set; }
    public bool IsProvisional { get; set; }
}
