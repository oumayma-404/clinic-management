using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A planned act line on a <see cref="TreatmentPlan"/> (aggregate child). References a catalog
/// <see cref="DentalActCode"/> (snapshotting its code) <b>or</b> carries a free-text designation for
/// non-CNAM acts (crowns, implants…). Optionally targets specific FDI teeth.
/// </summary>
public class TreatmentPlanItem : Entity<Guid>
{
    public Guid TreatmentPlanId { get; private set; }
    public Guid? DentalActCodeId { get; private set; }
    public string? CodeActe { get; private set; }
    public string DesignationFr { get; private set; } = string.Empty;

    private readonly List<int> _toothNumbers = new();
    public IReadOnlyList<int> ToothNumbers => _toothNumbers.AsReadOnly();

    public decimal PlannedCost { get; private set; }
    public TreatmentPlanItemStatus Status { get; private set; }
    public DateTime? DoneDate { get; private set; }
    public Guid? LinkedDentalRecordId { get; private set; }

    /// <summary>
    /// Clinical order within the plan (0-based). Deliberately a plain ordering field, **not** a séance id:
    /// grouping acts into numbered séances is a separate, larger feature, and keeping this dumb means that
    /// change stays additive. Pre-migration rows read 0 and keep their insertion order until first reordered.
    /// </summary>
    public int SequenceNumber { get; private set; }

    private TreatmentPlanItem() { } // For EF Core

    public TreatmentPlanItem(
        Guid id,
        Guid treatmentPlanId,
        string designationFr,
        decimal plannedCost,
        Guid? dentalActCodeId = null,
        string? codeActe = null,
        IEnumerable<int>? toothNumbers = null,
        int sequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(designationFr))
            throw new ArgumentException("La désignation de l'acte est requise.", nameof(designationFr));
        if (plannedCost < 0)
            throw new ArgumentException("Le coût prévu ne peut pas être négatif.", nameof(plannedCost));

        Id = id;
        TreatmentPlanId = treatmentPlanId;
        DentalActCodeId = dentalActCodeId;
        CodeActe = string.IsNullOrWhiteSpace(codeActe) ? null : codeActe.Trim();
        DesignationFr = designationFr.Trim();
        PlannedCost = InvoiceCalculator.RoundMoney(plannedCost);
        Status = TreatmentPlanItemStatus.Planned;
        SequenceNumber = sequenceNumber;

        if (toothNumbers != null)
        {
            foreach (var tooth in toothNumbers.Distinct())
            {
                if (!FdiTooth.IsValid(tooth))
                    throw new ArgumentException($"Numéro de dent invalide : {tooth}.", nameof(toothNumbers));
                _toothNumbers.Add(tooth);
            }
        }
    }

    /// <summary>
    /// Record that this act was carried out, linking the dental record that evidences it.
    /// <para>
    /// Re-marking is guarded. Editing the same record must stay idempotent (a fiche can be saved twice), so
    /// re-linking the <b>same</b> record is a no-op. But silently overwriting <see cref="DoneDate"/> and
    /// <see cref="LinkedDentalRecordId"/> with a <b>different</b> record would rewrite clinical history —
    /// the act would claim to have happened at a visit it did not — so that is refused.
    /// </para>
    /// </summary>
    public void MarkDone(DateTime doneOn, Guid? linkedDentalRecordId)
    {
        if (Status == TreatmentPlanItemStatus.Done)
        {
            // Same evidence (or none supplied now): nothing changes, and re-saving a fiche must not fail.
            if (linkedDentalRecordId == null || linkedDentalRecordId == LinkedDentalRecordId)
            {
                return;
            }

            throw new InvalidOperationException(
                "Cet acte est déjà réalisé et rattaché à une autre fiche de soins. Détachez-le de cette fiche avant de le rattacher à une nouvelle.");
        }

        Status = TreatmentPlanItemStatus.Done;
        DoneDate = doneOn;
        LinkedDentalRecordId = linkedDentalRecordId;
    }

    /// <summary>Place this act at a given position in the plan's clinical order.</summary>
    public void SetSequenceNumber(int sequenceNumber)
    {
        if (sequenceNumber < 0)
            throw new ArgumentException("La position de l'acte ne peut pas être négative.", nameof(sequenceNumber));

        SequenceNumber = sequenceNumber;
    }
}
