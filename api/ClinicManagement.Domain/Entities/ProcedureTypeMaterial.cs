using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One line of an act's <b>material list</b> (AC-P4.9): "performing this procedure consumes N of this stock
/// item". A child of <see cref="ProcedureType"/>, so it inherits the parent's clinic and its query filter, and
/// the list is per-clinic like every other catalog (AC-P4.14).
///
/// <para><b>Why it hangs off <see cref="ProcedureType"/> and not <c>DentalActCode</c>.</b> AC-P4.9 allows
/// either ("ProcedureType and/or DentalActCode"). Only one of them is reachable from a saved fiche:
/// <c>DentalRecordAct</c> carries a nullable <c>ProcedureTypeId</c> and <b>no</b> <c>DentalActCodeId</c>, so a
/// list attached to <c>DentalActCode</c> could never be consumed on fiche save (AC-P4.10) — it would be a
/// second finished-but-uncallable capability, which is the exact class of defect P2 existed to remove. When a
/// fiche gains a DCH link, this entity is the template to mirror.</para>
/// </summary>
public class ProcedureTypeMaterial : Entity<Guid>
{
    public Guid ProcedureTypeId { get; private set; }
    public Guid StockItemId { get; private set; }

    /// <summary>How many units of the item one performance of the act consumes. Always ≥ 1.</summary>
    public int QuantityPerAct { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private ProcedureTypeMaterial() { } // For EF Core

    public ProcedureTypeMaterial(Guid id, Guid procedureTypeId, Guid stockItemId, int quantityPerAct)
    {
        if (procedureTypeId == Guid.Empty)
            throw new ArgumentException("L'acte est requis.", nameof(procedureTypeId));
        if (stockItemId == Guid.Empty)
            throw new ArgumentException("L'article de stock est requis.", nameof(stockItemId));
        if (quantityPerAct <= 0)
            throw new ArgumentException("La quantité consommée doit être supérieure à 0.", nameof(quantityPerAct));

        Id = id;
        ProcedureTypeId = procedureTypeId;
        StockItemId = stockItemId;
        QuantityPerAct = quantityPerAct;
        CreatedAt = DateTime.UtcNow;
    }
}
