using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A Tunisian dental nomenclature act (chapitre <c>DCH</c> of the CNAM "Liste des actes"), DB-backed and
/// <b>per-clinic</b> reference data (has <c>ClinicId</c> and a clinic query filter; every clinic is seeded
/// the same defaults, then edits stay private). <b>The only act catalogue</b> since feature
/// <c>single-act-catalogue</c> retired the parallel invented one. Seeded <b>provisionally</b> ("à vérifier")
/// from the official list until an admin confirms it; the per-act numeric <c>Coefficient</c> (cotation)
/// is optional because it lives in the separate NGAP arrêté, not in the acts list. Create/update/
/// deactivate/confirm are admin-only (enforced at the controller).
/// </summary>
public class DentalActCode : AggregateRoot<Guid>
{
    /// <summary>Owning clinic — per-clinic catalog (#5).</summary>
    public Guid ClinicId { get; private set; }
    /// <summary>The DCH code, e.g. <c>DCH020030</c>. Unique per clinic (enforced by the handler + a composite index).</summary>
    public string CodeActe { get; private set; } = string.Empty;
    public string DesignationFr { get; private set; } = string.Empty;
    /// <summary>Lettre clé — "D" for every dental act (the CNAM dental key).</summary>
    public string LettreCle { get; private set; } = "D";
    /// <summary>Cotation coefficient. Nullable: absent from the acts list (defined by the NGAP arrêté).</summary>
    public decimal? Coefficient { get; private set; }
    public string Category { get; private set; } = string.Empty;
    /// <summary>Suggested private fee in TND (honoraires libres); optional.</summary>
    public decimal? DefaultFee { get; private set; }
    /// <summary>Whether CNAM prior authorization ("accord préalable") is required for this act.</summary>
    public bool RequiresAccordPrealable { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsProvisional { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private DentalActCode() { } // For EF Core

    public DentalActCode(
        Guid id,
        Guid clinicId,
        string codeActe,
        string designationFr,
        string category,
        string lettreCle = "D",
        decimal? coefficient = null,
        decimal? defaultFee = null,
        bool requiresAccordPrealable = false,
        bool isProvisional = true)
    {
        Id = id;
        ClinicId = clinicId;
        SetCore(codeActe, designationFr, lettreCle, coefficient, category, defaultFee, requiresAccordPrealable);
        IsActive = true;
        IsProvisional = isProvisional;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Update the editable fields (admin). Code acte can change; uniqueness is enforced by the handler.</summary>
    public void Update(
        string codeActe,
        string designationFr,
        string lettreCle,
        decimal? coefficient,
        string category,
        decimal? defaultFee,
        bool requiresAccordPrealable)
    {
        SetCore(codeActe, designationFr, lettreCle, coefficient, category, defaultFee, requiresAccordPrealable);
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

    /// <summary>Clears the provisional "à vérifier" flag once an admin has confirmed the data.</summary>
    public void Confirm()
    {
        IsProvisional = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private void SetCore(
        string codeActe,
        string designationFr,
        string lettreCle,
        decimal? coefficient,
        string category,
        decimal? defaultFee,
        bool requiresAccordPrealable)
    {
        if (string.IsNullOrWhiteSpace(codeActe))
            throw new ArgumentException("Le code acte est obligatoire.", nameof(codeActe));
        if (string.IsNullOrWhiteSpace(designationFr))
            throw new ArgumentException("La désignation est obligatoire.", nameof(designationFr));
        if (string.IsNullOrWhiteSpace(lettreCle))
            throw new ArgumentException("La lettre clé est obligatoire.", nameof(lettreCle));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("La catégorie est obligatoire.", nameof(category));
        if (coefficient.HasValue && coefficient.Value <= 0)
            throw new ArgumentException("Le coefficient doit être strictement positif.", nameof(coefficient));
        if (defaultFee.HasValue && defaultFee.Value < 0)
            throw new ArgumentException("Le tarif par défaut ne peut pas être négatif.", nameof(defaultFee));

        CodeActe = codeActe.Trim();
        DesignationFr = designationFr.Trim();
        LettreCle = lettreCle.Trim().ToUpperInvariant();
        Coefficient = coefficient;
        Category = category.Trim();
        DefaultFee = defaultFee;
        RequiresAccordPrealable = requiresAccordPrealable;
    }
}
