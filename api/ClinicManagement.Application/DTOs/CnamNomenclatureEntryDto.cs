namespace ClinicManagement.Application.DTOs;

// A single CNAM dental nomenclature entry — static, in-code reference data supplied by the
// Infrastructure provider and exposed read-only via GET /api/cnam-nomenclature. Not clinic-scoped,
// never persisted. The doctor picks an entry to consistently fill Code acte + Cotation on a bulletin.
public class CnamNomenclatureEntryDto
{
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
}
