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

    private TreatmentPlanItem() { } // For EF Core

    public TreatmentPlanItem(
        Guid id,
        Guid treatmentPlanId,
        string designationFr,
        decimal plannedCost,
        Guid? dentalActCodeId = null,
        string? codeActe = null,
        IEnumerable<int>? toothNumbers = null)
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

    public void MarkDone(DateTime doneOn, Guid? linkedDentalRecordId)
    {
        Status = TreatmentPlanItemStatus.Done;
        DoneDate = doneOn;
        LinkedDentalRecordId = linkedDentalRecordId;
    }
}
