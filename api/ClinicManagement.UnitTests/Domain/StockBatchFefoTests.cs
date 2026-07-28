using ClinicManagement.Domain.Entities;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Per-batch stock and FEFO consumption (AC-P4.1, AC-P4.4, AC-P4.7, AC-P4.12).
///
/// The defect these pin: <c>ExpiryDate</c>/<c>BatchNumber</c> were two scalar columns that <c>AddStock</c>
/// <b>overwrote</b>, so a second delivery destroyed the first lot's date and the item displayed whatever arrived
/// last rather than the date that actually matters.
/// </summary>
public class StockBatchFefoTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Today = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    private static StockItem Item(int minimum = 0) =>
        new(Guid.NewGuid(), ClinicId, "Composite", "Consommable", "Boîte", minimum, 100);

    // [AC-P4.1] Two deliveries, two lots — the first one's date survives the second.
    [Fact]
    public void A_Second_Delivery_Does_Not_Destroy_The_First_Lots_Expiry()
    {
        var item = Item();
        item.AddStock(5, Today.AddMonths(2), "LOT-A");
        item.AddStock(5, Today.AddMonths(9), "LOT-B");

        Assert.Equal(2, item.Batches.Count);
        Assert.Contains(item.Batches, b => b.BatchNumber == "LOT-A" && b.ExpiryDate == Today.AddMonths(2));
        Assert.Contains(item.Batches, b => b.BatchNumber == "LOT-B" && b.ExpiryDate == Today.AddMonths(9));
        Assert.Equal(10, item.CurrentStock);
    }

    // [AC-P4.3] The displayed expiry is the SOONEST one still holding stock, not the last entered — which is
    // the whole point of per-lot rows.
    [Fact]
    public void The_Relevant_Expiry_Is_The_Soonest_Not_The_Newest()
    {
        var item = Item();
        item.AddStock(5, Today.AddMonths(9), "LOT-LATER");
        item.AddStock(5, Today.AddMonths(2), "LOT-SOONER");

        Assert.Equal(Today.AddMonths(2), item.EarliestRelevantExpiry());
    }

    // [AC-P4.4] FEFO: the soonest-expiring lot is drawn down first.
    [Fact]
    public void Consumption_Draws_The_Soonest_Expiring_Lot_First()
    {
        var item = Item();
        item.AddStock(10, Today.AddMonths(9), "LOT-LATER");
        item.AddStock(4, Today.AddMonths(1), "LOT-SOONER");

        var shortfall = item.ConsumeStock(3);

        Assert.Equal(0, shortfall);
        Assert.Equal(1, item.Batches.Single(b => b.BatchNumber == "LOT-SOONER").RemainingQuantity);
        Assert.Equal(10, item.Batches.Single(b => b.BatchNumber == "LOT-LATER").RemainingQuantity);
    }

    // [AC-P4.4] EC-30 — consuming past the first lot spills into the next one, still in FEFO order.
    [Fact]
    public void Consumption_Spills_Into_The_Next_Lot_In_Expiry_Order()
    {
        var item = Item();
        item.AddStock(3, Today.AddMonths(1), "LOT-SOONER");
        item.AddStock(10, Today.AddMonths(9), "LOT-LATER");

        var shortfall = item.ConsumeStock(5);

        Assert.Equal(0, shortfall);
        Assert.Equal(0, item.Batches.Single(b => b.BatchNumber == "LOT-SOONER").RemainingQuantity);
        Assert.Equal(8, item.Batches.Single(b => b.BatchNumber == "LOT-LATER").RemainingQuantity);
        // The emptied lot no longer drives the displayed expiry — it holds nothing.
        Assert.Equal(Today.AddMonths(9), item.EarliestRelevantExpiry());
    }

    // [AC-P4.4] An undated lot sorts LAST: it can wait, whereas a dated one is on a clock.
    [Fact]
    public void An_Undated_Lot_Is_Consumed_After_Every_Dated_One()
    {
        var item = Item();
        item.AddStock(5); // no expiry
        item.AddStock(5, Today.AddMonths(3), "LOT-DATED");

        item.ConsumeStock(5);

        Assert.Equal(0, item.Batches.Single(b => b.BatchNumber == "LOT-DATED").RemainingQuantity);
        Assert.Equal(5, item.Batches.Single(b => b.BatchNumber == null).RemainingQuantity);
    }

    /// <summary>
    /// [AC-P4.12] The rule that matters most: a shortfall NEVER blocks and is NEVER clamped. The clinical work
    /// has already happened, so on-hand goes negative and the discrepancy is reported — clamping to zero would
    /// silently erase it and make the ledger un-reconcilable, which is the one outcome AC-P4.19 forbids.
    /// </summary>
    [Fact]
    public void Consuming_More_Than_Is_On_The_Shelf_Reports_The_Shortfall_And_Goes_Negative()
    {
        var item = Item();
        item.AddStock(2, Today.AddMonths(3), "LOT-A");

        var shortfall = item.ConsumeStock(5);

        Assert.Equal(3, shortfall);
        Assert.Equal(-3, item.CurrentStock);
        Assert.Equal(0, item.Batches.Single().RemainingQuantity);
    }

    // [AC-P4.7] Both fields stay optional: an item whose lots carry no expiry behaves exactly as before.
    [Fact]
    public void An_Item_With_No_Expiry_Anywhere_Reports_No_Date_And_No_Warning()
    {
        var item = Item();
        item.AddStock(10);

        Assert.Null(item.EarliestRelevantExpiry());
        Assert.False(item.HasExpiredStock(Today));
        Assert.False(item.HasStockExpiringSoon(Today, 30));
    }

    // [AC-P4.5] At OR past the date counts as expired — "expires today" is not usable tomorrow.
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void Expiry_Is_Inclusive_Of_Today(int dayOffset, bool expectedExpired)
    {
        var item = Item();
        item.AddStock(5, Today.AddDays(dayOffset), "LOT-A");

        Assert.Equal(expectedExpired, item.HasExpiredStock(Today));
    }

    // [AC-P4.5] An expired lot that has been fully consumed is no longer a warning — there is nothing on the
    // shelf to throw away.
    [Fact]
    public void An_Emptied_Expired_Lot_Stops_Being_Reported()
    {
        var item = Item();
        item.AddStock(2, Today.AddDays(-5), "LOT-EXPIRED");
        Assert.True(item.HasExpiredStock(Today));

        item.ConsumeStock(2);

        Assert.False(item.HasExpiredStock(Today));
    }

    // [AC-P4.6] Approaching expiry is bounded by the lead time on both sides: already-expired is not
    // "expiring soon", and beyond the window is not either.
    [Theory]
    [InlineData(10, true)]
    [InlineData(30, true)]
    [InlineData(45, false)]
    [InlineData(-1, false)]
    public void Expiring_Soon_Respects_The_Lead_Time_Window(int dayOffset, bool expected)
    {
        var item = Item();
        item.AddStock(5, Today.AddDays(dayOffset), "LOT-A");

        Assert.Equal(expected, item.HasStockExpiringSoon(Today, 30));
    }

    /// <summary>
    /// [AC-P4.15] <c>SetCurrentStock</c> returns the SIGNED delta. It used to return void, which is exactly why
    /// its only caller wrote no <c>StockMovement</c> and Σ movements stopped reconciling with on-hand.
    /// </summary>
    [Theory]
    [InlineData(10, 4, -6)]
    [InlineData(10, 15, 5)]
    [InlineData(10, 10, 0)]
    public void SetCurrentStock_Returns_The_Signed_Delta(int start, int corrected, int expectedDelta)
    {
        var item = Item();
        item.AddStock(start, Today.AddMonths(6), "LOT-A");

        var delta = item.SetCurrentStock(corrected);

        Assert.Equal(expectedDelta, delta);
        Assert.Equal(corrected, item.CurrentStock);
    }

    // [AC-P4.15] A stock-take must leave the lots agreeing with on-hand, or the batch rows and CurrentStock
    // drift apart the first time one runs.
    [Fact]
    public void A_Downward_Correction_Draws_The_Lots_Down_Too()
    {
        var item = Item();
        item.AddStock(10, Today.AddMonths(6), "LOT-A");

        item.SetCurrentStock(4);

        Assert.Equal(4, item.CurrentStock);
        Assert.Equal(4, item.Batches.Sum(b => b.RemainingQuantity));
    }

    // An upward correction becomes an undated correction lot, so the totals still agree and FEFO is unaffected.
    [Fact]
    public void An_Upward_Correction_Adds_An_Undated_Lot()
    {
        var item = Item();
        item.AddStock(10, Today.AddMonths(6), "LOT-A");

        item.SetCurrentStock(13);

        Assert.Equal(13, item.CurrentStock);
        Assert.Equal(13, item.Batches.Sum(b => b.RemainingQuantity));
        Assert.Contains(item.Batches, b => b.ExpiryDate == null && b.RemainingQuantity == 3);
    }

    /// <summary>
    /// [AC-P4.8] The migration's opening batch describes stock that is ALREADY counted in
    /// <c>CurrentStock</c>, so it must not go through <c>AddStock</c> (which increments). That is the whole
    /// reason <c>AttachExistingBatch</c> exists.
    /// </summary>
    [Fact]
    public void Attaching_An_Existing_Batch_Does_Not_Double_The_On_Hand_Total()
    {
        var item = Item();
        item.AddStock(7, Today.AddMonths(4), "LOT-A");
        var before = item.CurrentStock;

        item.AttachExistingBatch(new StockBatch(Guid.NewGuid(), item.Id, 7, Today.AddMonths(4), "OPENING"));

        Assert.Equal(before, item.CurrentStock);
        Assert.Equal(2, item.Batches.Count);
    }

    // RemoveStock keeps its throwing contract (an explicit manual sortie should be refused), while
    // ConsumeStock — the act-driven path — deliberately does not. The two must not be conflated.
    [Fact]
    public void RemoveStock_Still_Refuses_To_Oversell()
    {
        var item = Item();
        item.AddStock(2);

        Assert.Throws<InvalidOperationException>(() => item.RemoveStock(5));
        Assert.Equal(2, item.CurrentStock);
    }
}
