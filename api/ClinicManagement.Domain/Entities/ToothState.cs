using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One recorded treatment/condition on a tooth (child-of-patient; no <c>ClinicId</c> — tenant isolation is
/// via the owning <see cref="Patient"/>). A tooth can have MANY of these across time: each is produced by a
/// <see cref="DentalRecord"/> (the session in which the doctor recorded it) and carries that record's date.
/// The patient's odontogram is the accumulation of these entries; it is edited only through the dental-record
/// flow (never directly), so deleting the source record cascades its tooth entries away.
/// </summary>
public class ToothState : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public int ToothNumber { get; private set; }
    public ToothCondition Condition { get; private set; }
    /// <summary>Affected surfaces, a subset of <c>MODVL</c> (Mésial/Occlusal/Distal/Vestibulaire/Lingual); optional.</summary>
    public string? Surfaces { get; private set; }
    public string? Note { get; private set; }
    /// <summary>The dental record (session) that recorded this treatment. Null only for legacy/manual entries.</summary>
    public Guid? DentalRecordId { get; private set; }
    /// <summary>Date the treatment was carried out (the source record's intervention date).</summary>
    public DateTime TreatmentDate { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ToothState() { } // For EF Core

    public ToothState(
        Guid id,
        Guid patientId,
        int toothNumber,
        ToothCondition condition,
        DateTime treatmentDate,
        string? surfaces = null,
        string? note = null,
        Guid? dentalRecordId = null)
    {
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        if (!FdiTooth.IsValid(toothNumber))
            throw new ArgumentException($"Numéro de dent invalide : {toothNumber}.", nameof(toothNumber));

        Id = id;
        PatientId = patientId;
        ToothNumber = toothNumber;
        Condition = condition;
        Surfaces = NormalizeSurfaces(surfaces);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        TreatmentDate = treatmentDate;
        DentalRecordId = dentalRecordId;
        CreatedAt = DateTime.UtcNow;
    }

    private static string? NormalizeSurfaces(string? surfaces)
    {
        if (string.IsNullOrWhiteSpace(surfaces))
            return null;

        var normalized = surfaces.Trim().ToUpperInvariant();
        foreach (var c in normalized)
        {
            if ("MODVL".IndexOf(c) < 0)
                throw new ArgumentException($"Surface invalide : '{c}'. Valeurs autorisées : M, O, D, V, L.", nameof(surfaces));
        }
        return normalized;
    }
}
