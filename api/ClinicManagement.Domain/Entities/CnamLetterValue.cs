using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// The <b>valeur de la lettre clé (VLC)</b> — the dinar value per lettre clé (CD/CDS/VD/D/RD…) used in the
/// indicative CNAM reimbursement estimate (FR-5.2). Like <see cref="CnamNomenclatureEntry"/> this is
/// <b>global</b> reference data (no <c>ClinicId</c>, not in the clinic query filter) and seeded
/// provisionally ("à vérifier") until an admin confirms it. Read by any authenticated user; only an admin
/// can change a value.
/// </summary>
public class CnamLetterValue : AggregateRoot<Guid>
{
    /// <summary>Owning clinic — per-clinic catalog (#5).</summary>
    public Guid ClinicId { get; private set; }
    public string LettreCle { get; private set; } = string.Empty;
    public decimal Value { get; private set; }
    public bool IsProvisional { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private CnamLetterValue() { } // For EF Core

    public CnamLetterValue(Guid id, Guid clinicId, string lettreCle, decimal value, bool isProvisional = true)
    {
        if (string.IsNullOrWhiteSpace(lettreCle))
            throw new ArgumentException("La lettre clé est obligatoire.", nameof(lettreCle));
        if (value < 0)
            throw new ArgumentException("La valeur de la lettre clé ne peut pas être négative.", nameof(value));

        Id = id;
        ClinicId = clinicId;
        LettreCle = lettreCle.Trim().ToUpperInvariant();
        Value = value;
        IsProvisional = isProvisional;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetValue(decimal value)
    {
        if (value < 0)
            throw new ArgumentException("La valeur de la lettre clé ne peut pas être négative.", nameof(value));

        Value = value;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Clears the provisional "à vérifier" flag once an admin has confirmed the value (FR-5.2).</summary>
    public void Confirm()
    {
        IsProvisional = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
