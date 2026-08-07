using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Stock;

/// <summary>
/// § 6.7 — stock is consumed by performing an act (AC-P4.9–4.14). Before this, <c>grep StockItem</c> across every
/// feature outside <c>Features/Stock/</c> returned <b>zero</b> hits: consumption was 100% manual.
///
/// Two rules carry the most weight and are pinned from both directions: the link is <b>opt-in per act</b>
/// (AC-P4.11 — the majority case must not regress) and a shortfall <b>never blocks the visit</b> (AC-P4.12 — the
/// clinical work has already happened).
/// </summary>
public class StockConsumptionServiceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<IStockItemRepository> _stockItems = new();
    private readonly Mock<IStockMovementRepository> _movements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRealtimeNotifier> _realtime = new();
    private readonly Mock<INotificationGenerator> _notifications = new();
    private readonly List<StockMovement> _written = new();

    public StockConsumptionServiceTests()
    {
        _movements
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => _written.Add(m))
            .ReturnsAsync((StockMovement m, CancellationToken _) => m);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private StockConsumptionService Service() => new(
        _procedureTypes.Object, _stockItems.Object, _movements.Object, _uow.Object,
        _realtime.Object, _notifications.Object,
        NullLogger<StockConsumptionService>.Instance);

    private StockItem StockedItem(string name, int onHand, int minimum = 0)
    {
        var item = new StockItem(Guid.NewGuid(), ClinicId, name, "Consommable", "Unité", minimum, 500);
        if (onHand > 0)
        {
            item.AddStock(onHand);
        }

        _stockItems.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        return item;
    }

    private ProcedureType ActWithMaterials(Guid clinicId, params (Guid StockItemId, int Quantity)[] materials)
    {
        var act = new ProcedureType(Guid.NewGuid(), clinicId, "Composite", 30, ColorHex.FromString("#4F83CC"));
        act.SetMaterials(materials.Select(m => (m.StockItemId, m.Quantity)));
        _procedureTypes.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        return act;
    }

    // ------------------------------------------------------------------ opt-in (AC-P4.11)

    /// <summary>
    /// The majority case. An act with no material list must consume nothing AND cost nothing — not even a stock
    /// read — because every fiche in the app goes through this path.
    /// </summary>
    [Fact]
    public async Task An_Act_With_No_Material_List_Consumes_Nothing()
    {
        var act = ActWithMaterials(ClinicId);

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        Assert.Empty(_written);
        _stockItems.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A fiche with no catalogued act at all (free-text only) short-circuits before touching any repository.
    [Fact]
    public async Task A_Fiche_With_No_Catalogued_Act_Reads_Nothing_At_All()
    {
        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, Array.Empty<Guid>());

        _procedureTypes.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_written);
    }

    // ------------------------------------------------------------------ the happy path (AC-P4.10/4.15)

    [Fact]
    public async Task An_Acts_Material_List_Is_Drawn_Down_And_Ledgered()
    {
        var gloves = StockedItem("Gants", 20);
        var act = ActWithMaterials(ClinicId, (gloves.Id, 2));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        Assert.Equal(18, gloves.CurrentStock);
        var movement = Assert.Single(_written);
        Assert.Equal(StockMovementType.Consume, movement.Type);
        Assert.Equal(2, movement.Quantity);
        Assert.Equal(18, movement.ResultingStock);
        // AC-P4.17 — the reason names the visit, so the ledger says WHICH fiche drew the stock.
        Assert.Contains(RecordId.ToString(), movement.Reason);
    }

    /// <summary>
    /// Two performances of the same act consume twice: two composites really do use two capsules. Collapsing to
    /// distinct procedure ids would silently under-consume, which is the subtler half of "the ledger must
    /// reconcile".
    /// </summary>
    [Fact]
    public async Task The_Same_Act_Twice_On_One_Fiche_Consumes_Twice()
    {
        var capsules = StockedItem("Capsules", 20);
        var act = ActWithMaterials(ClinicId, (capsules.Id, 1));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id, act.Id });

        Assert.Equal(18, capsules.CurrentStock);
        // One movement for the combined quantity — the ledger records the draw, not the loop.
        Assert.Equal(2, Assert.Single(_written).Quantity);
    }

    // Two different acts needing the same item are combined into one draw of the total.
    [Fact]
    public async Task Two_Acts_Sharing_An_Item_Draw_The_Combined_Total()
    {
        var gloves = StockedItem("Gants", 20);
        var first = ActWithMaterials(ClinicId, (gloves.Id, 2));
        var second = ActWithMaterials(ClinicId, (gloves.Id, 3));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { first.Id, second.Id });

        Assert.Equal(15, gloves.CurrentStock);
        Assert.Equal(5, Assert.Single(_written).Quantity);
    }

    // ------------------------------------------------------------------ shortfall (AC-P4.12)

    /// <summary>
    /// The stated rule: recording the visit is never blocked by a stock shortfall, stock is allowed to go
    /// negative rather than clamping to zero and losing the discrepancy, and the shortfall is SURFACED.
    /// </summary>
    [Fact]
    public async Task A_Shortfall_Goes_Negative_And_Is_Surfaced_Rather_Than_Blocking()
    {
        var gloves = StockedItem("Gants", 1, minimum: 5);
        var act = ActWithMaterials(ClinicId, (gloves.Id, 4));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        Assert.Equal(-3, gloves.CurrentStock);
        Assert.Single(_written);
        _notifications.Verify(
            g => g.LowStockAsync(ClinicId, gloves.Id, "Gants", -3, 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // An item that merely crosses into low (no shortfall) gets exactly one edge-triggered notice — and an item
    // that was ALREADY low does not re-notify, matching the existing edge-triggered contract.
    [Fact]
    public async Task Crossing_Into_Low_Notifies_Once()
    {
        var gloves = StockedItem("Gants", 10, minimum: 8);
        var act = ActWithMaterials(ClinicId, (gloves.Id, 3));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        Assert.Equal(7, gloves.CurrentStock);
        _notifications.Verify(
            g => g.LowStockAsync(ClinicId, gloves.Id, "Gants", 7, 8, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Staying_Low_Does_Not_Re_Notify()
    {
        var gloves = StockedItem("Gants", 3, minimum: 8); // already low
        var act = ActWithMaterials(ClinicId, (gloves.Id, 1));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        _notifications.Verify(
            g => g.LowStockAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------ tenancy and best-effort

    // A cross-clinic act consumes nothing — degrade to a no-op rather than throwing inside a best-effort
    // side effect, matching how the post-visit-review target resolution handles the same case.
    [Fact]
    public async Task An_Act_From_Another_Clinic_Consumes_Nothing()
    {
        var gloves = StockedItem("Gants", 20);
        var foreignAct = ActWithMaterials(OtherClinicId, (gloves.Id, 2));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { foreignAct.Id });

        Assert.Equal(20, gloves.CurrentStock);
        Assert.Empty(_written);
    }

    /// <summary>
    /// [AC-P4.13] The contract that protects the clinical record: this runs post-commit, so a stock failure must
    /// never throw back. If it did, a failed inventory write would surface as a failed fiche save — and the
    /// dentist would retype the visit.
    /// </summary>
    [Fact]
    public async Task A_Persistence_Failure_Never_Throws_Back_To_The_Fiche()
    {
        var gloves = StockedItem("Gants", 20);
        var act = ActWithMaterials(ClinicId, (gloves.Id, 2));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });
        // Reaching here at all is the assertion.
    }

    [Fact]
    public async Task A_Successful_Consumption_Broadcasts_The_Stock_Key()
    {
        var gloves = StockedItem("Gants", 20);
        var act = ActWithMaterials(ClinicId, (gloves.Id, 1));

        await Service().ConsumeForDentalRecordAsync(ClinicId, RecordId, new[] { act.Id });

        _realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicId, "stock", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------ the material list itself (AC-P4.9)

    // One stock item may appear only once per act; a duplicate is a caller bug, not something to silently merge.
    [Fact]
    public void An_Act_Refuses_A_Duplicate_Material_Line()
    {
        var act = new ProcedureType(Guid.NewGuid(), ClinicId, "Composite", 30, ColorHex.FromString("#4F83CC"));
        var itemId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => act.SetMaterials(new[] { (itemId, 1), (itemId, 2) }));
    }

    [Fact]
    public void Setting_Materials_Replaces_The_Whole_List()
    {
        var act = new ProcedureType(Guid.NewGuid(), ClinicId, "Composite", 30, ColorHex.FromString("#4F83CC"));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        act.SetMaterials(new[] { (first, 1) });
        act.SetMaterials(new[] { (second, 4) });

        var material = Assert.Single(act.Materials);
        Assert.Equal(second, material.StockItemId);
        Assert.Equal(4, material.QuantityPerAct);
    }

    [Fact]
    public void A_Material_Line_Requires_A_Positive_Quantity()
    {
        var act = new ProcedureType(Guid.NewGuid(), ClinicId, "Composite", 30, ColorHex.FromString("#4F83CC"));

        Assert.Throws<ArgumentException>(() => act.SetMaterials(new[] { (Guid.NewGuid(), 0) }));
    }
}
