using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One active ingredient (DCI / INN molecule) of a <see cref="Medication"/>. A medication has one-or-more
/// (combination drugs carry several). The molecule is what a later drug-interaction check keys on, so it is
/// stored normalized (trimmed) and deduped case-insensitively within a medication.
/// </summary>
public class MedicationActiveIngredient : Entity<Guid>
{
    public Guid MedicationId { get; private set; }
    public string Dci { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    // Navigation property
    public Medication Medication { get; private set; } = null!;

    private MedicationActiveIngredient() { } // For EF Core

    public MedicationActiveIngredient(Guid id, Guid medicationId, string dci)
    {
        var normalized = NormalizeDci(dci);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("La DCI (molécule) ne peut pas être vide.", nameof(dci));

        Id = id;
        MedicationId = medicationId;
        Dci = normalized;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Canonical normalization for a DCI string. Trims only (case preserved for display); matching
    /// is done case-insensitively.</summary>
    public static string NormalizeDci(string dci) => dci?.Trim() ?? string.Empty;
}
