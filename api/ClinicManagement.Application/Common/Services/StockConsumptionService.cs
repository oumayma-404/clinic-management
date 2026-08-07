using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Draws an act's material list out of stock when its fiche is saved (see
/// <see cref="IStockConsumptionService"/>). Persists on the caller's scoped DbContext <b>after</b> the caller has
/// already committed the fiche, then broadcasts the <c>"stock"</c> realtime key so the stock screen refreshes.
///
/// Best-effort: the whole run is wrapped in one try/catch that swallows and logs at Error, so a stock failure can
/// never fail or roll back the clinical record (AC-P4.13).
/// </summary>
public class StockConsumptionService : IStockConsumptionService
{
    private const string RealtimeResourceKey = "stock";

    private readonly IProcedureTypeRepository _procedureTypes;
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly ILogger<StockConsumptionService> _logger;

    public StockConsumptionService(
        IProcedureTypeRepository procedureTypes,
        IStockItemRepository stockItems,
        IStockMovementRepository movements,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtimeNotifier,
        INotificationGenerator notificationGenerator,
        ILogger<StockConsumptionService> logger)
    {
        _procedureTypes = procedureTypes;
        _stockItems = stockItems;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
        _notificationGenerator = notificationGenerator;
        _logger = logger;
    }

    public async Task ConsumeForDentalRecordAsync(
        Guid clinicId,
        Guid dentalRecordId,
        IReadOnlyList<Guid> procedureTypeIds,
        CancellationToken cancellationToken = default)
    {
        // The overwhelmingly common case: nothing recorded a catalogued act, or none of them carry a list.
        // Returning here keeps the no-material path free of any extra read (AC-P4.11).
        if (procedureTypeIds == null || procedureTypeIds.Count == 0)
        {
            return;
        }

        try
        {
            // Total per item across every act on the fiche. Two composites consume two capsules, so a repeated
            // procedure id must multiply rather than collapse — hence counting occurrences, not distinct ids.
            var required = new Dictionary<Guid, int>();
            foreach (var procedureTypeId in procedureTypeIds)
            {
                var procedureType = await _procedureTypes.GetByIdAsync(procedureTypeId, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                {
                    // A cross-clinic or unknown act consumes nothing — the same degrade-to-no-op rule the
                    // post-visit-review resolution uses, rather than throwing inside a best-effort side effect.
                    continue;
                }

                foreach (var material in procedureType.Materials)
                {
                    required[material.StockItemId] =
                        required.GetValueOrDefault(material.StockItemId) + material.QuantityPerAct;
                }
            }

            if (required.Count == 0)
            {
                return;
            }

            var shortfalls = new List<(StockItem Item, int Shortfall)>();
            var crossedIntoLow = new List<StockItem>();

            foreach (var (stockItemId, quantity) in required)
            {
                var item = await _stockItems.GetByIdAsync(stockItemId, cancellationToken);
                if (item == null || item.ClinicId != clinicId)
                {
                    continue;
                }

                var wasLow = item.IsLowStock();

                // FEFO, and NOT blocking on a shortfall (AC-P4.12): the clinical work has already happened, so
                // on-hand is allowed to go negative. Clamping to zero would silently erase the discrepancy,
                // which is the one outcome that makes the ledger un-reconcilable.
                var shortfall = item.ConsumeStock(quantity);

                await _movements.AddAsync(
                    new StockMovement(
                        Guid.NewGuid(), clinicId, item.Id, StockMovementType.Consume, quantity, item.CurrentStock,
                        $"Consommé par la fiche de soins {dentalRecordId}"),
                    cancellationToken);

                await _stockItems.UpdateAsync(item, cancellationToken);

                if (shortfall > 0)
                {
                    shortfalls.Add((item, shortfall));
                }

                if (!wasLow && item.IsLowStock())
                {
                    crossedIntoLow.Add(item);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // AC-P4.12 — the shortfall is *surfaced*, not swallowed. Reuses the low-stock notification rather
            // than inventing a category: the operator's action is the same either way (reorder), and a stock
            // level that has gone negative is the most extreme form of low.
            foreach (var (item, shortfall) in shortfalls)
            {
                _logger.LogWarning(
                    "Stock shortfall consuming {Quantity} of {ItemName} for dental record {DentalRecordId}: {Shortfall} short.",
                    shortfall, item.Name, dentalRecordId, shortfall);

                await _notificationGenerator.LowStockAsync(
                    clinicId, item.Id, item.Name, item.CurrentStock, item.MinimumStockLevel, cancellationToken);
            }

            // Items that merely crossed into low (without a shortfall) get the ordinary edge-triggered notice.
            foreach (var item in crossedIntoLow.Where(i => shortfalls.All(s => s.Item.Id != i.Id)))
            {
                await _notificationGenerator.LowStockAsync(
                    clinicId, item.Id, item.Name, item.CurrentStock, item.MinimumStockLevel, cancellationToken);
            }

            await _realtimeNotifier.NotifyEntityChangedAsync(clinicId, RealtimeResourceKey, cancellationToken);
        }
        catch (Exception ex)
        {
            // AC-P4.13 — never rolls back the fiche. Logged at Error so a genuine bug stays visible, following
            // the INotificationGenerator contract exactly.
            _logger.LogError(
                ex, "Failed to consume stock for dental record {DentalRecordId} in clinic {ClinicId}",
                dentalRecordId, clinicId);
        }
    }
}
