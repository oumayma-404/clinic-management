namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Draws the stock an act consumes out of the store room when a fiche de soins records it (AC-P4.10).
///
/// Called inline from the dental-record handlers <b>after</b> their own commit, and best-effort in the strict
/// sense: every method swallows its exceptions (logged at Error) so a stock failure can never fail or roll back
/// the fiche — the same contract as <see cref="INotificationGenerator"/> and <see cref="IReminderScheduler"/>
/// (AC-P4.13). The clinical record is the thing that must survive; the inventory is a consequence of it.
///
/// <b>Opt-in per act</b> (AC-P4.11): an act with no material list consumes nothing and behaves exactly as it did
/// before this existed, which is the majority case and must not regress.
/// </summary>
public interface IStockConsumptionService
{
    /// <summary>
    /// Consumes the material list of every act on a saved fiche, writing one <c>StockMovement</c> per item so
    /// the ledger stays reconcilable (AC-P4.15).
    ///
    /// A shortfall never blocks anything (AC-P4.12): the visit already happened, so on-hand is allowed to go
    /// negative and the discrepancy is surfaced as a low-stock notification rather than clamped to zero and
    /// lost.
    /// </summary>
    /// <param name="clinicId">The clinic the fiche belongs to; every item is re-checked against it.</param>
    /// <param name="dentalRecordId">Recorded on each movement's reason, so the ledger says which visit drew it.</param>
    /// <param name="procedureTypeIds">
    /// The acts the fiche recorded, one entry per performance — a repeated id consumes its list twice, because
    /// two composites really do use two capsules.
    /// </param>
    Task ConsumeForDentalRecordAsync(
        Guid clinicId,
        Guid dentalRecordId,
        IReadOnlyList<Guid> procedureTypeIds,
        CancellationToken cancellationToken = default);
}
