using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A CNAM dental nomenclature act — DB-backed, <b>per-clinic</b> reference data (has <c>ClinicId</c> and a
/// clinic query filter; every clinic is seeded the same defaults, then edits stay private). Replaces the former in-code
/// <c>CnamNomenclatureProvider</c> (FR-5.1). Entries are seeded <b>provisionally</b> ("à vérifier") until
/// an admin confirms the data against the current CNAM dentist convention; nothing is blocked while the
/// flag is set. Create/update/deactivate/confirm are admin-only (enforced at the controller).
/// </summary>
public class CnamNomenclatureEntry : AggregateRoot<Guid>
{
    /// <summary>Owning clinic. The catalog is per-clinic (feature cloud-security-and-tenant-isolation, #5):
    /// every clinic is seeded with the same default set, then its admin edits stay private to it.</summary>
    public Guid ClinicId { get; private set; }
    public string CodeActe { get; private set; } = string.Empty;
    public string DesignationFr { get; private set; } = string.Empty;
    public string LettreCle { get; private set; } = string.Empty;
    public decimal Coefficient { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool IsProvisional { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private CnamNomenclatureEntry() { } // For EF Core

    public CnamNomenclatureEntry(
        Guid id,
        Guid clinicId,
        string codeActe,
        string designationFr,
        string lettreCle,
        decimal coefficient,
        string category,
        bool isProvisional = true)
    {
        Id = id;
        ClinicId = clinicId;
        SetCore(codeActe, designationFr, lettreCle, coefficient, category);
        IsActive = true;
        IsProvisional = isProvisional;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Update the editable fields (admin). Code acte can change; uniqueness is enforced by the handler.</summary>
    public void Update(string codeActe, string designationFr, string lettreCle, decimal coefficient, string category)
    {
        SetCore(codeActe, designationFr, lettreCle, coefficient, category);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Clears the provisional "à vérifier" flag once an admin has confirmed the data (FR-5.1).</summary>
    public void Confirm()
    {
        IsProvisional = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private void SetCore(string codeActe, string designationFr, string lettreCle, decimal coefficient, string category)
    {
        if (string.IsNullOrWhiteSpace(codeActe))
            throw new ArgumentException("Le code acte est obligatoire.", nameof(codeActe));
        if (string.IsNullOrWhiteSpace(designationFr))
            throw new ArgumentException("La désignation est obligatoire.", nameof(designationFr));
        if (string.IsNullOrWhiteSpace(lettreCle))
            throw new ArgumentException("La lettre clé est obligatoire.", nameof(lettreCle));
        if (coefficient <= 0)
            throw new ArgumentException("Le coefficient doit être strictement positif.", nameof(coefficient));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("La catégorie est obligatoire.", nameof(category));

        CodeActe = codeActe.Trim();
        DesignationFr = designationFr.Trim();
        LettreCle = lettreCle.Trim().ToUpperInvariant();
        Coefficient = coefficient;
        Category = category.Trim();
    }
}
