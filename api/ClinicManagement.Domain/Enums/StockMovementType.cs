namespace ClinicManagement.Domain.Enums;

/// <summary>Direction of a stock movement: a sortie (consumption), an entrée (replenishment), or a correction.</summary>
public enum StockMovementType
{
    Consume,
    Restock,

    /// <summary>
    /// A manual stock-take correction — the operator set on-hand to an absolute figure rather than moving a
    /// delta (AC-P4.16). Distinguishable from a consume or a restock on purpose: an inventory count that
    /// disagreed with the books is a different fact from stock actually leaving or arriving, and reading the
    /// ledger without that distinction makes a correction look like real consumption.
    /// </summary>
    Adjustment
}
