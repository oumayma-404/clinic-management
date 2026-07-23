using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A drug in the global medication catalog — DB-backed, <b>global</b> reference data (no <c>ClinicId</c>;
/// excluded from the clinic query filter, shared across every clinic). Mirrors the CNAM nomenclature
/// catalog pattern. A doctor picks an entry to fill the ordonnance medication line consistently
/// (structured selection instead of free text). Entries are seeded <b>provisionally</b> ("à vérifier")
/// until an admin confirms them; nothing is blocked while the flag is set. Create/update/deactivate/confirm
/// are admin-only (enforced at the controller). Each medication carries one or more active ingredients
/// (DCI / INN molecules) — combination drugs have several.
/// </summary>
public class Medication : AggregateRoot<Guid>
{
    /// <summary>Owning clinic — per-clinic catalog (#5).</summary>
    public Guid ClinicId { get; private set; }
    public string BrandName { get; private set; } = string.Empty;
    public string Form { get; private set; } = string.Empty;
    public string Strength { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool IsProvisional { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<MedicationActiveIngredient> _activeIngredients = new();
    public IReadOnlyCollection<MedicationActiveIngredient> ActiveIngredients => _activeIngredients.AsReadOnly();

    private Medication() { } // For EF Core

    public Medication(
        Guid id,
        Guid clinicId,
        string brandName,
        string form,
        string strength,
        IEnumerable<string> dcis,
        bool isProvisional = true)
    {
        Id = id;
        ClinicId = clinicId;
        SetCore(brandName, form, strength);
        foreach (var dci in dcis ?? Enumerable.Empty<string>())
        {
            AddIngredientInternal(dci);
        }
        IsActive = true;
        IsProvisional = isProvisional;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Update the editable fields (admin). Brand/form/strength can change.</summary>
    public void Update(string brandName, string form, string strength)
    {
        SetCore(brandName, form, strength);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Replace the full set of active ingredients (admin edit). Existing molecules are cleared first.</summary>
    public void ReplaceActiveIngredients(IEnumerable<string> dcis)
    {
        _activeIngredients.Clear();
        foreach (var dci in dcis ?? Enumerable.Empty<string>())
        {
            AddIngredientInternal(dci);
        }
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

    private void SetCore(string brandName, string form, string strength)
    {
        if (string.IsNullOrWhiteSpace(brandName))
            throw new ArgumentException("Le nom commercial est obligatoire.", nameof(brandName));

        BrandName = brandName.Trim();
        Form = form?.Trim() ?? string.Empty;
        Strength = strength?.Trim() ?? string.Empty;
    }

    // Adds a molecule, normalized + case-insensitively deduped; never bumps UpdatedAt (used by ctor + replace).
    private void AddIngredientInternal(string dci)
    {
        var normalized = MedicationActiveIngredient.NormalizeDci(dci);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("La DCI (molécule) ne peut pas être vide.", nameof(dci));

        if (_activeIngredients.Any(i => string.Equals(i.Dci, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        _activeIngredients.Add(new MedicationActiveIngredient(Guid.NewGuid(), Id, normalized));
    }
}
