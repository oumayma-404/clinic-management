namespace ClinicManagement.Application.DTOs;

// A valeur de la lettre clé (VLC) — the dinar value per lettre clé used in the indicative reimbursement
// estimate (FR-5.2). Global reference data; readable by any authenticated user, editable by admins only.
public class CnamLetterValueDto
{
    public Guid Id { get; set; }
    public string LettreCle { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public bool IsProvisional { get; set; }
}
