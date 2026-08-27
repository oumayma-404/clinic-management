namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.TreatmentPlan"/>: Draft (editable devis, deletable) →
/// Accepted (numbered, frozen) → InProgress (first act done or first installment paid) → Completed
/// (all acts done), or Cancelled (motif kept, no further changes).
/// </summary>
public enum TreatmentPlanStatus
{
    Draft = 0,
    Accepted = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}
