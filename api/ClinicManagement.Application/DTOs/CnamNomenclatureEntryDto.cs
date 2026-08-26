namespace ClinicManagement.Application.DTOs;

// A single CNAM dental nomenclature entry. Now DB-backed (FR-5.1) and GLOBAL (not clinic-scoped): the
// doctor picks an entry to consistently fill Code acte + Cotation on a bulletin. Reads are open to any
// authenticated user; create/update/deactivate/confirm are admin-only.
public class CnamNomenclatureEntryDto
{
    public Guid Id { get; set; }
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsProvisional { get; set; }

    /// <summary>Round-tripped by the edit form so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }
}
