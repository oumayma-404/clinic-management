using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One recorded condition on a tooth (child-of-patient; no <c>ClinicId</c> — tenant isolation is via the
/// owning <see cref="Patient"/>). A tooth can have MANY of these across time. A <see cref="ToothStateSource.Treatment"/>
/// entry is produced by a <see cref="DentalRecord"/> (the session in which the doctor recorded a completed act)
/// and carries that record's date; a <see cref="ToothStateSource.Diagnosis"/> entry is charted directly on the
/// odontogram before treatment and has no source record. The patient's odontogram is the accumulation of these
/// entries; deleting a source record cascades its treatment entries away, while diagnosis entries persist until
/// treated or explicitly removed.
/// </summary>
public class ToothState : Entity<Guid>
{
    public Guid PatientId { get; private set; }

    /// <summary>The owning clinic, denormalised from the patient. See <see cref="PatientMedicalHistory.ClinicId"/>.</summary>
    public Guid ClinicId { get; private set; }

    public int ToothNumber { get; private set; }
    public ToothCondition Condition { get; private set; }
    /// <summary>Whether this entry is a charted diagnosis or a completed treatment (from a dental record).</summary>
    public ToothStateSource Source { get; private set; }
    /// <summary>Affected surfaces, a subset of <c>MODVL</c> (Mésial/Occlusal/Distal/Vestibulaire/Lingual); optional.</summary>
    public string? Surfaces { get; private set; }
    public string? Note { get; private set; }
    /// <summary>The dental record (session) that recorded this treatment. Null for diagnosis / legacy entries.</summary>
    public Guid? DentalRecordId { get; private set; }
    /// <summary>Date the treatment was carried out (the source record's intervention date), or the diagnosis date.</summary>
    public DateTime TreatmentDate { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ToothState() { } // For EF Core

    public ToothState(
        Guid id,
        Guid patientId,
        Guid clinicId,
        int toothNumber,
        ToothCondition condition,
        DateTime treatmentDate,
        string? surfaces = null,
        string? note = null,
        Guid? dentalRecordId = null,
        ToothStateSource source = ToothStateSource.Treatment)
    {
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        if (!FdiTooth.IsValid(toothNumber))
            throw new ArgumentException($"Numéro de dent invalide : {toothNumber}.", nameof(toothNumber));

        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
        ToothNumber = toothNumber;
        Condition = condition;
        Source = source;
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
