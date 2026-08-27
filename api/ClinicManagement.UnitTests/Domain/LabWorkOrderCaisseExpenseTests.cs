using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Receiving a prothèse is money leaving the cabinet, so la caisse learns of it at the arrival rather than
/// waiting for somebody to remember to file it. <c>NeedsCaisseExpense</c> is the whole rule, and it lives on the
/// aggregate so that any later path which receives a bon — a job, an import, a second command — cannot implement
/// half of it.
///
/// <para>The case that pays for this class is the <b>re-arrival</b>: « Reçu » → « En cours » → « Reçu » is a legal
/// round trip (a piece that arrives wrong goes back to the lab) and <c>ReceivedDate</c> is deliberately
/// re-stamped each time. A rule keyed on the transition alone would charge one crown to la caisse twice, and the
/// day's net would silently be wrong.</para>
/// </summary>
public class LabWorkOrderCaisseExpenseTests
{
    private static LabWorkOrder NewOrder(decimal? cost) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Labo Dentaire Tunis",
        "Couronne céramo-métallique",
        toothNumber: 16,
        cost: cost);

    /// <summary>Walk to « Reçu » through legal steps only, so the fixture cannot beg the question.</summary>
    private static LabWorkOrder ReceivedOrder(decimal? cost)
    {
        var order = NewOrder(cost);
        order.SetStatus(LabOrderStatus.InProgress);
        order.SetStatus(LabOrderStatus.Received);
        return order;
    }

    [Fact]
    public void A_Received_Bon_With_A_Cost_Owes_The_Caisse_A_Depense()
    {
        var order = ReceivedOrder(350m);

        Assert.True(order.NeedsCaisseExpense);
    }

    // Nothing is owed before the work is in: the cabinet has not taken delivery, so it has not bought anything.
    [Theory]
    [InlineData(LabOrderStatus.Sent)]
    [InlineData(LabOrderStatus.InProgress)]
    public void A_Bon_Still_At_The_Lab_Owes_Nothing(LabOrderStatus status)
    {
        var order = NewOrder(350m);
        if (status == LabOrderStatus.InProgress)
        {
            order.SetStatus(LabOrderStatus.InProgress);
        }

        Assert.False(order.NeedsCaisseExpense);
    }

    // `Expense` refuses an amount ≤ 0, so a bon with no coût has nothing postable. The UI says so out loud
    // rather than reporting a dépense that was never filed.
    [Fact]
    public void A_Received_Bon_With_No_Cost_Owes_Nothing()
    {
        Assert.False(ReceivedOrder(null).NeedsCaisseExpense);
    }

    // A coût of exactly zero is not « pas de coût » — the constructor accepts it — but it is still unpostable,
    // and `Expense` would throw on it. The guard is `> 0`, not `!= null`.
    [Fact]
    public void A_Received_Bon_Costing_Zero_Owes_Nothing()
    {
        Assert.False(ReceivedOrder(0m).NeedsCaisseExpense);
    }

    [Fact]
    public void Linking_The_Depense_Settles_The_Debt()
    {
        var order = ReceivedOrder(350m);

        order.LinkExpense(Guid.NewGuid());

        Assert.NotNull(order.ExpenseId);
        Assert.False(order.NeedsCaisseExpense);
    }

    // THE regression this column exists for: the piece came back wrong, went back to the lab and arrived again.
    // The laboratory is paid once.
    [Fact]
    public void A_Second_Arrival_Does_Not_Charge_The_Caisse_Again()
    {
        var order = ReceivedOrder(350m);
        order.LinkExpense(Guid.NewGuid());

        order.SetStatus(LabOrderStatus.InProgress);
        order.SetStatus(LabOrderStatus.Received);

        Assert.Equal(LabOrderStatus.Received, order.Status);
        Assert.False(order.NeedsCaisseExpense);
    }

    // Fitting the piece is not a second purchase either, and neither is any state after the arrival.
    [Fact]
    public void Fitting_A_Posted_Bon_Owes_Nothing()
    {
        var order = ReceivedOrder(350m);
        order.LinkExpense(Guid.NewGuid());

        order.SetStatus(LabOrderStatus.Fitted);

        Assert.False(order.NeedsCaisseExpense);
    }

    // A bon whose coût was only entered after it arrived is still owed — the debt is keyed on the link, not on
    // the moment of the transition, so filling the cost in and re-saving does not lose the dépense.
    [Fact]
    public void A_Cost_Entered_After_The_Arrival_Still_Owes()
    {
        var order = ReceivedOrder(null);
        Assert.False(order.NeedsCaisseExpense);

        order.UpdateDetails(
            "Labo Dentaire Tunis",
            "Couronne céramo-métallique",
            toothNumber: 16,
            sentDate: null,
            expectedDate: null,
            cost: 350m,
            notes: null);

        Assert.True(order.NeedsCaisseExpense);
    }
}
